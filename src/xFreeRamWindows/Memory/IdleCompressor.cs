// Developer: Jose Rodriguez Arroyo
// Email: jrpcone@gmail.com
// GitHub: https://github.com/jorodriguezpr

using System.Diagnostics;

namespace xFreeRamWindows.Memory;

internal enum IdleRule { Default, Never, Sooner }

internal sealed record IdleAppStatus(string Name, string Label, int Processes, long Ram, string State, IdleRule Rule);

/// <summary>
/// "Compress idle apps" - the RAM Doubler idea done with Windows' own engine, no driver.
///
/// <para>Every minute it looks at the apps in your session. An app counts as <b>in use</b> while one of its
/// windows is in front or its processes used more than 1 second of CPU in the last minute. Once an app has
/// been idle for the chosen minutes:</para>
/// <list type="bullet">
/// <item>its memory priority is set to low, so Windows compresses or reuses its pages before those of the
/// app you are working in (a documented per-process setting, put back when the app is used again);</item>
/// <item>its working set is trimmed once, so its idle pages move to the compressed store instead of
/// staying uncompressed in RAM;</item>
/// <item>optionally efficiency mode (EcoQoS) is turned on for it, also undone when it is used again.</item>
/// </list>
/// <para>Never touched: Windows' own protected processes, other sessions (services), this app, the Boost
/// target, the app in front, and apps you mark "Never". Nothing is closed and no service is stopped.
/// Everything changed is put back when the feature is turned off or the tray app exits.</para>
/// </summary>
internal sealed class IdleCompressor
{
    public const int CheckSeconds = 60;
    private const double ActiveCpuMsPerMinute = 1000;
    private const int SoonerMinutes = 2;
    private const long RegrowBytes = 200L * 1024 * 1024;

    private sealed class AppState
    {
        public DateTime LastActiveUtc;
        public TimeSpan LastCpu;
        public bool Compressed;
        public DateTime CompressedAt;
        public long RamAfterTrim;
    }

    private readonly AppSettings _settings;
    private readonly Dictionary<string, AppState> _apps = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Processes we changed: pid -> (original memory priority, efficiency mode set by us).</summary>
    private readonly Dictionary<int, (uint Priority, bool Eco)> _changed = new();
    private readonly object _lock = new();
    private DateTime _lastCheckUtc = DateTime.MinValue;

    public DateTime? LastCompressAt { get; private set; }
    internal int Restored { get; private set; }
    public int CompressedApps
    {
        get { lock (_lock) return _apps.Values.Count(a => a.Compressed); }
    }

    public IdleCompressor(AppSettings settings) => _settings = settings;

    /// <summary>Self-test only: when set, every other app is ignored.</summary>
    internal HashSet<string>? OnlyApps { get; set; }

    public static IdleRule RuleFor(AppSettings s, string name) =>
        s.IdleRules.TryGetValue(name, out var r) && Enum.TryParse<IdleRule>(r, true, out var rule) ? rule : IdleRule.Default;

    /// <summary>Runs on a background thread from the tray timer.</summary>
    public void Check()
    {
        lock (_lock)
        {
            if (!_settings.IdleCompressEnabled)
            {
                if (_changed.Count > 0) RestoreAllLocked();
                _apps.Clear();
                return;
            }

            var now = DateTime.UtcNow;
            double minutes = _lastCheckUtc == DateTime.MinValue ? 1 : Math.Clamp((now - _lastCheckUtc).TotalMinutes, 0.25, 10);
            _lastCheckUtc = now;

            int fgPid = NativeMethods.GetForegroundProcessId();
            int mySession = Process.GetCurrentProcess().SessionId;
            string? targetName = string.IsNullOrEmpty(_settings.TargetExePath) ? null : Path.GetFileNameWithoutExtension(_settings.TargetExePath);

            var groups = new Dictionary<string, List<Process>>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.SessionId != mySession || p.Id == Environment.ProcessId ||
                        (OnlyApps is not null && !OnlyApps.Contains(p.ProcessName))) { p.Dispose(); continue; }
                    if (!groups.TryGetValue(p.ProcessName, out var list)) groups[p.ProcessName] = list = new List<Process>();
                    list.Add(p);
                }
                catch
                {
                    p.Dispose();
                }
            }

            try
            {
                foreach (var (name, procs) in groups)
                {
                    var rule = RuleFor(_settings, name);
                    bool excluded = rule == IdleRule.Never || ProtectedList.IsProtectedProcess(name) ||
                                    (targetName is not null && name.Equals(targetName, StringComparison.OrdinalIgnoreCase)) ||
                                    name.Equals("xFreeRamWindows", StringComparison.OrdinalIgnoreCase);

                    var state = _apps.TryGetValue(name, out var st) ? st : _apps[name] = new AppState { LastActiveUtc = now, LastCpu = CpuOf(procs) };
                    var cpu = CpuOf(procs);
                    double usedMs = Math.Max(0, (cpu - state.LastCpu).TotalMilliseconds);
                    state.LastCpu = cpu;
                    bool front = procs.Any(p => p.Id == fgPid);
                    bool active = front || usedMs > ActiveCpuMsPerMinute * minutes;

                    if (excluded || active)
                    {
                        if (active) state.LastActiveUtc = now;
                        if (state.Compressed || procs.Any(p => _changed.ContainsKey(p.Id)))
                        {
                            foreach (var p in procs) Restore(p.Id);
                            state.Compressed = false;
                            Log($"{name}: in use again - memory priority and efficiency mode put back");
                        }
                        continue;
                    }

                    var wait = TimeSpan.FromMinutes(rule == IdleRule.Sooner ? SoonerMinutes : Math.Max(1, _settings.IdleCompressMinutes));
                    if (now - state.LastActiveUtc < wait) continue;

                    long ram = procs.Sum(p => SafeRam(p));
                    bool regrew = state.Compressed && ram - state.RamAfterTrim > RegrowBytes;
                    bool newProcs = state.Compressed && procs.Any(p => !_changed.ContainsKey(p.Id));
                    if (state.Compressed && !regrew && !newProcs) continue;

                    int done = 0;
                    foreach (var p in procs)
                        if (CompressProcess(p.Id, trim: !state.Compressed || regrew)) done++;

                    long after = procs.Sum(p => SafeRam(p));
                    if (done > 0)
                    {
                        if (!state.Compressed) LastCompressAt = DateTime.Now;
                        Log($"{name}: idle {(now - state.LastActiveUtc).TotalMinutes:N0} min - {done}/{procs.Count} processes set to low memory priority" +
                            (!state.Compressed || regrew ? $", trimmed {MemorySnapshot.Mb(ram)} -> {MemorySnapshot.Mb(after)}" : "") +
                            (_settings.IdleEfficiencyMode ? ", efficiency mode on" : ""));
                        state.Compressed = true;
                        state.CompressedAt = DateTime.Now;
                        state.RamAfterTrim = after;
                    }
                }

                // forget apps that closed and processes that exited
                foreach (var gone in _apps.Keys.Where(k => !groups.ContainsKey(k)).ToList()) _apps.Remove(gone);
                var alive = new HashSet<int>(groups.Values.SelectMany(l => l).Select(p => p.Id));
                foreach (var pid in _changed.Keys.Where(pid => !alive.Contains(pid)).ToList()) _changed.Remove(pid);
            }
            finally
            {
                foreach (var p in groups.Values.SelectMany(l => l)) p.Dispose();
            }
        }
    }

    /// <summary>For the dashboard: every app in the session with what is happening to it.</summary>
    public List<IdleAppStatus> Status()
    {
        var apps = AppMemoryUsage.GetApps();
        int mySession = Process.GetCurrentProcess().SessionId;
        var now = DateTime.UtcNow;
        var list = new List<IdleAppStatus>();
        lock (_lock)
        {
            foreach (var a in apps.Where(a => a.SessionId == mySession && a.Name != "xFreeRamWindows"))
            {
                var rule = RuleFor(_settings, a.Name);
                string state;
                if (ProtectedList.IsProtectedProcess(a.Name)) state = "Windows component - never touched";
                else if (rule == IdleRule.Never) state = "Never touched (your rule)";
                else if (!_settings.IdleCompressEnabled) state = "-";
                else if (_apps.TryGetValue(a.Name, out var st))
                    state = st.Compressed ? $"Compressed since {st.CompressedAt:HH:mm}"
                          : now - st.LastActiveUtc < TimeSpan.FromMinutes(1.5) ? "In use"
                          : $"Idle {(now - st.LastActiveUtc).TotalMinutes:N0} min";
                else state = "Checking...";
                list.Add(new IdleAppStatus(a.Name, a.Label, a.Processes, a.Ram, state, rule));
            }
        }
        return list.OrderByDescending(x => x.Ram).ToList();
    }

    /// <summary>Puts every changed process back (feature turned off, app exits).</summary>
    public void RestoreAll()
    {
        lock (_lock) RestoreAllLocked();
    }

    private void RestoreAllLocked()
    {
        foreach (var pid in _changed.Keys.ToList()) Restore(pid);
        foreach (var a in _apps.Values) a.Compressed = false;
        Log("put back memory priority / efficiency mode for all apps");
    }

    /// <summary>Current memory priority of a process (1-5), or 0 if it can't be read. Used by the self-test.</summary>
    internal static unsafe uint ReadMemoryPriority(int pid)
    {
        var h = MemoryNative.OpenProcess(MemoryNative.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return 0;
        try
        {
            uint prio = 0;
            return MemoryNative.GetProcessInformation(h, MemoryNative.ProcessMemoryPriority, &prio, sizeof(uint)) ? prio : 0;
        }
        finally
        {
            MemoryNative.CloseHandle(h);
        }
    }

    private unsafe bool CompressProcess(int pid, bool trim)
    {
        var h = MemoryNative.OpenProcess(MemoryNative.PROCESS_SET_INFORMATION | MemoryNative.PROCESS_QUERY_INFORMATION | MemoryNative.PROCESS_SET_QUOTA, false, pid);
        if (h == IntPtr.Zero) return false;
        try
        {
            if (!_changed.ContainsKey(pid))
            {
                uint original = MemoryNative.MEMORY_PRIORITY_NORMAL;
                MemoryNative.GetProcessInformation(h, MemoryNative.ProcessMemoryPriority, &original, sizeof(uint));
                uint low = MemoryNative.MEMORY_PRIORITY_LOW;
                if (original > low && !MemoryNative.SetProcessInformation(h, MemoryNative.ProcessMemoryPriority, &low, sizeof(uint)))
                    return false;

                bool eco = false;
                if (_settings.IdleEfficiencyMode)
                {
                    var t = new MemoryNative.PROCESS_POWER_THROTTLING_STATE
                    {
                        Version = 1,
                        ControlMask = MemoryNative.PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                        StateMask = MemoryNative.PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                    };
                    eco = MemoryNative.SetProcessInformation(h, MemoryNative.ProcessPowerThrottling, &t, (uint)sizeof(MemoryNative.PROCESS_POWER_THROTTLING_STATE));
                }
                _changed[pid] = (original, eco);
            }
            if (trim) MemoryNative.EmptyWorkingSet(h);
            return true;
        }
        finally
        {
            MemoryNative.CloseHandle(h);
        }
    }

    private unsafe void Restore(int pid)
    {
        if (!_changed.Remove(pid, out var orig)) return;
        Restored++;
        var h = MemoryNative.OpenProcess(MemoryNative.PROCESS_SET_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return;   // exited
        try
        {
            uint prio = orig.Priority is >= 1 and <= 5 ? orig.Priority : MemoryNative.MEMORY_PRIORITY_NORMAL;
            MemoryNative.SetProcessInformation(h, MemoryNative.ProcessMemoryPriority, &prio, sizeof(uint));
            if (orig.Eco)
            {
                // ControlMask 0 = let Windows decide again (not "never throttle")
                var t = new MemoryNative.PROCESS_POWER_THROTTLING_STATE { Version = 1, ControlMask = 0, StateMask = 0 };
                MemoryNative.SetProcessInformation(h, MemoryNative.ProcessPowerThrottling, &t, (uint)sizeof(MemoryNative.PROCESS_POWER_THROTTLING_STATE));
            }
        }
        finally
        {
            MemoryNative.CloseHandle(h);
        }
    }

    private static TimeSpan CpuOf(List<Process> procs)
    {
        var total = TimeSpan.Zero;
        foreach (var p in procs)
        {
            try { total += p.TotalProcessorTime; } catch { /* no access or exited */ }
        }
        return total;
    }

    private static long SafeRam(Process p)
    {
        try { p.Refresh(); return p.WorkingSet64; } catch { return 0; }
    }

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "xFreeRamWindows", "idle.log");

    private static void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            var info = new FileInfo(LogPath);
            if (info.Exists && info.Length > 256 * 1024) File.Move(LogPath, LogPath + ".old", overwrite: true);
            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch
        {
            // logging must never break the tray app
        }
    }
}

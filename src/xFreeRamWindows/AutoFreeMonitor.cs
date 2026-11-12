// Developer: Jose Rodriguez Arroyo
// Email: jrpcone@gmail.com
// GitHub: https://github.com/jorodriguezpr

using System.Diagnostics;
using xFreeRamWindows.Models;

namespace xFreeRamWindows;

/// <summary>
/// Auto Free RAM: watches memory while the tray app runs and, when it gets tight, runs the
/// same pass as "Free Up RAM Now" by itself.
///
/// <para><b>Safety rules (deliberate):</b></para>
/// <list type="bullet">
/// <item>Only <see cref="BoostEngine.FreeUpRamNow"/> is used — working-set trim plus the
/// standby purge, which is already skipped when VBS/HVCI is on (the 0x18B bugcheck cause).
/// Auto mode never closes processes and never stops services; that stays a manual,
/// confirmed Boost Now action.</item>
/// <item>It runs only when memory use is at or above the threshold, and never more often
/// than the chosen interval. Trimming too often makes Windows page memory back in, which
/// is slower than not trimming at all.</item>
/// <item>If a run frees little (&lt; <see cref="SmallGainMB"/>), the wait doubles (up to an
/// hour) — memory is genuinely in use and repeating would only cause churn. It resets once
/// a run frees a useful amount or memory use drops below the threshold.</item>
/// <item>The window the user is working in, the Boost target app and this app are never
/// trimmed, so what the user is doing doesn't stutter.</item>
/// <item>Skipped while the CPU is very busy (≥ <see cref="BusyCpuPercent"/>%); tried again
/// on the next check.</item>
/// </list>
/// </summary>
internal sealed class AutoFreeMonitor
{
    public const int CheckSeconds = 30;
    private const long SmallGainMB = 100;
    private const int MaxBackoffMinutes = 60;
    private const double BusyCpuPercent = 90;

    private readonly AppSettings _settings;
    private bool _running;
    private DateTime _lastRunUtc = DateTime.MinValue;
    private int _backoffMinutes;

    public DateTime? LastRunAt { get; private set; }
    public FreeRamResult? LastResult { get; private set; }

    /// <summary>Raised on the UI thread after an automatic pass finished.</summary>
    public event Action<FreeRamResult>? Freed;

    public AutoFreeMonitor(AppSettings settings) => _settings = settings;

    /// <summary>Minutes to wait between runs right now (interval, or longer after small gains).</summary>
    public int CurrentWaitMinutes => Math.Max(_settings.AutoFreeIntervalMinutes, _backoffMinutes);

    /// <summary>Called from the UI timer every <see cref="CheckSeconds"/> seconds. Cheap when nothing to do.</summary>
    public async void Check(double cpuPercent)
    {
        if (!_settings.AutoFreeEnabled || _running) return;

        var mem = SystemStats.GetMemoryStatus();
        if (mem.TotalMB == 0) return;

        if (mem.MemoryLoadPercent < _settings.AutoFreeThresholdPercent)
        {
            _backoffMinutes = 0;   // memory is fine again — next tight moment gets the normal interval
            return;
        }

        if (DateTime.UtcNow - _lastRunUtc < TimeSpan.FromMinutes(CurrentWaitMinutes)) return;

        if (cpuPercent >= BusyCpuPercent)
        {
            Log($"skipped: CPU busy {cpuPercent:F0}% (RAM in use {mem.MemoryLoadPercent}%)");
            return;
        }

        _running = true;
        _lastRunUtc = DateTime.UtcNow;
        try
        {
            var skip = ProtectedProcessIds();
            var loadBefore = mem.MemoryLoadPercent;
            var result = await Task.Run(() => BoostEngine.FreeUpRamNow(skip));

            var wait = CurrentWaitMinutes;
            _backoffMinutes = result.FreedMB < SmallGainMB ? Math.Min(MaxBackoffMinutes, wait * 2) : 0;

            LastRunAt = DateTime.Now;
            LastResult = result;
            Log($"freed {result.FreedMB:N0} MB (RAM in use {loadBefore}% -> {SystemStats.GetMemoryStatus().MemoryLoadPercent}%), " +
                $"trimmed {result.ProcessesTrimmed}, standby purged: {result.StandbyListPurged}, next wait {CurrentWaitMinutes} min");
            Freed?.Invoke(result);
        }
        catch (Exception ex)
        {
            Log($"error: {ex.Message}");
        }
        finally
        {
            _running = false;
        }
    }

    private HashSet<int> ProtectedProcessIds()
    {
        var ids = new HashSet<int> { Environment.ProcessId };
        var fg = NativeMethods.GetForegroundProcessId();
        if (fg > 0) ids.Add(fg);

        if (!string.IsNullOrEmpty(_settings.TargetExePath))
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(_settings.TargetExePath)))
                {
                    using (p) ids.Add(p.Id);
                }
            }
            catch
            {
                // Can't list the target — it just gets trimmed like the rest.
            }
        }
        return ids;
    }

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "xFreeRamWindows", "autofree.log");

    /// <summary>Small log (%APPDATA%\xFreeRamWindows\autofree.log, ~256 KB, one old copy kept).</summary>
    private static void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            var info = new FileInfo(LogPath);
            if (info.Exists && info.Length > 256 * 1024)
            {
                File.Move(LogPath, LogPath + ".old", overwrite: true);
            }
            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never break the tray app.
        }
    }
}

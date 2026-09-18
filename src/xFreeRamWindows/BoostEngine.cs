// Developer: Jose Rodriguez Arroyo
// Email: jrpcone@gmail.com
// GitHub: https://github.com/jorodriguezpr

using System.Diagnostics;
using System.ServiceProcess;
using xFreeRamWindows.Models;

namespace xFreeRamWindows;

/// <summary>
/// Core "free up memory" logic.
///
/// <para><b>History:</b> Boost Now crashed the machine with an identical bugcheck (0x18B,
/// params 0x121/0xC0000018/0x476663500000030b/0x0) seven times running, across three
/// completely different automatic service-stopping strategies and after all memory
/// operations (working-set trim, standby-list purge) were already removed from this path.
/// An 8th run stopped crashing once Boost Now was cut down to services-only (no process-
/// closing, no priority-raise) — isolating process-closing/priority-raising as the actual
/// trigger, not services or memory. That services-only run also froze very little RAM,
/// since the automatic classifiers tried (Safe Mode essential list, then a Microsoft-
/// publisher check) excluded real memory users like Windows Search/Superfetch alongside the
/// genuinely critical services. A 9th run with fully user-selected services (see below) ran
/// clean but still didn't free much, since process-closing — often the bigger RAM win — was
/// still disabled.</para>
///
/// <para><b>Current design: both services and processes are entirely user-selected, never
/// automatically classified.</b> The user picks exactly which services are stoppable via
/// <see cref="ServiceSelectionForm"/> (<see cref="AppSettings.ServicesToStop"/>) and which
/// processes are closeable via <see cref="ProcessSelectionForm"/>
/// (<see cref="AppSettings.ProcessesToStop"/>). This reintroduces process-closing — the
/// confirmed prior crash trigger — but in a fundamentally different, much narrower shape:
/// the old design closed every running process except a protected list (unbounded blast
/// radius); this one closes only the specific processes the user explicitly checked
/// (bounded to whatever they picked). Needs its own real-world test before being trusted —
/// do not assume the isolation finding above still applies unchanged to this new shape.
/// Priority-raising remains disabled (not requested, not reintroduced here).</para>
///
/// <para>Regardless of what the user selects, <see cref="SafeBootServices"/> (Safe Mode with
/// Networking's essential set) and <see cref="ProtectedList"/> are still re-checked at stop
/// time as hard vetoes for services; <see cref="ProtectedList"/>, the current process, and
/// the target app's own process are hard vetoes for process-closing. None of these can be
/// overridden by what's saved in settings. Boost Now never touches system memory directly —
/// that's <see cref="FreeUpRamNow"/>'s job, deliberately kept separate. See README for why.</para>
/// </summary>
internal static class BoostEngine
{
    public static BoostSnapshot? CurrentSnapshot { get; private set; }

    public static BoostResult Boost(
        string targetExePath, IReadOnlyCollection<string> servicesToStop, IReadOnlyCollection<string> processesToStop)
    {
        var result = new BoostResult();
        var targetProcessName = Path.GetFileNameWithoutExtension(targetExePath);

        EnsureTargetRunning(targetExePath, targetProcessName, result);

        StopSelectedProcesses(processesToStop, targetProcessName, result);

        var snapshot = new BoostSnapshot { TargetProcessName = targetProcessName };
        StopSelectedServices(servicesToStop, snapshot, result);
        CurrentSnapshot = snapshot;

        return result;
    }

    public static RestoreResult RestoreAll()
    {
        var result = new RestoreResult();
        var snapshot = CurrentSnapshot;
        if (snapshot is null) return result;

        foreach (var serviceName in snapshot.StoppedServiceNames)
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status is ServiceControllerStatus.Stopped)
                {
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                }
                result.ServicesRestarted++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Service '{serviceName}': {ex.Message}");
            }
        }

        try
        {
            foreach (var p in Process.GetProcessesByName(snapshot.TargetProcessName))
            {
                using (p)
                {
                    if (!p.HasExited) p.PriorityClass = ProcessPriorityClass.Normal;
                }
            }
        }
        catch
        {
            // Target already exited or priority not accessible — nothing to restore.
        }

        CurrentSnapshot = null;
        return result;
    }

    /// <summary>
    /// Plain "free up RAM" pass: trims every process's working set and purges the standby
    /// list. Doesn't close anything or touch services — safe to run any time, no confirmation
    /// needed, unlike <see cref="Boost"/>.
    /// </summary>
    public static FreeRamResult FreeUpRamNow()
    {
        var before = SystemStats.GetMemoryStatus().AvailableMB;
        var result = new FreeRamResult();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    NativeMethods.TryTrimWorkingSet(process.Handle);
                    result.ProcessesTrimmed++;
                }
                catch
                {
                    // No access to processes we don't own/aren't elevated enough for — skip.
                }
            }
        }

        result.StandbyListPurged = SystemMemoryManager.TryPurgeStandbyList();

        var after = SystemStats.GetMemoryStatus().AvailableMB;
        result.FreedMB = (long)after - (long)before;
        return result;
    }

    private static void EnsureTargetRunning(string targetExePath, string targetProcessName, BoostResult result)
    {
        if (Process.GetProcessesByName(targetProcessName).Length > 0) return;
        if (!File.Exists(targetExePath)) return;

        try
        {
            Process.Start(new ProcessStartInfo(targetExePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Could not launch target app: {ex.Message}");
        }
    }

    private static void StopSelectedProcesses(
        IReadOnlyCollection<string> processNamesToStop, string targetProcessName, BoostResult result)
    {
        if (processNamesToStop.Count == 0) return;

        var selected = new HashSet<string>(processNamesToStop, StringComparer.OrdinalIgnoreCase);
        var currentProcessId = Environment.ProcessId;

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == currentProcessId) continue;
                if (!selected.Contains(process.ProcessName)) continue;
                if (string.Equals(process.ProcessName, targetProcessName, StringComparison.OrdinalIgnoreCase)) continue;
                if (ProtectedList.IsProtectedProcess(process.ProcessName)) continue;

                try
                {
                    if (!process.CloseMainWindow())
                    {
                        process.Kill();
                    }
                    else if (!process.WaitForExit(2000))
                    {
                        process.Kill();
                    }
                    result.ProcessesStopped++;
                }
                catch (Exception ex)
                {
                    // Expected for elevated/system-owned processes we're not allowed to touch.
                    result.Errors.Add($"Process '{process.ProcessName}' (pid {process.Id}): {ex.Message}");
                }
            }
        }
    }

    private static void StopSelectedServices(
        IReadOnlyCollection<string> servicesToStop, BoostSnapshot snapshot, BoostResult result)
    {
        if (servicesToStop.Count == 0) return;

        // Re-verify the essential set at stop time regardless of what the picker showed —
        // if we can't reliably determine what's essential, fail safe: stop nothing rather
        // than risk taking down something critical.
        if (!SafeBootServices.TryGetEssentialServiceNames(out var essentialNames))
        {
            result.Errors.Add(
                "Could not verify Safe Mode's essential-service list from the registry — " +
                "skipped service stopping entirely this run for safety.");
            return;
        }

        var selected = new HashSet<string>(servicesToStop, StringComparer.OrdinalIgnoreCase);

        foreach (var sc in ServiceController.GetServices())
        {
            using (sc)
            {
                if (!selected.Contains(sc.ServiceName)) continue;
                if (sc.Status is not (ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)) continue;
                if (essentialNames.Contains(sc.ServiceName)) continue;
                if (ProtectedList.IsProtectedService(sc.ServiceName)) continue;
                if (!sc.CanStop) continue;

                try
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                    snapshot.StoppedServiceNames.Add(sc.ServiceName);
                    result.ServicesStopped++;
                }
                catch (Exception ex)
                {
                    // Expected: dependent services, tamper-protected services (e.g. Defender), access denied.
                    result.Errors.Add($"Service '{sc.ServiceName}': {ex.Message}");
                }
            }
        }
    }

    private static void BoostTargetPriority(string targetProcessName, BoostResult result)
    {
        foreach (var p in Process.GetProcessesByName(targetProcessName))
        {
            using (p)
            {
                try
                {
                    p.PriorityClass = ProcessPriorityClass.High;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Could not raise priority for '{targetProcessName}': {ex.Message}");
                }
            }
        }
    }
}

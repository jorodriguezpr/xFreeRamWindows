// Developer: Jose Rodriguez Arroyo
// Email: jrpcone@gmail.com
// GitHub: https://github.com/jorodriguezpr

using System.Diagnostics;

namespace xFreeRamWindows.Memory;

/// <summary>Memory use of one app, all its processes added together (VS Code runs ~20 processes).</summary>
internal sealed record AppUsage(string Name, string Label, int Processes, long Commit, long Ram, int[] Pids, int SessionId);

internal static class AppMemoryUsage
{
    /// <summary>Plain names for process names people don't recognise.</summary>
    private static readonly Dictionary<string, string> Friendly = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Code"] = "Visual Studio Code",
        ["msedge"] = "Microsoft Edge",
        ["msedgewebview2"] = "Edge WebView (used inside other apps)",
        ["chrome"] = "Google Chrome",
        ["firefox"] = "Firefox",
        ["WorkloadsSessionHost"] = "Windows AI features (search, Click to Do, Recall)",
        ["vmmemWSL"] = "WSL (Linux on Windows)",
        ["vmmem"] = "Virtual machines / WSL",
        ["MsMpEng"] = "Microsoft Defender",
        ["Memory Compression"] = "Windows memory compression store",
        ["netbeans64"] = "NetBeans",
        ["nbcode64"] = "NetBeans",
        ["java"] = "Java",
        ["javaw"] = "Java",
        ["copilotapp"] = "Microsoft Copilot app",
        ["SSMS"] = "SQL Server Management Studio",
        ["explorer"] = "Windows Explorer (desktop, taskbar)",
        ["dwm"] = "Desktop Window Manager",
        ["SearchHost"] = "Windows Search",
        ["svchost"] = "Windows services (svchost)",
        ["Teams"] = "Microsoft Teams",
        ["ms-teams"] = "Microsoft Teams",
        ["node"] = "Node.js",
        ["dotnet"] = ".NET",
        ["claude"] = "Claude",
    };

    public static string LabelFor(string processName) =>
        Friendly.TryGetValue(processName, out var label) ? label : processName;

    /// <summary>All running apps, biggest committed memory first. Cheap: one system process list, no per-process opens.</summary>
    public static List<AppUsage> GetApps()
    {
        var groups = new Dictionary<string, (int Count, long Commit, long Ram, List<int> Pids, int Session)>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.Id == 0) continue;   // Idle
                    var name = p.ProcessName;
                    groups.TryGetValue(name, out var g);
                    g.Pids ??= new List<int>();
                    g.Pids.Add(p.Id);
                    g.Count++;
                    g.Commit += p.PagedMemorySize64;   // private bytes = this process's share of the commit charge
                    g.Ram += p.WorkingSet64;
                    if (g.Count == 1) g.Session = p.SessionId;
                    groups[name] = g;
                }
                catch
                {
                    // process exited while listing
                }
            }
        }
        return groups
            .Select(kv => new AppUsage(kv.Key, LabelFor(kv.Key), kv.Value.Count, kv.Value.Commit, kv.Value.Ram, kv.Value.Pids.ToArray(), kv.Value.Session))
            .OrderByDescending(a => a.Commit)
            .ToList();
    }

    /// <summary>"VS Code 4.9 GB, Windows AI features 3.3 GB, NetBeans 2.6 GB" for the warning balloon.</summary>
    public static string TopSummary(int count = 3)
    {
        var top = GetApps().Where(a => a.Name != "System" && a.Name != "Memory Compression").Take(count);
        return string.Join(", ", top.Select(a => $"{Short(a.Label)} {MemorySnapshot.Gb(a.Commit)}"));
    }

    private static string Short(string label)
    {
        var i = label.IndexOf(" (", StringComparison.Ordinal);
        return i > 0 ? label[..i] : label;
    }
}

// Developer: Jose Rodriguez Arroyo
// Email: jrpcone@gmail.com
// GitHub: https://github.com/jorodriguezpr

using System.Management;
using System.Text.Json;

namespace xFreeRamWindows.Memory;

internal sealed record PageFileSetting(string Path, int InitialMB, int MaximumMB);
internal sealed record PageFileUsage(string Path, int AllocatedMB, int CurrentMB, int PeakMB);

internal sealed record PageFileState
{
    public bool AutomaticallyManaged { get; init; }
    public List<PageFileSetting> Settings { get; init; } = new();
    public List<PageFileUsage> Usage { get; init; } = new();
    public long RamMB { get; init; }
    public long CommitPeakMB { get; init; }
    public long CommitLimitMB { get; init; }
    public string SystemDrive { get; init; } = "C:";
    public long DriveFreeMB { get; init; }
    public long DriveSizeMB { get; init; }
    public string? Error { get; init; }
}

internal sealed record PageFileRecommendation
{
    public string Path { get; init; } = @"C:\pagefile.sys";
    public int InitialMB { get; init; }
    public int MaximumMB { get; init; }
    public bool AlreadyGood { get; init; }
    public bool LimitedByDisk { get; init; }
    public string Reason { get; init; } = "";
}

/// <summary>
/// Page file optimizer. The commit limit is RAM + page file: when programs together reserve more than
/// that, they crash or fail to start (WSL and Node did on this PC with 1.8 GB left). A bigger page file
/// raises the limit for the price of disk space; with an initial size and a larger maximum, Windows only
/// grows the file when it is really needed.
///
/// <para>Rule: initial = the larger of RAM (up to 16 GB) and what the highest commit since boot needed
/// (+10 %); maximum = the larger of 2 x initial and the highest commit + 50 % - RAM; never more than 3 x RAM;
/// the drive always keeps max(20 GB, 5 %) free even with the page file at its maximum.</para>
///
/// <para>Changes go through WMI (Win32_ComputerSystem / Win32_PageFileSetting, the documented way),
/// need administrator rights, are saved to a backup first, and take effect after a restart.</para>
/// </summary>
internal static class PageFileOptimizer
{
    private const int Gb = 1024;

    private static readonly string BackupPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "xFreeRamWindows", "pagefile-backup.json");

    public static bool HasBackup => File.Exists(BackupPath);

    public static PageFileState Read()
    {
        var snap = MemorySnapshot.Take(includeStore: false);
        var drive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\');
        long free = 0, size = 0;
        try
        {
            var di = new DriveInfo(drive + "\\");
            free = di.AvailableFreeSpace / 1048576;
            size = di.TotalSize / 1048576;
        }
        catch
        {
            // drive info unavailable - recommendation then can't check disk space and says so
        }

        var state = new PageFileState
        {
            RamMB = snap.RamTotal / 1048576,
            CommitPeakMB = Math.Max(snap.CommitPeak, snap.Commit) / 1048576,
            CommitLimitMB = snap.CommitLimit / 1048576,
            SystemDrive = drive,
            DriveFreeMB = free,
            DriveSizeMB = size,
        };
        try
        {
            bool auto = false;
            using (var s = new ManagementObjectSearcher("SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem"))
            foreach (ManagementObject o in s.Get())
                using (o) auto = o["AutomaticManagedPagefile"] is true;

            var settings = new List<PageFileSetting>();
            using (var s = new ManagementObjectSearcher("SELECT Name, InitialSize, MaximumSize FROM Win32_PageFileSetting"))
            foreach (ManagementObject o in s.Get())
                using (o) settings.Add(new PageFileSetting((string)o["Name"], ToInt(o["InitialSize"]), ToInt(o["MaximumSize"])));

            var usage = new List<PageFileUsage>();
            using (var s = new ManagementObjectSearcher("SELECT Name, AllocatedBaseSize, CurrentUsage, PeakUsage FROM Win32_PageFileUsage"))
            foreach (ManagementObject o in s.Get())
                using (o) usage.Add(new PageFileUsage((string)o["Name"], ToInt(o["AllocatedBaseSize"]), ToInt(o["CurrentUsage"]), ToInt(o["PeakUsage"])));

            return state with { AutomaticallyManaged = auto, Settings = settings, Usage = usage };
        }
        catch (Exception ex)
        {
            return state with { Error = "Could not read the page file settings: " + ex.Message };
        }
    }

    public static PageFileRecommendation Recommend(PageFileState s)
    {
        var path = s.SystemDrive + @"\pagefile.sys";
        long ram = RoundUpGb(s.RamMB);
        long peak = s.CommitPeakMB;

        long initial = Math.Max(Math.Min(ram, 16 * Gb), RoundUpGb((long)(peak * 1.10) - ram));
        long maximum = Math.Max(initial * 2, RoundUpGb((long)(peak * 1.50) - ram));
        maximum = Math.Min(maximum, Math.Max(ram * 3, initial));

        // disk guard: the drive must keep max(20 GB, 5 %) free with the page file at its maximum
        bool limited = false;
        if (s.DriveSizeMB > 0)
        {
            long current = s.Usage.Where(u => u.Path.StartsWith(s.SystemDrive, StringComparison.OrdinalIgnoreCase)).Sum(u => (long)u.AllocatedMB);
            long reserve = Math.Max(20 * Gb, s.DriveSizeMB / 20);
            long room = s.DriveFreeMB + current - reserve;
            if (maximum > room)
            {
                limited = true;
                maximum = Math.Max(0, RoundDownGb(room));
                initial = Math.Min(initial, maximum);
            }
        }

        var existing = s.Settings.FirstOrDefault(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        bool good = !s.AutomaticallyManaged && existing is not null && existing.InitialMB >= initial && existing.MaximumMB >= maximum;

        string reason =
            $"Highest commit since Windows started: {s.CommitPeakMB / 1024.0:N1} GB (limit now {s.CommitLimitMB / 1024.0:N1} GB, RAM {s.RamMB / 1024.0:N1} GB). " +
            $"Start at {initial / 1024} GB and let Windows grow it up to {maximum / 1024} GB only when needed: " +
            $"that raises the commit limit to about {(s.RamMB + initial) / 1024.0:N0} GB, and up to {(s.RamMB + maximum) / 1024.0:N0} GB.";
        if (limited)
            reason += $" Limited by free space on {s.SystemDrive} (it keeps at least {Math.Max(20, s.DriveSizeMB / 20 / 1024)} GB free).";
        if (maximum < 4 * Gb)
            reason = $"Not enough free space on {s.SystemDrive} for a useful page file. Free some disk space first.";

        return new PageFileRecommendation
        {
            Path = path,
            InitialMB = (int)initial,
            MaximumMB = (int)maximum,
            AlreadyGood = good,
            LimitedByDisk = limited,
            Reason = reason,
        };
    }

    /// <summary>Sets a fixed initial/maximum size for the system drive's page file. Other drives' page files are left alone.</summary>
    public static string Apply(string path, int initialMB, int maximumMB)
    {
        if (initialMB < 1024 || maximumMB < initialMB) return "Not applied: the sizes are not valid.";
        SaveBackup(Read());
        var scope = Connect();

        using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM Win32_ComputerSystem")))
        foreach (ManagementObject cs in s.Get())
            using (cs)
            {
                if (cs["AutomaticManagedPagefile"] is true)
                {
                    cs["AutomaticManagedPagefile"] = false;
                    cs.Put();
                }
            }

        bool updated = false;
        using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM Win32_PageFileSetting")))
        foreach (ManagementObject o in s.Get())
            using (o)
            {
                if (!string.Equals((string)o["Name"], path, StringComparison.OrdinalIgnoreCase)) continue;
                o["InitialSize"] = (uint)initialMB;
                o["MaximumSize"] = (uint)maximumMB;
                o.Put();
                updated = true;
            }

        if (!updated)
        {
            using var cls = new ManagementClass(scope, new ManagementPath("Win32_PageFileSetting"), null);
            using var inst = cls.CreateInstance();
            inst["Name"] = path;
            inst["InitialSize"] = (uint)initialMB;
            inst["MaximumSize"] = (uint)maximumMB;
            inst.Put();
        }

        var after = Read();
        var check = after.Settings.FirstOrDefault(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (after.AutomaticallyManaged || check is null || check.InitialMB != initialMB || check.MaximumMB != maximumMB)
            return "Windows did not keep the new setting. Nothing else was changed.";
        return $"Saved: {path} starts at {initialMB / 1024} GB and can grow to {maximumMB / 1024} GB. Restart Windows to use it.";
    }

    /// <summary>Hands the page file back to Windows ("System managed size").</summary>
    public static string SetAutomatic()
    {
        SaveBackup(Read());
        var scope = Connect();
        using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM Win32_ComputerSystem")))
        foreach (ManagementObject cs in s.Get())
            using (cs)
            {
                cs["AutomaticManagedPagefile"] = true;
                cs.Put();
            }
        return Read().AutomaticallyManaged
            ? "Saved: Windows manages the page file size again. Restart Windows to use it."
            : "Windows did not keep the setting.";
    }

    /// <summary>Puts back the settings saved before xFreeRamWindows first changed them.</summary>
    public static string RestoreBackup()
    {
        if (!HasBackup) return "There is no saved previous setting.";
        var backup = JsonSerializer.Deserialize<PageFileState>(File.ReadAllText(BackupPath));
        if (backup is null) return "The saved setting could not be read.";

        var scope = Connect();
        using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM Win32_ComputerSystem")))
        foreach (ManagementObject cs in s.Get())
            using (cs)
            {
                cs["AutomaticManagedPagefile"] = backup.AutomaticallyManaged;
                cs.Put();
            }

        if (!backup.AutomaticallyManaged)
        {
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM Win32_PageFileSetting")))
            foreach (ManagementObject o in s.Get())
                using (o)
                {
                    var old = backup.Settings.FirstOrDefault(b => b.Path.Equals((string)o["Name"], StringComparison.OrdinalIgnoreCase));
                    if (old is null) continue;
                    o["InitialSize"] = (uint)old.InitialMB;
                    o["MaximumSize"] = (uint)old.MaximumMB;
                    o.Put();
                    done.Add(old.Path);
                }
            foreach (var old in backup.Settings.Where(b => !done.Contains(b.Path)))
            {
                using var cls = new ManagementClass(scope, new ManagementPath("Win32_PageFileSetting"), null);
                using var inst = cls.CreateInstance();
                inst["Name"] = old.Path;
                inst["InitialSize"] = (uint)old.InitialMB;
                inst["MaximumSize"] = (uint)old.MaximumMB;
                inst.Put();
            }
        }
        File.Move(BackupPath, BackupPath + ".restored", overwrite: true);
        return "Original page file setting restored. Restart Windows to use it.";
    }

    public static string Describe(PageFileState s)
    {
        if (s.Error is not null) return s.Error;
        var lines = new List<string>
        {
            s.AutomaticallyManaged ? "Size: managed by Windows (System managed size)" : "Size: set by hand",
        };
        foreach (var x in s.Settings)
            lines.Add(x.InitialMB == 0 && x.MaximumMB == 0
                ? $"{x.Path}: system managed"
                : $"{x.Path}: starts at {x.InitialMB / 1024.0:N1} GB, can grow to {x.MaximumMB / 1024.0:N1} GB");
        foreach (var u in s.Usage)
            lines.Add($"{u.Path}: {u.AllocatedMB / 1024.0:N1} GB on disk now, {u.CurrentMB / 1024.0:N1} GB in use (highest {u.PeakMB / 1024.0:N1} GB)");
        lines.Add($"Commit limit (RAM + page file): {s.CommitLimitMB / 1024.0:N1} GB; highest commit since start: {s.CommitPeakMB / 1024.0:N1} GB");
        if (s.DriveSizeMB > 0) lines.Add($"Free space on drive {s.SystemDrive} {s.DriveFreeMB / 1024.0:N0} GB of {s.DriveSizeMB / 1024.0:N0} GB");
        return string.Join(Environment.NewLine, lines);
    }

    private static ManagementScope Connect()
    {
        var scope = new ManagementScope(@"\\.\root\cimv2",
            new ConnectionOptions { EnablePrivileges = true, Impersonation = ImpersonationLevel.Impersonate });
        scope.Connect();
        return scope;
    }

    /// <summary>Keeps the setting from BEFORE the first change, so "Restore previous" always goes back to the original.</summary>
    private static void SaveBackup(PageFileState s)
    {
        if (HasBackup || s.Error is not null) return;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(BackupPath)!);
        File.WriteAllText(BackupPath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static int ToInt(object? v) => v is null ? 0 : Convert.ToInt32(v);
    private static long RoundUpGb(long mb) => mb <= 0 ? 0 : (mb + Gb - 1) / Gb * Gb;
    private static long RoundDownGb(long mb) => mb <= 0 ? 0 : mb / Gb * Gb;
}

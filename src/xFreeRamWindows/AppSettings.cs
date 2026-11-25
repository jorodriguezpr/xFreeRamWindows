// Developer: Jose Rodriguez Arroyo
// Email: jrpcone@gmail.com
// GitHub: https://github.com/jorodriguezpr

using System.Text.Json;

namespace xFreeRamWindows;

internal sealed class AppSettings
{
    public string? TargetExePath { get; set; }
    public List<string> ServicesToStop { get; set; } = new();
    public List<string> ProcessesToStop { get; set; } = new();

    // Auto Free RAM: while the app runs, free RAM by itself when memory gets tight.
    // Only the safe "Free Up RAM Now" pass is used — never closes apps or stops services.
    public bool AutoFreeEnabled { get; set; } = true;
    /// <summary>Free RAM when this percent of memory (or more) is in use.</summary>
    public int AutoFreeThresholdPercent { get; set; } = 80;
    /// <summary>Never run more often than this, however high memory use is.</summary>
    public int AutoFreeIntervalMinutes { get; set; } = 10;
    public bool AutoFreeNotify { get; set; } = false;

    // 1.2.0 Commit guard: warn before the commit limit (RAM + page file) is reached.
    public bool CommitWarnEnabled { get; set; } = true;
    public int CommitWarnPercent { get; set; } = 90;

    // 1.2.0 Compress idle apps: low memory priority + one trim for apps idle this long.
    public bool IdleCompressEnabled { get; set; } = true;
    public int IdleCompressMinutes { get; set; } = 10;
    /// <summary>Also turn on efficiency mode (EcoQoS) for idle apps. Off by default.</summary>
    public bool IdleEfficiencyMode { get; set; } = false;
    /// <summary>Per-app rules by process name: "Never" or "Sooner" (missing = Default).</summary>
    public Dictionary<string, string> IdleRules { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "xFreeRamWindows", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded is not null)
                {
                    loaded.IdleRules = new Dictionary<string, string>(loaded.IdleRules ?? new(), StringComparer.OrdinalIgnoreCase);
                    loaded.CommitWarnPercent = Math.Clamp(loaded.CommitWarnPercent, 70, 98);
                    loaded.IdleCompressMinutes = Math.Clamp(loaded.IdleCompressMinutes, 1, 120);
                    return loaded;
                }
            }
        }
        catch
        {
            // Corrupt or unreadable settings file — fall back to defaults rather than crash the tray app.
        }
        return new AppSettings();
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}

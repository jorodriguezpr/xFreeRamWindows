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
                if (loaded is not null) return loaded;
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

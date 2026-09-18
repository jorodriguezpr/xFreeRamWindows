// Developer: Jose Rodriguez Arroyo
// Email: jrpcone@gmail.com
// GitHub: https://github.com/jorodriguezpr

using System.Diagnostics;
using Microsoft.Win32;

namespace xFreeRamWindows;

/// <summary>
/// Resolves a service's binary and reads its publisher (file version info company name),
/// purely as information shown in <see cref="ServiceSelectionForm"/> — the user decides
/// what's safe to stop; this just helps them tell Microsoft-published services apart from
/// third-party ones while choosing.
/// </summary>
internal static class ServicePublisher
{
    private static readonly Dictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string GetPublisherLabel(string serviceName)
    {
        if (Cache.TryGetValue(serviceName, out var cached)) return cached;

        var label = Resolve(serviceName);
        Cache[serviceName] = label;
        return label;
    }

    private static string Resolve(string serviceName)
    {
        var exePath = TryGetExecutablePath(serviceName);
        if (exePath is null || !File.Exists(exePath)) return "Unknown";

        try
        {
            var company = FileVersionInfo.GetVersionInfo(exePath).CompanyName;
            return string.IsNullOrWhiteSpace(company) ? "Unknown" : company;
        }
        catch
        {
            return "Unknown";
        }
    }

    private static string? TryGetExecutablePath(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            var imagePath = key?.GetValue("ImagePath") as string;
            if (string.IsNullOrWhiteSpace(imagePath)) return null;

            var expanded = ExpandSystemRoot(Environment.ExpandEnvironmentVariables(imagePath));
            return ExtractExePath(expanded);
        }
        catch
        {
            return null;
        }
    }

    private static string ExpandSystemRoot(string path)
    {
        const string prefix = @"\SystemRoot\";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return path;

        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return Path.Combine(windowsDir, path[prefix.Length..]);
    }

    private static string? ExtractExePath(string imagePath)
    {
        imagePath = imagePath.Trim();

        if (imagePath.StartsWith('"'))
        {
            var end = imagePath.IndexOf('"', 1);
            if (end > 0) return imagePath[1..end];
        }

        var exeIndex = imagePath.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exeIndex > 0 ? imagePath[..(exeIndex + 4)] : null;
    }
}

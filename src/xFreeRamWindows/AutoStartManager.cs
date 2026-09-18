// Developer: Jose Rodriguez Arroyo
// Email: jrpcone@gmail.com
// GitHub: https://github.com/jorodriguezpr

using System.Diagnostics;

namespace xFreeRamWindows;

/// <summary>
/// Registers/unregisters xFreeRamWindows to start at logon via a Scheduled Task rather than
/// the HKCU Run key. The app's manifest requires administrator, and a Run-key entry for
/// an elevated app re-prompts UAC on every logon; a logon-triggered task with "run with
/// highest privileges" starts elevated silently instead.
/// </summary>
internal static class AutoStartManager
{
    private const string TaskName = "xFreeRamWindows";

    public static bool IsEnabled()
    {
        var (exitCode, _) = RunSchtasks($"/Query /TN \"{TaskName}\"");
        return exitCode == 0;
    }

    public static bool Enable()
    {
        var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
        var (exitCode, output) = RunSchtasks(
            $"/Create /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\"\" /SC ONLOGON /RL HIGHEST /F");
        return exitCode == 0 || output.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase);
    }

    public static bool Disable()
    {
        var (exitCode, _) = RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
        return exitCode == 0;
    }

    private static (int ExitCode, string Output) RunSchtasks(string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("schtasks.exe", arguments)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output);
        }
        catch
        {
            return (-1, string.Empty);
        }
    }
}

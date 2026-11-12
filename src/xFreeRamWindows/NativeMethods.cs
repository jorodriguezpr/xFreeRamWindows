// Developer: Jose Rodriguez Arroyo
// Email: jrpcone@gmail.com
// GitHub: https://github.com/jorodriguezpr

using System.Runtime.InteropServices;

namespace xFreeRamWindows;

internal static partial class NativeMethods
{
    [LibraryImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyWorkingSet(IntPtr hProcess);

    /// <summary>
    /// Forces a process to trim its working set back to the OS. Best-effort:
    /// the pages come right back if the process touches them again, but this
    /// is what reclaims the "freed" memory immediately instead of waiting for
    /// the OS to reclaim it lazily.
    /// </summary>
    public static bool TryTrimWorkingSet(IntPtr processHandle) => EmptyWorkingSet(processHandle);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>Process id of the window the user is working in right now, or 0 if unknown.</summary>
    public static int GetForegroundProcessId()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(hwnd, out var pid);
        return (int)pid;
    }
}

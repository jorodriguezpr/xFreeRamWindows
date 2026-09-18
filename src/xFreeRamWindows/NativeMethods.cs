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
}

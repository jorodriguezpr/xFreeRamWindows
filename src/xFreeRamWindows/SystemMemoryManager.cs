// Developer: Jose Rodriguez Arroyo
// Email: jrpcone@gmail.com
// GitHub: https://github.com/jorodriguezpr

using System.Runtime.InteropServices;

namespace xFreeRamWindows;

/// <summary>
/// Purges the standby list — memory Windows is holding as cached-but-reclaimable
/// (recently used file/mapped pages it hasn't handed back yet). Killing processes
/// only frees their own working sets; this is what actually pushes "available
/// physical memory" up further, the same technique dedicated RAM-cleaner tools use.
/// </summary>
internal static partial class SystemMemoryManager
{
    private const int SystemMemoryListInformation = 80;
    private const int MemoryPurgeStandbyList = 4;

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privileges;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GetCurrentProcess();

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(
        IntPtr tokenHandle, [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TOKEN_PRIVILEGES newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("ntdll.dll")]
    private static partial int NtSetSystemInformation(int systemInformationClass, ref int systemInformation, int systemInformationLength);

    /// <summary>
    /// Best-effort. Returns false if the privilege or the syscall isn't available, or if
    /// Virtualization-Based Security is active — this purge caused two real 0x18B
    /// SECURE_KERNEL_ERROR bugchecks on a VBS/HVCI-enabled machine. Never throws.
    /// </summary>
    public static bool TryPurgeStandbyList()
    {
        if (VbsDetector.IsSecureKernelActive()) return false;

        try
        {
            if (!EnableProfileSingleProcessPrivilege()) return false;
            var command = MemoryPurgeStandbyList;
            return NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int)) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool EnableProfileSingleProcessPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token))
            return false;

        try
        {
            if (!LookupPrivilegeValue(null, "SeProfileSingleProcessPrivilege", out var luid))
                return false;

            var privileges = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Privileges = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = SE_PRIVILEGE_ENABLED },
            };

            return AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            CloseHandle(token);
        }
    }
}

// Developer: Jose Rodriguez Arroyo
// Email: jrpcone@gmail.com
// GitHub: https://github.com/jorodriguezpr

using System.Runtime.InteropServices;

namespace xFreeRamWindows.Memory;

/// <summary>Windows API declarations used by the 1.2.0 memory features (read-only queries plus
/// per-process memory priority / efficiency mode, all documented, user-mode APIs).</summary>
internal static unsafe partial class MemoryNative
{
    // ── system-wide performance numbers (commit charge, commit limit, cache, kernel) ──
    [StructLayout(LayoutKind.Sequential)]
    public struct PERFORMANCE_INFORMATION
    {
        public uint cb;
        public nuint CommitTotal;
        public nuint CommitLimit;
        public nuint CommitPeak;
        public nuint PhysicalTotal;
        public nuint PhysicalAvailable;
        public nuint SystemCache;
        public nuint KernelTotal;
        public nuint KernelPaged;
        public nuint KernelNonpaged;
        public nuint PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    [LibraryImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetPerformanceInfo(out PERFORMANCE_INFORMATION info, uint size);

    // ── memory compression store (same read-only query Task Manager and System Informer use) ──
    public const int SystemStoreInformation = 109;
    public const uint MemCompressionInfoRequest = 22;

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_STORE_INFORMATION
    {
        public uint VersionAndClass;   // bits 0-7 version (1), bits 8-31 store information class
        public IntPtr Data;
        public uint Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SM_MEM_COMPRESSION_INFO_REQUEST
    {
        public uint VersionAndSpare;   // bits 0-7 version (3)
        public uint CompressionPid;
        public uint WorkingSetSize;
        public nuint TotalDataCompressed;
        public nuint TotalCompressedSize;
        public nuint TotalUniqueDataCompressed;
    }

    [LibraryImport("ntdll.dll")]
    public static partial int NtQuerySystemInformation(int infoClass, ref SYSTEM_STORE_INFORMATION info, int length, out int returnLength);

    // ── per-process access ──
    public const uint PROCESS_VM_READ = 0x0010;
    public const uint PROCESS_SET_QUOTA = 0x0100;
    public const uint PROCESS_SET_INFORMATION = 0x0200;
    public const uint PROCESS_QUERY_INFORMATION = 0x0400;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EmptyWorkingSet(IntPtr process);

    // memory priority (ProcessMemoryPriority = 0) and efficiency mode (ProcessPowerThrottling = 4)
    public const int ProcessMemoryPriority = 0;
    public const int ProcessPowerThrottling = 4;
    public const uint MEMORY_PRIORITY_LOW = 2;
    public const uint MEMORY_PRIORITY_NORMAL = 5;
    public const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessInformation(IntPtr process, int infoClass, void* info, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetProcessInformation(IntPtr process, int infoClass, void* info, uint size);

    // ── reading another process's memory for the compressibility test ──
    public const uint MEM_COMMIT = 0x1000;
    public const uint MEM_PRIVATE = 0x20000;
    public const uint PAGE_NOACCESS = 0x01;
    public const uint PAGE_GUARD = 0x100;
    public const uint ReadableMask = 0x02 | 0x04 | 0x08 | 0x20 | 0x40 | 0x80; // R, RW, WC, XR, XRW, XWC

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORY_BASIC_INFORMATION
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public ushort Reserved;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
        public uint Reserved2;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nuint VirtualQueryEx(IntPtr process, IntPtr address, out MEMORY_BASIC_INFORMATION info, nuint length);

    [StructLayout(LayoutKind.Sequential)]
    public struct PSAPI_WORKING_SET_EX_INFORMATION
    {
        public IntPtr VirtualAddress;
        public nuint VirtualAttributes;   // bit 0 = Valid (the page is in RAM right now)
    }

    [LibraryImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryWorkingSetEx(IntPtr process, PSAPI_WORKING_SET_EX_INFORMATION* info, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReadProcessMemory(IntPtr process, IntPtr address, byte* buffer, nuint size, out nuint read);

    // ── Windows Compression API (XPRESS, the same family the memory compression store uses) ──
    public const uint COMPRESS_ALGORITHM_XPRESS = 3;
    public const uint COMPRESS_RAW = 1u << 29;

    [LibraryImport("cabinet.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreateCompressor(uint algorithm, IntPtr allocationRoutines, out IntPtr compressor);

    [LibraryImport("cabinet.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Compress(IntPtr compressor, byte* data, nuint dataSize, byte* output, nuint outputSize, out nuint compressedSize);

    [LibraryImport("cabinet.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseCompressor(IntPtr compressor);

    // ── privilege for the store query ──
    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LookupPrivilegeValue(string? system, string name, out long luid);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct TOKEN_PRIVILEGES_ONE
    {
        public uint PrivilegeCount;
        public long Luid;
        public uint Attributes;
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AdjustTokenPrivileges(IntPtr token, [MarshalAs(UnmanagedType.Bool)] bool disableAll,
        ref TOKEN_PRIVILEGES_ONE state, uint length, IntPtr previous, IntPtr returnLength);

    [LibraryImport("kernel32.dll")]
    public static partial IntPtr GetCurrentProcess();

    // progress bar colour state (green / red / yellow) - ForeColor is ignored with visual styles
    public const int PBM_SETSTATE = 0x0410;
    public const int PBST_NORMAL = 1, PBST_ERROR = 2, PBST_PAUSED = 3;

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}

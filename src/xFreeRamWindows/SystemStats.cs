// Developer: Jose Rodriguez Arroyo
// Email: jrpcone@gmail.com
// GitHub: https://github.com/jorodriguezpr

using System.Runtime.InteropServices;

namespace xFreeRamWindows;

internal readonly record struct MemoryStatus(ulong AvailableMB, ulong TotalMB, uint MemoryLoadPercent);

/// <summary>
/// Lightweight system stats for the tray display — free RAM and an estimated
/// system-wide CPU% derived from GetSystemTimes deltas between samples (no
/// PerformanceCounter warm-up delay, no admin requirement beyond what we already have).
/// </summary>
internal static partial class SystemStats
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint Low;
        public uint High;
        public long ToLong() => ((long)High << 32) | Low;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

    public static MemoryStatus GetMemoryStatus()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status)) return default;
        return new MemoryStatus(status.ullAvailPhys / 1024 / 1024, status.ullTotalPhys / 1024 / 1024, status.dwMemoryLoad);
    }

    private static long _lastIdle, _lastKernel, _lastUser;
    private static bool _hasSample;

    /// <summary>
    /// Call this on a fixed interval (e.g. every 1.5s from a UI timer). The first call
    /// after startup has no prior sample to diff against and returns 0.
    /// </summary>
    public static double SampleCpuUsagePercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;

        long idleT = idle.ToLong(), kernelT = kernel.ToLong(), userT = user.ToLong();
        if (!_hasSample)
        {
            _lastIdle = idleT; _lastKernel = kernelT; _lastUser = userT;
            _hasSample = true;
            return 0;
        }

        long idleDelta = idleT - _lastIdle;
        long kernelDelta = kernelT - _lastKernel; // kernel time includes idle time on Windows
        long userDelta = userT - _lastUser;
        _lastIdle = idleT; _lastKernel = kernelT; _lastUser = userT;

        long totalDelta = kernelDelta + userDelta;
        if (totalDelta <= 0) return 0;

        double busy = totalDelta - idleDelta;
        return Math.Clamp(busy * 100.0 / totalDelta, 0, 100);
    }
}

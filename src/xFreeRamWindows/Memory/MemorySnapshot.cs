// Developer: Jose Rodriguez Arroyo
// Email: jrpcone@gmail.com
// GitHub: https://github.com/jorodriguezpr

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace xFreeRamWindows.Memory;

/// <summary>
/// One reading of system memory. <b>Commit</b> is the number that really runs out: every program
/// reserves ("commits") memory, and when the total reaches the commit limit (RAM + page file)
/// programs fail to start or crash - even if some RAM still looks free.
/// </summary>
internal sealed record MemorySnapshot
{
    public DateTime At { get; init; } = DateTime.Now;
    public long RamTotal { get; init; }
    public long RamAvailable { get; init; }
    public long Commit { get; init; }
    public long CommitLimit { get; init; }
    public long CommitPeak { get; init; }
    public long Cache { get; init; }
    public long KernelPaged { get; init; }
    public long KernelNonpaged { get; init; }
    /// <summary>RAM used by the memory compression store (the "Memory Compression" process), or -1.</summary>
    public long CompressedStoreRam { get; init; } = -1;
    /// <summary>How much data the store holds before compression, or -1 when Windows doesn't tell us (needs admin).</summary>
    public long StoreOriginalData { get; init; } = -1;
    /// <summary>The store's compressed size (in RAM or paged out), or -1.</summary>
    public long StoreCompressedData { get; init; } = -1;

    public long RamUsed => RamTotal - RamAvailable;
    public double RamPercent => RamTotal > 0 ? RamUsed * 100.0 / RamTotal : 0;
    public double CommitPercent => CommitLimit > 0 ? Commit * 100.0 / CommitLimit : 0;
    public double StoreRatio => StoreCompressedData > 0 && StoreOriginalData > 0 ? (double)StoreOriginalData / StoreCompressedData : 0;

    public static MemorySnapshot Take(bool includeStore = true)
    {
        if (!MemoryNative.GetPerformanceInfo(out var pi, (uint)Marshal.SizeOf<MemoryNative.PERFORMANCE_INFORMATION>()))
            return new MemorySnapshot();

        long page = (long)pi.PageSize;
        var snap = new MemorySnapshot
        {
            RamTotal = (long)pi.PhysicalTotal * page,
            RamAvailable = (long)pi.PhysicalAvailable * page,
            Commit = (long)pi.CommitTotal * page,
            CommitLimit = (long)pi.CommitLimit * page,
            CommitPeak = (long)pi.CommitPeak * page,
            Cache = (long)pi.SystemCache * page,
            KernelPaged = (long)pi.KernelPaged * page,
            KernelNonpaged = (long)pi.KernelNonpaged * page,
        };
        if (!includeStore) return snap;

        var storeRam = CompressionStoreRam();
        var (original, compressed) = CompressionStore.TryQuery(page, snap.CommitLimit);
        return snap with { CompressedStoreRam = storeRam, StoreOriginalData = original, StoreCompressedData = compressed };
    }

    /// <summary>Working set of the "Memory Compression" process = RAM the compressed store occupies.</summary>
    private static long CompressionStoreRam()
    {
        try
        {
            var procs = Process.GetProcessesByName("Memory Compression");
            long total = 0;
            foreach (var p in procs)
            {
                using (p) total += p.WorkingSet64;
            }
            return procs.Length > 0 ? total : -1;
        }
        catch
        {
            return -1;
        }
    }

    public static string Gb(long bytes) => bytes < 0 ? "-" : $"{bytes / 1073741824.0:N1} GB";
    public static string Mb(long bytes) => bytes < 0 ? "-" : $"{bytes / 1048576.0:N0} MB";
}

/// <summary>
/// Reads the compression store statistics (how much data went in, how small it became). This is the
/// read-only query Task Manager uses for "In use (Compressed)". It needs administrator rights
/// (SeProfileSingleProcessPrivilege); without them the dashboard shows the store's RAM only.
/// </summary>
internal static unsafe class CompressionStore
{
    private static bool? _privilege;

    public static (long Original, long Compressed) TryQuery(long pageSize, long commitLimit)
    {
        try
        {
            _privilege ??= EnablePrivilege("SeProfileSingleProcessPrivilege");
            if (_privilege != true) return (-1, -1);

            var request = new MemoryNative.SM_MEM_COMPRESSION_INFO_REQUEST { VersionAndSpare = 3 };
            var info = new MemoryNative.SYSTEM_STORE_INFORMATION
            {
                VersionAndClass = 1u | (MemoryNative.MemCompressionInfoRequest << 8),
                Data = (IntPtr)(&request),
                Length = (uint)sizeof(MemoryNative.SM_MEM_COMPRESSION_INFO_REQUEST),
            };
            int status = MemoryNative.NtQuerySystemInformation(MemoryNative.SystemStoreInformation, ref info,
                sizeof(MemoryNative.SYSTEM_STORE_INFORMATION), out _);
            if (status != 0) return (-1, -1);

            return (ToBytes((long)request.TotalDataCompressed, pageSize, commitLimit),
                    ToBytes((long)request.TotalCompressedSize, pageSize, commitLimit));
        }
        catch
        {
            return (-1, -1);
        }
    }

    /// <summary>
    /// Windows builds differ on whether these fields count pages or bytes. A value that would be far
    /// larger than all of RAM + page file when read as pages must already be bytes.
    /// </summary>
    internal static long ToBytes(long value, long pageSize, long commitLimit)
    {
        if (value <= 0) return 0;
        return value * pageSize > commitLimit * 2 ? value : value * pageSize;
    }

    internal static bool EnablePrivilege(string name)
    {
        const uint TOKEN_ADJUST_PRIVILEGES = 0x20, TOKEN_QUERY = 0x08, SE_PRIVILEGE_ENABLED = 0x2;
        if (!MemoryNative.OpenProcessToken(MemoryNative.GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token))
            return false;
        try
        {
            if (!MemoryNative.LookupPrivilegeValue(null, name, out var luid)) return false;
            var tp = new MemoryNative.TOKEN_PRIVILEGES_ONE { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
            if (!MemoryNative.AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero)) return false;
            return Marshal.GetLastWin32Error() == 0;   // ERROR_NOT_ALL_ASSIGNED (1300) = we don't hold it
        }
        finally
        {
            MemoryNative.CloseHandle(token);
        }
    }
}

/// <summary>Last hour of readings for the dashboard graph (one point every 10 seconds).</summary>
internal sealed class MemoryHistory
{
    public const int Capacity = 360;
    private readonly Queue<MemorySnapshot> _points = new();
    private readonly object _lock = new();

    public void Add(MemorySnapshot s)
    {
        lock (_lock)
        {
            _points.Enqueue(s);
            while (_points.Count > Capacity) _points.Dequeue();
        }
    }

    public MemorySnapshot[] ToArray()
    {
        lock (_lock) return _points.ToArray();
    }
}

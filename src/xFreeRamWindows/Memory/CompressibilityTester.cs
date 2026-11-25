// Developer: Jose Rodriguez Arroyo
// Email: jrpcone@gmail.com
// GitHub: https://github.com/jorodriguezpr

using System.Diagnostics;

namespace xFreeRamWindows.Memory;

internal sealed record CompressibilityResult
{
    public string Name { get; init; } = "";
    public string Label { get; init; } = "";
    public int Processes { get; init; }
    public int ProcessesRead { get; init; }
    public long Commit { get; init; }
    public long SampledBytes { get; init; }
    public long ZeroBytes { get; init; }
    public long CompressedBytes { get; init; }

    /// <summary>How many times smaller the sampled non-zero pages became (2.0 = half the size).</summary>
    public double Ratio => CompressedBytes > 0 ? (double)(SampledBytes - ZeroBytes) / CompressedBytes : 0;
    public double ZeroPercent => SampledBytes > 0 ? ZeroBytes * 100.0 / SampledBytes : 0;
    /// <summary>Estimated size of this app's committed memory if all of it were compressed like the sample.</summary>
    public long EstimatedCompressed => SampledBytes > 0 ? (long)(Commit * (double)CompressedBytes / SampledBytes) : Commit;
    public long EstimatedSaving => Math.Max(0, Commit - EstimatedCompressed);
}

internal sealed record CompressibilityReport
{
    public DateTime At { get; init; } = DateTime.Now;
    public List<CompressibilityResult> Apps { get; init; } = new();
    public long TotalCommit => Apps.Sum(a => a.Commit);
    public long TotalSaving => Apps.Sum(a => a.EstimatedSaving);
    public long TotalSampled => Apps.Sum(a => a.SampledBytes);
    public double OverallRatio
    {
        get
        {
            long nonZero = Apps.Sum(a => a.SampledBytes - a.ZeroBytes), comp = Apps.Sum(a => a.CompressedBytes);
            return comp > 0 ? (double)nonZero / comp : 0;
        }
    }
    public long StoreOriginal { get; init; } = -1;
    public long StoreCompressed { get; init; } = -1;
    public int Skipped { get; init; }
}

/// <summary>
/// Measures how well the memory of the running apps compresses (phase 0 of the "RAM doubler" question).
///
/// <para>For each of the biggest apps it reads a spread-out sample of its private memory pages - only pages
/// that are in RAM right now (checked with QueryWorkingSetEx), so the test never pulls memory back from the
/// page file or the compressed store - and compresses each 4 KB page on its own with Windows' XPRESS
/// compressor, the same family the memory compression store uses. Zero-filled pages are counted apart
/// (Windows doesn't need to store them at all).</para>
///
/// <para>Privacy: the bytes read are compressed in memory and thrown away; nothing is saved, logged or sent.
/// Protected processes (Defender, system) can't be read and are skipped.</para>
/// </summary>
internal static unsafe class CompressibilityTester
{
    private const int PageSize = 4096;
    private const int PagesPerProcess = 512;      // 2 MB sample per process
    private const int CandidatesPerProcess = 8192;
    private const int MaxProcessesPerApp = 8;

    public static CompressibilityReport Run(int topApps = 20, IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        var apps = AppMemoryUsage.GetApps()
            .Where(a => a.Name is not ("System" or "Memory Compression" or "Registry" or "Secure System" or "vmmem" or "vmmemWSL"))
            .Take(topApps)
            .ToList();

        if (!MemoryNative.CreateCompressor(MemoryNative.COMPRESS_ALGORITHM_XPRESS | MemoryNative.COMPRESS_RAW, IntPtr.Zero, out var compressor) &&
            !MemoryNative.CreateCompressor(MemoryNative.COMPRESS_ALGORITHM_XPRESS, IntPtr.Zero, out compressor))
            throw new InvalidOperationException("Windows' compressor (cabinet.dll) is not available.");

        var results = new List<CompressibilityResult>();
        int skipped = 0;
        byte[] page = new byte[PageSize];
        byte[] output = new byte[PageSize * 2];
        try
        {
            int n = 0;
            foreach (var app in apps)
            {
                cancel.ThrowIfCancellationRequested();
                progress?.Report($"Testing {++n}/{apps.Count}: {app.Label}...");

                long sampled = 0, zero = 0, compressed = 0;
                int read = 0;
                var pids = BiggestPids(app.Pids, MaxProcessesPerApp);
                foreach (var pid in pids)
                {
                    var (s, z, c) = SampleProcess(pid, compressor, page, output);
                    if (s > 0) { read++; sampled += s; zero += z; compressed += c; }
                }
                if (read == 0) { skipped++; continue; }

                results.Add(new CompressibilityResult
                {
                    Name = app.Name,
                    Label = app.Label,
                    Processes = app.Processes,
                    ProcessesRead = read,
                    Commit = app.Commit,
                    SampledBytes = sampled,
                    ZeroBytes = zero,
                    CompressedBytes = compressed,
                });
            }
        }
        finally
        {
            MemoryNative.CloseCompressor(compressor);
        }

        var snap = MemorySnapshot.Take();
        return new CompressibilityReport
        {
            Apps = results.OrderByDescending(r => r.EstimatedSaving).ToList(),
            StoreOriginal = snap.StoreOriginalData,
            StoreCompressed = snap.StoreCompressedData,
            Skipped = skipped,
        };
    }

    private static List<int> BiggestPids(int[] pids, int max)
    {
        var sized = new List<(int Pid, long Size)>();
        foreach (var pid in pids)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                sized.Add((pid, p.PagedMemorySize64));
            }
            catch
            {
                // exited
            }
        }
        return sized.OrderByDescending(x => x.Size).Take(max).Select(x => x.Pid).ToList();
    }

    /// <returns>(bytes sampled, of which zero-filled, compressed size of the non-zero pages)</returns>
    private static (long Sampled, long Zero, long Compressed) SampleProcess(int pid, IntPtr compressor, byte[] page, byte[] output)
    {
        var h = MemoryNative.OpenProcess(MemoryNative.PROCESS_QUERY_INFORMATION | MemoryNative.PROCESS_VM_READ, false, pid);
        if (h == IntPtr.Zero) return (0, 0, 0);
        try
        {
            // 1. private, committed, readable regions
            var regions = new List<(long Start, long Pages)>();
            long totalPages = 0;
            long addr = 0;
            while (addr < 0x7FFF_FFFF_0000)
            {
                if (MemoryNative.VirtualQueryEx(h, (IntPtr)addr, out var mbi, (nuint)sizeof(MemoryNative.MEMORY_BASIC_INFORMATION)) == 0) break;
                long size = (long)mbi.RegionSize;
                if (size <= 0) break;
                if (mbi.State == MemoryNative.MEM_COMMIT && mbi.Type == MemoryNative.MEM_PRIVATE &&
                    (mbi.Protect & (MemoryNative.PAGE_GUARD | MemoryNative.PAGE_NOACCESS)) == 0 &&
                    (mbi.Protect & MemoryNative.ReadableMask) != 0)
                {
                    long pages = size / PageSize;
                    regions.Add(((long)mbi.BaseAddress, pages));
                    totalPages += pages;
                }
                addr = (long)mbi.BaseAddress + size;
            }
            if (totalPages == 0) return (0, 0, 0);

            // 2. spread candidate pages evenly over all of it
            long stride = Math.Max(1, totalPages / CandidatesPerProcess);
            var candidates = new List<long>();
            long index = 0;
            foreach (var (start, pages) in regions)
            {
                for (long i = 0; i < pages; i++, index++)
                {
                    if (index % stride == 0) candidates.Add(start + i * PageSize);
                    if (candidates.Count >= CandidatesPerProcess) break;
                }
                if (candidates.Count >= CandidatesPerProcess) break;
            }

            // 3. keep only pages that are in RAM now (reading others would page them back in)
            var info = new MemoryNative.PSAPI_WORKING_SET_EX_INFORMATION[candidates.Count];
            for (int i = 0; i < info.Length; i++) info[i].VirtualAddress = (IntPtr)candidates[i];
            fixed (MemoryNative.PSAPI_WORKING_SET_EX_INFORMATION* pInfo = info)
            {
                if (!MemoryNative.QueryWorkingSetEx(h, pInfo, (uint)(info.Length * sizeof(MemoryNative.PSAPI_WORKING_SET_EX_INFORMATION))))
                    return (0, 0, 0);
            }
            var resident = info.Where(x => (x.VirtualAttributes & 1) != 0).Select(x => x.VirtualAddress).ToList();
            if (resident.Count == 0) return (0, 0, 0);
            int step = Math.Max(1, resident.Count / PagesPerProcess);

            // 4. read + compress each page on its own
            long sampled = 0, zero = 0, compressed = 0;
            fixed (byte* pPage = page)
            fixed (byte* pOut = output)
            {
                for (int i = 0; i < resident.Count && sampled < (long)PagesPerProcess * PageSize; i += step)
                {
                    if (!MemoryNative.ReadProcessMemory(h, resident[i], pPage, PageSize, out var got) || got != PageSize) continue;
                    sampled += PageSize;
                    if (page.AsSpan().IndexOfAnyExcept((byte)0) < 0) { zero += PageSize; continue; }
                    if (MemoryNative.Compress(compressor, pPage, PageSize, pOut, (nuint)output.Length, out var size))
                        compressed += Math.Min((long)size, PageSize);   // a page that doesn't shrink is kept as is
                    else
                        compressed += PageSize;
                }
            }
            page.AsSpan().Clear();
            output.AsSpan().Clear();
            return (sampled, zero, compressed);
        }
        catch
        {
            return (0, 0, 0);
        }
        finally
        {
            MemoryNative.CloseHandle(h);
        }
    }
}

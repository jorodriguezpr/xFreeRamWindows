// Developer: Jose Rodriguez Arroyo
// Email: jrpcone@gmail.com
// GitHub: https://github.com/jorodriguezpr

using System.Text.Json;
using xFreeRamWindows.Memory;

namespace xFreeRamWindows;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Diagnostics (no tray; only --idle-selftest changes anything, and only its own ping.exe child):
        //   xFreeRamWindows.exe --report <file.json> [--compress]   memory, page file, recommendation, top apps [+ compressibility test]
        //   xFreeRamWindows.exe --screenshots <folder>             saves each dashboard tab as a PNG
        //   xFreeRamWindows.exe --idle-selftest <file.txt>         checks "Compress idle apps" on a ping.exe child it starts
        if (args.Length >= 2 && args[0] == "--report") return WriteReport(args[1], args.Contains("--compress"));
        if (args.Length >= 2 && args[0] == "--screenshots") return SaveScreenshots(args[1]);
        if (args.Length >= 2 && args[0] == "--idle-selftest") return IdleSelfTest(args[1]);

        using var singleInstanceMutex = new Mutex(true, "xFreeRamWindows_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("xFreeRamWindows is already running (check the system tray).", "xFreeRamWindows",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.Run(new TrayApplicationContext());
        return 0;
    }

    private static int WriteReport(string path, bool compress)
    {
        try
        {
            var snap = MemorySnapshot.Take();
            var pf = PageFileOptimizer.Read();
            var report = new
            {
                Version = typeof(Program).Assembly.GetName().Version?.ToString(3),
                Memory = snap,
                snap.RamPercent,
                snap.CommitPercent,
                snap.StoreRatio,
                PageFile = pf,
                Recommendation = PageFileOptimizer.Recommend(pf),
                TopApps = AppMemoryUsage.GetApps().Take(15).Select(a => new { a.Label, a.Name, a.Processes, CommitMB = a.Commit / 1048576, RamMB = a.Ram / 1048576 }),
                Compressibility = compress ? CompressibilityTester.Run() : null,
            };
            File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(path, "ERROR " + ex);
            return 1;
        }
    }

    /// <summary>
    /// Checks "Compress idle apps" on a harmless child process (ping.exe) only: after 1 idle minute it must get
    /// low memory priority and a trimmed working set; RestoreAll must put the priority back. No other app is touched.
    /// </summary>
    private static int IdleSelfTest(string path)
    {
        var log = new List<string>();
        var settings = new AppSettings { IdleCompressEnabled = true, IdleCompressMinutes = 1 };
        var idle = new IdleCompressor(settings) { OnlyApps = new(StringComparer.OrdinalIgnoreCase) { "PING" } };
        using var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ping", "-n 400 127.0.0.1")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        bool ok = true;
        try
        {
            Thread.Sleep(1500);
            child.Refresh();
            uint before = IdleCompressor.ReadMemoryPriority(child.Id);
            long wsBefore = child.WorkingSet64;
            log.Add($"start: pid {child.Id} memory priority {before}, working set {wsBefore / 1024} KB");
            idle.Check();                                  // first sight: app is "in use" (just seen)
            Thread.Sleep(TimeSpan.FromSeconds(70));
            idle.Check();                                  // idle > 1 min -> compress
            child.Refresh();
            uint during = IdleCompressor.ReadMemoryPriority(child.Id);
            log.Add($"after 70 s idle: memory priority {during}, working set {child.WorkingSet64 / 1024} KB, compressed apps {idle.CompressedApps}");
            ok &= during == 2 && idle.CompressedApps == 1 && child.WorkingSet64 < wsBefore;
            idle.RestoreAll();
            uint after = IdleCompressor.ReadMemoryPriority(child.Id);
            log.Add($"after RestoreAll: memory priority {after}, processes restored {idle.Restored}");
            ok &= after == before && idle.Restored == 1;
        }
        catch (Exception ex)
        {
            log.Add("ERROR " + ex);
            ok = false;
        }
        finally
        {
            try { child.Kill(); } catch { /* already gone */ }
        }
        log.Add(ok ? "PASS" : "FAIL");
        File.WriteAllLines(path, log);
        return ok ? 0 : 1;
    }

    private static int SaveScreenshots(string folder)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Directory.CreateDirectory(folder);
        var settings = new AppSettings();   // defaults, never saved here
        var history = new MemoryHistory();
        var first = MemorySnapshot.Take();
        var rnd = new Random(1);
        for (int i = 0; i < 120; i++)
            history.Add(first with { At = first.At.AddSeconds(-10 * (120 - i)), RamAvailable = Math.Max(0, first.RamAvailable + rnd.Next(-400, 400) * 1048576L) });
        var form = new DashboardForm(settings, history, new IdleCompressor(settings)) { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000) };
        form.Show();
        foreach (var tab in new[] { DashboardForm.TabOverview, DashboardForm.TabApps, DashboardForm.TabPageFile, DashboardForm.TabIdle, DashboardForm.TabTest })
        {
            form.ShowTab(tab);
            for (int i = 0; i < 20; i++) { Application.DoEvents(); Thread.Sleep(50); }
            using var bmp = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
            bmp.Save(Path.Combine(folder, $"dashboard-{tab}.png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        form.Close();
        return 0;
    }
}

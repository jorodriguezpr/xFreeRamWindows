// Developer: Jose Rodriguez Arroyo
// Email: jrpcone@gmail.com
// GitHub: https://github.com/jorodriguezpr

using System.Drawing.Drawing2D;
using xFreeRamWindows.Memory;

namespace xFreeRamWindows;

/// <summary>
/// Memory dashboard (1.2.0): Overview with the last hour's graph, Top apps, Page file optimizer,
/// Idle apps (compress idle apps) and the Compressibility test.
/// </summary>
internal sealed class DashboardForm : Form
{
    public const string TabOverview = "overview", TabApps = "apps", TabPageFile = "pagefile", TabIdle = "idle", TabTest = "test";

    private readonly AppSettings _settings;
    private readonly MemoryHistory _history;
    private readonly IdleCompressor _idle;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    private readonly CancellationTokenSource _cancel = new();
    private int _tick;

    // overview
    private readonly ProgressBar _ramBar = Bar(), _commitBar = Bar(), _pageBar = Bar(), _storeBar = Bar();
    private readonly Label _ramText = Value(), _commitText = Value(), _pageText = Value(), _storeText = Value();
    private readonly Label _overviewNote = new() { Dock = DockStyle.Fill, AutoSize = false, Padding = new Padding(4) };
    private readonly HistoryGraph _graph = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _warnBox = new() { AutoSize = true };
    private readonly ComboBox _warnPct = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
    private long _pageFileUsed = -1, _pageFileSize = -1;

    // top apps
    private readonly ListView _appsList = List(("App", 300), ("Process", 160), ("Processes", 80), ("Commit", 100), ("In RAM", 100));

    // page file
    private readonly TextBox _pfCurrent = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None };
    private readonly Label _pfRecommend = new() { Dock = DockStyle.Fill, AutoSize = false };
    private readonly Label _pfResult = new() { Dock = DockStyle.Fill, AutoSize = false, ForeColor = Color.DarkGreen };
    private readonly Button _pfApply = new() { Text = "Apply recommended", AutoSize = true };
    private readonly Button _pfAuto = new() { Text = "Let Windows manage it", AutoSize = true };
    private readonly Button _pfRestore = new() { Text = "Restore original", AutoSize = true };
    private PageFileRecommendation? _rec;

    // idle apps
    private readonly CheckBox _idleOn = new() { Text = "Compress idle apps", AutoSize = true };
    private readonly ComboBox _idleMinutes = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly CheckBox _idleEco = new() { Text = "Also use efficiency mode (less CPU for idle apps)", AutoSize = true };
    private readonly ListView _idleList = List(("App", 250), ("Process", 130), ("Processes", 70), ("In RAM", 80), ("Status", 230), ("Rule", 70));

    // compressibility
    private readonly Button _testRun = new() { Text = "Run test", AutoSize = true };
    private readonly Label _testStatus = new() { AutoSize = true, Padding = new Padding(8, 6, 0, 0) };
    private readonly Label _testSummary = new() { Dock = DockStyle.Fill, AutoSize = false };
    private readonly ListView _testList = List(("App", 280), ("Commit", 90), ("Sampled", 80), ("Zero pages", 85), ("Ratio", 70), ("Could save", 95));

    private static readonly int[] WarnChoices = { 80, 85, 90, 95 };
    private static readonly int[] IdleChoices = { 5, 10, 15, 30, 60 };

    public DashboardForm(AppSettings settings, MemoryHistory history, IdleCompressor idle)
    {
        _settings = settings;
        _history = history;
        _idle = idle;

        Text = "xFreeRamWindows - Memory Dashboard";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9f);
        Size = new Size(940, 640);
        MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* default icon */ }

        _tabs.TabPages.Add(BuildOverview());
        _tabs.TabPages.Add(BuildApps());
        _tabs.TabPages.Add(BuildPageFile());
        _tabs.TabPages.Add(BuildIdle());
        _tabs.TabPages.Add(BuildTest());
        Controls.Add(_tabs);

        _tabs.SelectedIndexChanged += (_, _) => RefreshVisibleTab(force: true);
        _timer.Tick += (_, _) => { _tick++; RefreshVisibleTab(force: false); };
        Shown += (_, _) => { RefreshOverview(); _timer.Start(); };
        FormClosed += (_, _) => { _timer.Stop(); _cancel.Cancel(); };
    }

    public void ShowTab(string tab)
    {
        var page = _tabs.TabPages.Cast<TabPage>().FirstOrDefault(p => (string?)p.Tag == tab);
        if (page is not null) _tabs.SelectedTab = page;
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    private void RefreshVisibleTab(bool force)
    {
        switch ((string?)_tabs.SelectedTab?.Tag)
        {
            case TabOverview: RefreshOverview(); break;
            case TabApps: if (force || _tick % 3 == 0) RefreshApps(); break;
            case TabPageFile: if (force) RefreshPageFile(); break;
            case TabIdle: if (force || _tick % 3 == 0) RefreshIdle(); break;
        }
    }

    // ───────────────────────────── Overview ─────────────────────────────
    private TabPage BuildOverview()
    {
        var page = new TabPage("Overview") { Tag = TabOverview, Padding = new Padding(10) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, AutoSize = true };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        AddRow(grid, "RAM in use", _ramBar, _ramText);
        AddRow(grid, "Commit (reserved memory)", _commitBar, _commitText);
        AddRow(grid, "Page file in use", _pageBar, _pageText);
        AddRow(grid, "Compressed store in RAM", _storeBar, _storeText);

        _warnPct.Items.AddRange(WarnChoices.Select(x => (object)$"{x} %").ToArray());
        _warnPct.SelectedIndex = Math.Max(0, Array.IndexOf(WarnChoices, _settings.CommitWarnPercent));
        _warnBox.Text = "Warn me when commit reaches";
        _warnBox.Checked = _settings.CommitWarnEnabled;
        _warnBox.CheckedChanged += (_, _) => { _settings.CommitWarnEnabled = _warnBox.Checked; _settings.Save(); };
        _warnPct.SelectedIndexChanged += (_, _) => { _settings.CommitWarnPercent = WarnChoices[_warnPct.SelectedIndex]; _settings.Save(); };
        var warnRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
        warnRow.Controls.Add(_warnBox);
        warnRow.Controls.Add(_warnPct);
        var pfLink = new LinkLabel { Text = "Raise the commit limit (page file)...", AutoSize = true, Padding = new Padding(16, 4, 0, 0) };
        pfLink.LinkClicked += (_, _) => ShowTab(TabPageFile);
        warnRow.Controls.Add(pfLink);

        var noteHolder = new Panel { Dock = DockStyle.Top, Height = 64 };
        noteHolder.Controls.Add(_overviewNote);
        var graphTitle = new Label { Text = "Last hour", Dock = DockStyle.Top, Height = 22, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 4, 0, 0) };

        page.Controls.Add(_graph);
        page.Controls.Add(graphTitle);
        page.Controls.Add(noteHolder);
        page.Controls.Add(grid);
        page.Controls.Add(warnRow);
        return page;
    }

    private void RefreshOverview()
    {
        var s = _history.ToArray().LastOrDefault() ?? MemorySnapshot.Take();
        if (DateTime.Now - s.At > TimeSpan.FromSeconds(5)) s = MemorySnapshot.Take();

        SetBar(_ramBar, s.RamPercent);
        _ramText.Text = $"{MemorySnapshot.Gb(s.RamUsed)} of {MemorySnapshot.Gb(s.RamTotal)} ({s.RamPercent:N0} %)";
        SetBar(_commitBar, s.CommitPercent);
        _commitText.Text = $"{MemorySnapshot.Gb(s.Commit)} of {MemorySnapshot.Gb(s.CommitLimit)} ({s.CommitPercent:N0} %)";
        SetBarState(_commitBar, s.CommitPercent >= _settings.CommitWarnPercent ? MemoryNative.PBST_ERROR
                              : s.CommitPercent >= _settings.CommitWarnPercent - 10 ? MemoryNative.PBST_PAUSED : MemoryNative.PBST_NORMAL);
        SetBarState(_ramBar, s.RamPercent >= 90 ? MemoryNative.PBST_PAUSED : MemoryNative.PBST_NORMAL);

        if (_tick % 15 == 0 || _pageFileSize < 0)
        {
            var pf = PageFileOptimizer.Read();
            _pageFileSize = pf.Usage.Sum(u => (long)u.AllocatedMB) * 1048576;
            _pageFileUsed = pf.Usage.Sum(u => (long)u.CurrentMB) * 1048576;
        }
        SetBar(_pageBar, _pageFileSize > 0 ? _pageFileUsed * 100.0 / _pageFileSize : 0);
        _pageText.Text = _pageFileSize > 0 ? $"{MemorySnapshot.Gb(_pageFileUsed)} of {MemorySnapshot.Gb(_pageFileSize)}" : "no page file";

        SetBar(_storeBar, s.CompressedStoreRam > 0 && s.RamTotal > 0 ? s.CompressedStoreRam * 100.0 / s.RamTotal : 0);
        _storeText.Text = s.StoreRatio > 0
            ? $"{MemorySnapshot.Gb(s.CompressedStoreRam)} holding {MemorySnapshot.Gb(s.StoreOriginalData)} ({s.StoreRatio:N1}x)"
            : $"{MemorySnapshot.Gb(s.CompressedStoreRam)} of RAM";

        long headroom = s.CommitLimit - s.Commit;
        _overviewNote.Text =
            $"Commit is the memory programs have reserved. When it reaches the limit ({MemorySnapshot.Gb(s.CommitLimit)} = RAM + page file), " +
            $"programs fail to start or crash, even if RAM still looks free. Room left now: {MemorySnapshot.Gb(headroom)}; highest since Windows started: {MemorySnapshot.Gb(s.CommitPeak)}." +
            (s.StoreRatio > 0
                ? $" Windows' compression keeps {MemorySnapshot.Gb(s.StoreOriginalData)} of idle memory in {MemorySnapshot.Gb(s.StoreCompressedData)}."
                : " (Compression ratio: run as administrator to see it.)");

        _graph.Points = _history.ToArray();
        _graph.Invalidate();
    }

    // ───────────────────────────── Top apps ─────────────────────────────
    private TabPage BuildApps()
    {
        var page = new TabPage("Top apps") { Tag = TabApps, Padding = new Padding(10) };
        var note = new Label
        {
            Dock = DockStyle.Top, Height = 40,
            Text = "All processes of an app added together. Commit = memory the app has reserved; it counts against the commit limit even when it is not in RAM. In RAM = what it uses of physical memory right now.",
        };
        page.Controls.Add(_appsList);
        page.Controls.Add(note);
        return page;
    }

    private void RefreshApps()
    {
        var apps = AppMemoryUsage.GetApps().Take(40).ToList();
        _appsList.BeginUpdate();
        _appsList.Items.Clear();
        foreach (var a in apps)
            _appsList.Items.Add(new ListViewItem(new[] { a.Label, a.Name, a.Processes.ToString(), MemorySnapshot.Mb(a.Commit), MemorySnapshot.Mb(a.Ram) }));
        _appsList.EndUpdate();
    }

    // ───────────────────────────── Page file ─────────────────────────────
    private TabPage BuildPageFile()
    {
        var page = new TabPage("Page file") { Tag = TabPageFile, Padding = new Padding(10) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(Title("Now"), 0, 0);
        layout.Controls.Add(_pfCurrent, 0, 1);
        layout.Controls.Add(Title("Recommended"), 0, 2);
        layout.Controls.Add(_pfRecommend, 0, 3);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        buttons.Controls.AddRange(new Control[] { _pfApply, _pfAuto, _pfRestore });
        layout.Controls.Add(buttons, 0, 4);
        var bottom = new Panel { Dock = DockStyle.Fill };
        var restartNote = new Label
        {
            Dock = DockStyle.Top, Height = 48,
            Text = "Changes take effect after you restart Windows. The setting from before the first change is saved, so \"Restore original\" can always put it back. " +
                   "A bigger page file only uses more disk space; with a larger maximum, Windows grows it only when memory really runs short.",
        };
        bottom.Controls.Add(_pfResult);
        bottom.Controls.Add(restartNote);
        layout.Controls.Add(bottom, 0, 5);
        page.Controls.Add(layout);

        _pfApply.Click += (_, _) =>
        {
            if (_rec is null) return;
            var ok = MessageBox.Show(this,
                $"Set {_rec.Path} to start at {_rec.InitialMB / 1024} GB and grow up to {_rec.MaximumMB / 1024} GB?\n\n" +
                "This changes a Windows setting and takes effect after a restart. The current setting is saved first.",
                "Page file optimizer", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ok == DialogResult.Yes) RunPageFileAction(() => PageFileOptimizer.Apply(_rec.Path, _rec.InitialMB, _rec.MaximumMB));
        };
        _pfAuto.Click += (_, _) =>
        {
            var ok = MessageBox.Show(this, "Let Windows manage the page file size (System managed size)? Takes effect after a restart.",
                "Page file optimizer", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ok == DialogResult.Yes) RunPageFileAction(PageFileOptimizer.SetAutomatic);
        };
        _pfRestore.Click += (_, _) =>
        {
            var ok = MessageBox.Show(this, "Put back the page file setting from before xFreeRamWindows changed it? Takes effect after a restart.",
                "Page file optimizer", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ok == DialogResult.Yes) RunPageFileAction(PageFileOptimizer.RestoreBackup);
        };
        return page;
    }

    private void RunPageFileAction(Func<string> action)
    {
        try
        {
            _pfResult.ForeColor = Color.DarkGreen;
            _pfResult.Text = action();
        }
        catch (Exception ex)
        {
            _pfResult.ForeColor = Color.Firebrick;
            _pfResult.Text = "Not changed: " + ex.Message + (ex is UnauthorizedAccessException or System.Management.ManagementException
                ? " (xFreeRamWindows needs to run as administrator for this.)" : "");
        }
        RefreshPageFile();
    }

    private void RefreshPageFile()
    {
        var state = PageFileOptimizer.Read();
        _pfCurrent.Text = PageFileOptimizer.Describe(state);
        _rec = state.Error is null ? PageFileOptimizer.Recommend(state) : null;
        if (_rec is null)
        {
            _pfRecommend.Text = "-";
            _pfApply.Enabled = false;
        }
        else
        {
            _pfRecommend.Text = (_rec.AlreadyGood ? "Your page file already matches this recommendation. " : "") + _rec.Reason;
            _pfApply.Enabled = !_rec.AlreadyGood && _rec.MaximumMB >= 4096;
        }
        _pfAuto.Enabled = !state.AutomaticallyManaged;
        _pfRestore.Enabled = PageFileOptimizer.HasBackup;
    }

    // ───────────────────────────── Idle apps ─────────────────────────────
    private TabPage BuildIdle()
    {
        var page = new TabPage("Idle apps") { Tag = TabIdle, Padding = new Padding(10) };
        var note = new Label
        {
            Dock = DockStyle.Top, Height = 58,
            Text = "When an app has not been used for a while (not in front and almost no CPU), its memory priority is lowered and its memory is trimmed once, " +
                   "so Windows compresses it before touching the app you are working in. As soon as you use the app again everything is put back. " +
                   "Nothing is closed. Windows components and services are never touched.",
        };
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        _idleMinutes.Items.AddRange(IdleChoices.Select(x => (object)$"after {x} min").ToArray());
        _idleMinutes.SelectedIndex = Math.Max(0, Array.IndexOf(IdleChoices, _settings.IdleCompressMinutes));
        _idleOn.Checked = _settings.IdleCompressEnabled;
        _idleEco.Checked = _settings.IdleEfficiencyMode;
        _idleOn.CheckedChanged += (_, _) => { _settings.IdleCompressEnabled = _idleOn.Checked; _settings.Save(); Task.Run(_idle.Check); };
        _idleMinutes.SelectedIndexChanged += (_, _) => { _settings.IdleCompressMinutes = IdleChoices[_idleMinutes.SelectedIndex]; _settings.Save(); };
        _idleEco.CheckedChanged += (_, _) =>
        {
            _settings.IdleEfficiencyMode = _idleEco.Checked;
            _settings.Save();
            if (!_idleEco.Checked) Task.Run(_idle.RestoreAll);   // takes effect again on the next idle period
        };
        top.Controls.AddRange(new Control[] { _idleOn, _idleMinutes, _idleEco });

        var rules = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        var never = new Button { Text = "Never touch selected", AutoSize = true };
        var normal = new Button { Text = "Default", AutoSize = true };
        var sooner = new Button { Text = "Compress sooner (2 min)", AutoSize = true };
        never.Click += (_, _) => SetRule(IdleRule.Never);
        normal.Click += (_, _) => SetRule(IdleRule.Default);
        sooner.Click += (_, _) => SetRule(IdleRule.Sooner);
        rules.Controls.AddRange(new Control[] { never, normal, sooner });

        page.Controls.Add(_idleList);
        page.Controls.Add(rules);
        page.Controls.Add(top);
        page.Controls.Add(note);
        return page;
    }

    private void SetRule(IdleRule rule)
    {
        foreach (ListViewItem item in _idleList.SelectedItems)
        {
            var name = item.SubItems[1].Text;
            if (rule == IdleRule.Default) _settings.IdleRules.Remove(name);
            else _settings.IdleRules[name] = rule.ToString();
        }
        _settings.Save();
        Task.Run(_idle.Check);
        RefreshIdle();
    }

    private void RefreshIdle()
    {
        var selected = _idleList.SelectedItems.Cast<ListViewItem>().Select(i => i.SubItems[1].Text).ToHashSet();
        var rows = _idle.Status();
        _idleList.BeginUpdate();
        _idleList.Items.Clear();
        foreach (var r in rows)
        {
            var item = new ListViewItem(new[] { r.Label, r.Name, r.Processes.ToString(), MemorySnapshot.Mb(r.Ram), r.State, r.Rule.ToString() });
            if (r.State.StartsWith("Compressed", StringComparison.Ordinal)) item.ForeColor = Color.DarkGreen;
            item.Selected = selected.Contains(r.Name);
            _idleList.Items.Add(item);
        }
        _idleList.EndUpdate();
    }

    // ───────────────────────────── Compressibility ─────────────────────────────
    private TabPage BuildTest()
    {
        var page = new TabPage("Compressibility test") { Tag = TabTest, Padding = new Padding(10) };
        var note = new Label
        {
            Dock = DockStyle.Top, Height = 58,
            Text = "Measures how well the memory of your biggest apps compresses: a sample of each app's pages that are in RAM right now is compressed " +
                   "with Windows' own XPRESS compressor, page by page, like the memory compression store does. Nothing read is saved or sent. " +
                   "This shows how much a \"RAM doubler\" could really gain on this PC.",
        };
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        top.Controls.Add(_testRun);
        top.Controls.Add(_testStatus);
        var summaryHolder = new Panel { Dock = DockStyle.Bottom, Height = 70 };
        summaryHolder.Controls.Add(_testSummary);
        _testRun.Click += async (_, _) => await RunTest();
        page.Controls.Add(_testList);
        page.Controls.Add(summaryHolder);
        page.Controls.Add(top);
        page.Controls.Add(note);
        return page;
    }

    private async Task RunTest()
    {
        _testRun.Enabled = false;
        _testList.Items.Clear();
        _testSummary.Text = "";
        var progress = new Progress<string>(t => _testStatus.Text = t);
        try
        {
            var report = await Task.Run(() => CompressibilityTester.Run(20, progress, _cancel.Token));
            if (IsDisposed) return;
            _testList.BeginUpdate();
            foreach (var a in report.Apps)
                _testList.Items.Add(new ListViewItem(new[]
                {
                    a.Label, MemorySnapshot.Mb(a.Commit), MemorySnapshot.Mb(a.SampledBytes), $"{a.ZeroPercent:N0} %",
                    a.Ratio > 0 ? $"{a.Ratio:N1}x" : "-", MemorySnapshot.Mb(a.EstimatedSaving),
                }));
            _testList.EndUpdate();
            _testStatus.Text = $"Done at {report.At:HH:mm}. {report.Apps.Count} apps measured" + (report.Skipped > 0 ? $", {report.Skipped} protected apps skipped." : ".");
            _testSummary.Text =
                $"Overall the sampled memory compresses {report.OverallRatio:N1}x (zero pages not counted). If all {MemorySnapshot.Gb(report.TotalCommit)} these apps reserved " +
                $"were compressed like the sample, it would save about {MemorySnapshot.Gb(report.TotalSaving)}. " +
                (report.StoreCompressed > 0
                    ? $"Windows' compression store already holds {MemorySnapshot.Gb(report.StoreOriginal)} in {MemorySnapshot.Gb(report.StoreCompressed)} ({(double)report.StoreOriginal / report.StoreCompressed:N1}x) - that part is already \"doubled\"."
                    : "Run as administrator to compare with what Windows' compression store already does.");
        }
        catch (OperationCanceledException)
        {
            // window closed
        }
        catch (Exception ex)
        {
            if (!IsDisposed) _testStatus.Text = "Test failed: " + ex.Message;
        }
        finally
        {
            if (!IsDisposed) _testRun.Enabled = true;
        }
    }

    // ───────────────────────────── helpers ─────────────────────────────
    private static ProgressBar Bar() => new() { Dock = DockStyle.Fill, Maximum = 1000, Height = 18, Margin = new Padding(3, 6, 3, 6) };
    private static Label Value() => new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoSize = false };
    private static Label Title(string t) => new() { Text = t, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };

    private static void AddRow(TableLayoutPanel grid, string name, Control bar, Control value)
    {
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        grid.Controls.Add(new Label { Text = name, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        grid.Controls.Add(bar, 1, row);
        grid.Controls.Add(value, 2, row);
    }

    private static void SetBar(ProgressBar bar, double percent) => bar.Value = (int)Math.Clamp(percent * 10, 0, 1000);

    private static void SetBarState(ProgressBar bar, int state)
    {
        if (bar.IsHandleCreated) MemoryNative.SendMessage(bar.Handle, MemoryNative.PBM_SETSTATE, state, IntPtr.Zero);
    }

    private static ListView List(params (string Title, int Width)[] columns)
    {
        var lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false, HideSelection = false };
        foreach (var (t, w) in columns) lv.Columns.Add(t, w);
        return lv;
    }
}

/// <summary>Last hour's RAM %, commit % and compressed store (as % of RAM).</summary>
internal sealed class HistoryGraph : Control
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public MemorySnapshot[] Points { get; set; } = Array.Empty<MemorySnapshot>();

    private static readonly (string Name, Color Color, Func<MemorySnapshot, double> Value)[] Series =
    {
        ("RAM in use", Color.FromArgb(0, 120, 212), s => s.RamPercent),
        ("Commit", Color.FromArgb(216, 110, 0), s => s.CommitPercent),
        ("Compressed store", Color.FromArgb(16, 137, 62), s => s.CompressedStoreRam > 0 && s.RamTotal > 0 ? s.CompressedStoreRam * 100.0 / s.RamTotal : 0),
    };

    public HistoryGraph()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        BackColor = SystemColors.Window;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var area = new Rectangle(44, 22, Math.Max(10, Width - 56), Math.Max(10, Height - 44));
        using var gridPen = new Pen(Color.FromArgb(225, 225, 225));
        using var textBrush = new SolidBrush(SystemColors.GrayText);
        foreach (var pct in new[] { 0, 25, 50, 75, 100 })
        {
            int y = area.Bottom - area.Height * pct / 100;
            g.DrawLine(gridPen, area.Left, y, area.Right, y);
            g.DrawString($"{pct}%", Font, textBrush, 4, y - 7);
        }
        g.DrawString("60 min ago", Font, textBrush, area.Left, area.Bottom + 4);
        var now = "now";
        g.DrawString(now, Font, textBrush, area.Right - g.MeasureString(now, Font).Width, area.Bottom + 4);

        float lx = area.Left;
        foreach (var (name, color, _) in Series)
        {
            using var b = new SolidBrush(color);
            g.FillRectangle(b, lx, 6, 12, 10);
            g.DrawString(name, Font, textBrush, lx + 16, 3);
            lx += 16 + g.MeasureString(name, Font).Width + 18;
        }

        var pts = Points;
        if (pts.Length < 2)
        {
            g.DrawString("Collecting... (one point every 10 seconds)", Font, textBrush, area.Left + 10, area.Top + 10);
            return;
        }
        foreach (var (_, color, value) in Series)
        {
            var line = new PointF[pts.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                // newest point on the right edge; 360 points = one hour
                float x = area.Right - (pts.Length - 1 - i) * (area.Width / (float)(MemoryHistory.Capacity - 1));
                float y = area.Bottom - (float)(Math.Clamp(value(pts[i]), 0, 100) / 100.0 * area.Height);
                line[i] = new PointF(Math.Max(area.Left, x), y);
            }
            using var pen = new Pen(color, 2f);
            g.DrawLines(pen, line);
        }
    }
}

// Developer: Jose Rodriguez Arroyo
// Email: jrpcone@gmail.com
// GitHub: https://github.com/jorodriguezpr

using xFreeRamWindows.Models;

namespace xFreeRamWindows;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _selectTargetItem;
    private readonly ToolStripMenuItem _selectServicesItem;
    private readonly ToolStripMenuItem _selectProcessesItem;
    private readonly ToolStripMenuItem _freeRamItem;
    private readonly ToolStripMenuItem _boostItem;
    private readonly ToolStripMenuItem _restoreItem;
    private readonly ToolStripMenuItem _targetLabelItem;
    private readonly ToolStripMenuItem _statsLabelItem;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly ToolStripMenuItem _aboutItem;
    private readonly System.Windows.Forms.Timer _statsTimer;
    private readonly System.Windows.Forms.Timer _autoFreeTimer;
    private readonly AppSettings _settings;
    private readonly AutoFreeMonitor _autoFree;
    private readonly ToolStripMenuItem _autoFreeMenu;
    private readonly ToolStripMenuItem _autoFreeEnabledItem;
    private readonly ToolStripMenuItem _autoFreeThresholdMenu;
    private readonly ToolStripMenuItem _autoFreeIntervalMenu;
    private readonly ToolStripMenuItem _autoFreeNotifyItem;
    private readonly ToolStripMenuItem _autoFreeStatusItem;
    private double _lastCpu;

    private static readonly int[] ThresholdChoices = { 60, 70, 80, 90 };
    private static readonly int[] IntervalChoices = { 5, 10, 15, 30, 60 };

    public TrayApplicationContext()
    {
        _settings = AppSettings.Load();
        if (!ThresholdChoices.Contains(_settings.AutoFreeThresholdPercent)) _settings.AutoFreeThresholdPercent = 80;
        if (!IntervalChoices.Contains(_settings.AutoFreeIntervalMinutes)) _settings.AutoFreeIntervalMinutes = 10;
        _autoFree = new AutoFreeMonitor(_settings);
        _autoFree.Freed += OnAutoFreed;

        _autoFreeEnabledItem = new ToolStripMenuItem("On", null, OnToggleAutoFree) { Checked = _settings.AutoFreeEnabled };
        _autoFreeThresholdMenu = new ToolStripMenuItem("When RAM in use is at least");
        foreach (var pct in ThresholdChoices)
        {
            _autoFreeThresholdMenu.DropDownItems.Add(new ToolStripMenuItem($"{pct}%", null, (_, _) => SetThreshold(pct))
                { Checked = pct == _settings.AutoFreeThresholdPercent, Tag = pct });
        }
        _autoFreeIntervalMenu = new ToolStripMenuItem("At most every");
        foreach (var min in IntervalChoices)
        {
            _autoFreeIntervalMenu.DropDownItems.Add(new ToolStripMenuItem($"{min} minutes", null, (_, _) => SetInterval(min))
                { Checked = min == _settings.AutoFreeIntervalMinutes, Tag = min });
        }
        _autoFreeNotifyItem = new ToolStripMenuItem("Notify me each time", null, OnToggleAutoFreeNotify) { Checked = _settings.AutoFreeNotify };
        _autoFreeStatusItem = new ToolStripMenuItem("Last run: not yet") { Enabled = false };
        _autoFreeMenu = new ToolStripMenuItem("Auto Free RAM");
        _autoFreeMenu.DropDownItems.Add(_autoFreeEnabledItem);
        _autoFreeMenu.DropDownItems.Add(_autoFreeThresholdMenu);
        _autoFreeMenu.DropDownItems.Add(_autoFreeIntervalMenu);
        _autoFreeMenu.DropDownItems.Add(_autoFreeNotifyItem);
        _autoFreeMenu.DropDownItems.Add(new ToolStripSeparator());
        _autoFreeMenu.DropDownItems.Add(_autoFreeStatusItem);
        _autoFreeMenu.DropDownItems.Add(new ToolStripMenuItem("Never closes apps or stops services") { Enabled = false });

        _statsLabelItem = new ToolStripMenuItem("RAM: -- | CPU: --") { Enabled = false };
        _targetLabelItem = new ToolStripMenuItem("Target: (none selected)") { Enabled = false };
        _selectTargetItem = new ToolStripMenuItem("Select Target App...", null, OnSelectTarget);
        _selectServicesItem = new ToolStripMenuItem("Select Services to Stop...", null, OnSelectServices);
        _selectProcessesItem = new ToolStripMenuItem("Select Processes to Close...", null, OnSelectProcesses);
        _freeRamItem = new ToolStripMenuItem("Free Up RAM Now", null, OnFreeRam);
        _boostItem = new ToolStripMenuItem("Boost Now", null, OnBoost);
        _restoreItem = new ToolStripMenuItem("Restore", null, OnRestore) { Enabled = false };
        _autoStartItem = new ToolStripMenuItem("Start with Windows", null, OnToggleAutoStart) { Checked = AutoStartManager.IsEnabled() };
        _aboutItem = new ToolStripMenuItem("About...", null, OnAbout);
        var exitItem = new ToolStripMenuItem("Exit", null, OnExit);

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statsLabelItem);
        menu.Items.Add(_targetLabelItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_freeRamItem);
        menu.Items.Add(_autoFreeMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_selectTargetItem);
        menu.Items.Add(_selectServicesItem);
        menu.Items.Add(_selectProcessesItem);
        menu.Items.Add(_boostItem);
        menu.Items.Add(_restoreItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_aboutItem);
        menu.Items.Add(exitItem);

        _trayIcon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application,
            Text = "xFreeRamWindows",
            Visible = true,
            ContextMenuStrip = menu,
        };

        _statsTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _statsTimer.Tick += (_, _) => RefreshStats();
        _statsTimer.Start();

        _autoFreeTimer = new System.Windows.Forms.Timer { Interval = AutoFreeMonitor.CheckSeconds * 1000 };
        _autoFreeTimer.Tick += (_, _) => _autoFree.Check(_lastCpu);
        _autoFreeTimer.Start();

        RefreshTargetLabel();
        RefreshStats();
        RefreshAutoFreeLabels();
    }

    private void RefreshAutoFreeLabels()
    {
        _autoFreeMenu.Text = _settings.AutoFreeEnabled
            ? $"Auto Free RAM: on (at {_settings.AutoFreeThresholdPercent}%)"
            : "Auto Free RAM: off";
        _autoFreeStatusItem.Text = _autoFree.LastRunAt is { } at && _autoFree.LastResult is { } r
            ? $"Last run: {at:HH:mm}, {Math.Max(0, r.FreedMB):N0} MB freed"
            : "Last run: not yet";
        foreach (ToolStripMenuItem item in _autoFreeThresholdMenu.DropDownItems)
            item.Checked = (int)item.Tag! == _settings.AutoFreeThresholdPercent;
        foreach (ToolStripMenuItem item in _autoFreeIntervalMenu.DropDownItems)
            item.Checked = (int)item.Tag! == _settings.AutoFreeIntervalMinutes;
    }

    private void OnToggleAutoFree(object? sender, EventArgs e)
    {
        _settings.AutoFreeEnabled = !_settings.AutoFreeEnabled;
        _autoFreeEnabledItem.Checked = _settings.AutoFreeEnabled;
        _settings.Save();
        RefreshAutoFreeLabels();
    }

    private void OnToggleAutoFreeNotify(object? sender, EventArgs e)
    {
        _settings.AutoFreeNotify = !_settings.AutoFreeNotify;
        _autoFreeNotifyItem.Checked = _settings.AutoFreeNotify;
        _settings.Save();
    }

    private void SetThreshold(int pct)
    {
        _settings.AutoFreeThresholdPercent = pct;
        _settings.Save();
        RefreshAutoFreeLabels();
    }

    private void SetInterval(int minutes)
    {
        _settings.AutoFreeIntervalMinutes = minutes;
        _settings.Save();
        RefreshAutoFreeLabels();
    }

    private void OnAutoFreed(FreeRamResult result)
    {
        RefreshStats();
        RefreshAutoFreeLabels();
        if (!_settings.AutoFreeNotify) return;

        _trayIcon.BalloonTipTitle = "RAM freed automatically";
        _trayIcon.BalloonTipText = result.FreedMB > 0
            ? $"~{result.FreedMB:N0} MB more free now."
            : "Memory is in use by running apps; nothing more to free right now.";
        _trayIcon.ShowBalloonTip(3000);
    }

    private void RefreshStats()
    {
        var mem = SystemStats.GetMemoryStatus();
        var cpu = SystemStats.SampleCpuUsagePercent();
        _lastCpu = cpu;

        _statsLabelItem.Text = $"RAM free: {mem.AvailableMB:N0} MB / {mem.TotalMB:N0} MB | CPU: {cpu:F0}%";

        var tooltip = $"xFreeRamWindows | RAM free {mem.AvailableMB:N0}MB | CPU {cpu:F0}%";
        _trayIcon.Text = tooltip.Length <= 63 ? tooltip : tooltip[..63];
    }

    private void RefreshTargetLabel()
    {
        _targetLabelItem.Text = string.IsNullOrEmpty(_settings.TargetExePath)
            ? "Target: (none selected)"
            : $"Target: {Path.GetFileName(_settings.TargetExePath)}";
        _boostItem.Enabled = !string.IsNullOrEmpty(_settings.TargetExePath) && BoostEngine.CurrentSnapshot is null;
        _restoreItem.Enabled = BoostEngine.CurrentSnapshot is not null;
    }

    private void OnSelectTarget(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Applications (*.exe)|*.exe",
            Title = "Select the application to keep at full resources",
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;

        _settings.TargetExePath = dialog.FileName;
        _settings.Save();
        RefreshTargetLabel();
    }

    private void OnSelectServices(object? sender, EventArgs e)
    {
        using var dialog = new ServiceSelectionForm(_settings.ServicesToStop);
        if (dialog.ShowDialog() != DialogResult.OK) return;

        _settings.ServicesToStop = dialog.SelectedServiceNames.ToList();
        _settings.Save();
    }

    private void OnSelectProcesses(object? sender, EventArgs e)
    {
        string? targetProcessName = string.IsNullOrEmpty(_settings.TargetExePath)
            ? null
            : Path.GetFileNameWithoutExtension(_settings.TargetExePath);

        using var dialog = new ProcessSelectionForm(_settings.ProcessesToStop, targetProcessName);
        if (dialog.ShowDialog() != DialogResult.OK) return;

        _settings.ProcessesToStop = dialog.SelectedProcessNames.ToList();
        _settings.Save();
    }

    private void OnFreeRam(object? sender, EventArgs e)
    {
        _freeRamItem.Enabled = false;
        var result = BoostEngine.FreeUpRamNow();
        _freeRamItem.Enabled = true;
        RefreshStats();

        _trayIcon.BalloonTipTitle = "RAM freed";
        _trayIcon.BalloonTipText =
            $"Trimmed {result.ProcessesTrimmed} process(es)." +
            (result.StandbyListPurged ? " Standby memory purged." : "") +
            (result.FreedMB > 0 ? $" ~{result.FreedMB:N0} MB more free now." : "");
        _trayIcon.ShowBalloonTip(4000);
    }

    private void OnBoost(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_settings.TargetExePath)) return;

        if (_settings.ServicesToStop.Count == 0 && _settings.ProcessesToStop.Count == 0)
        {
            MessageBox.Show(
                "Nothing is selected to stop. Use \"Select Services to Stop...\" and/or " +
                "\"Select Processes to Close...\" first.",
                "xFreeRamWindows",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"This will close the {_settings.ProcessesToStop.Count} process(es) and stop the " +
            $"{_settings.ServicesToStop.Count} service(s) you've selected that are currently " +
            "running. Protected/essential items are never touched even if selected. This does " +
            "not modify system memory directly." +
            "\n\nContinue?",
            "xFreeRamWindows - Boost Mode",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        _boostItem.Enabled = false;
        var result = BoostEngine.Boost(_settings.TargetExePath, _settings.ServicesToStop, _settings.ProcessesToStop);
        RefreshTargetLabel();

        _trayIcon.BalloonTipTitle = "Boost complete";
        _trayIcon.BalloonTipText =
            $"Closed {result.ProcessesStopped} process(es) and stopped {result.ServicesStopped} service(s)." +
            (result.Errors.Count > 0 ? $" {result.Errors.Count} item(s) skipped (protected/in use)." : "");
        _trayIcon.ShowBalloonTip(4000);
    }

    private void OnRestore(object? sender, EventArgs e)
    {
        var result = BoostEngine.RestoreAll();
        RefreshTargetLabel();

        _trayIcon.BalloonTipTitle = "Restore complete";
        _trayIcon.BalloonTipText =
            $"Restarted {result.ServicesRestarted} service(s). Closed apps were not relaunched.";
        _trayIcon.ShowBalloonTip(4000);
    }

    private void OnToggleAutoStart(object? sender, EventArgs e)
    {
        var enabling = !_autoStartItem.Checked;
        var ok = enabling ? AutoStartManager.Enable() : AutoStartManager.Disable();
        if (ok)
        {
            _autoStartItem.Checked = enabling;
        }
        else
        {
            MessageBox.Show(
                $"Could not {(enabling ? "enable" : "disable")} start with Windows.",
                "xFreeRamWindows",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OnAbout(object? sender, EventArgs e)
    {
        using var dialog = new AboutForm();
        dialog.ShowDialog();
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _statsTimer.Stop();
        _autoFreeTimer.Stop();
        _trayIcon.Visible = false;
        Application.Exit();
    }
}

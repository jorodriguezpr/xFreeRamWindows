// Developer: Jose Rodriguez Arroyo
// Email: jrpcone@gmail.com
// GitHub: https://github.com/jorodriguezpr

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
    private readonly System.Windows.Forms.Timer _statsTimer;
    private readonly AppSettings _settings;

    public TrayApplicationContext()
    {
        _settings = AppSettings.Load();

        _statsLabelItem = new ToolStripMenuItem("RAM: -- | CPU: --") { Enabled = false };
        _targetLabelItem = new ToolStripMenuItem("Target: (none selected)") { Enabled = false };
        _selectTargetItem = new ToolStripMenuItem("Select Target App...", null, OnSelectTarget);
        _selectServicesItem = new ToolStripMenuItem("Select Services to Stop...", null, OnSelectServices);
        _selectProcessesItem = new ToolStripMenuItem("Select Processes to Close...", null, OnSelectProcesses);
        _freeRamItem = new ToolStripMenuItem("Free Up RAM Now", null, OnFreeRam);
        _boostItem = new ToolStripMenuItem("Boost Now", null, OnBoost);
        _restoreItem = new ToolStripMenuItem("Restore", null, OnRestore) { Enabled = false };
        _autoStartItem = new ToolStripMenuItem("Start with Windows", null, OnToggleAutoStart) { Checked = AutoStartManager.IsEnabled() };
        var exitItem = new ToolStripMenuItem("Exit", null, OnExit);

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statsLabelItem);
        menu.Items.Add(_targetLabelItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_freeRamItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_selectTargetItem);
        menu.Items.Add(_selectServicesItem);
        menu.Items.Add(_selectProcessesItem);
        menu.Items.Add(_boostItem);
        menu.Items.Add(_restoreItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
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

        RefreshTargetLabel();
        RefreshStats();
    }

    private void RefreshStats()
    {
        var mem = SystemStats.GetMemoryStatus();
        var cpu = SystemStats.SampleCpuUsagePercent();

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

    private void OnExit(object? sender, EventArgs e)
    {
        _statsTimer.Stop();
        _trayIcon.Visible = false;
        Application.Exit();
    }
}

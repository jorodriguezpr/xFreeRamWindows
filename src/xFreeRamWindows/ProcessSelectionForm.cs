// Developer: Jose Rodriguez Arroyo
// Email: jrpcone@gmail.com
// GitHub: https://github.com/jorodriguezpr

using System.Diagnostics;

namespace xFreeRamWindows;

/// <summary>
/// Lets the user pick exactly which currently-running processes Boost Now is allowed to
/// close. Kernel/session-critical and shell processes on <see cref="ProtectedList"/>, this
/// app's own process, and the selected target app's process are never listed here — they
/// can't be selected no matter what, and <see cref="BoostEngine"/> re-checks the same
/// exclusions again at close time regardless of what this dialog saved. Rows are grouped by
/// process name (one row can represent several running instances, e.g. multiple browser
/// tabs' helper processes) since that's how the user picks and how Boost Now matches them.
/// </summary>
internal sealed class ProcessSelectionForm : Form
{
    private readonly ListView _listView;

    public HashSet<string> SelectedProcessNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ProcessSelectionForm(IReadOnlyCollection<string> currentlySelected, string? targetProcessName)
    {
        Text = "Select Processes Boost Now Can Close";
        Width = 640;
        Height = 520;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        _listView = new ListView
        {
            View = View.Details,
            CheckBoxes = true,
            FullRowSelect = true,
            Dock = DockStyle.Top,
            Height = 420,
        };
        _listView.Columns.Add("Process", 200);
        _listView.Columns.Add("Window Title", 240);
        _listView.Columns.Add("Memory (MB)", 100);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.RightToLeft,
        };
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var selectNoneButton = new Button { Text = "Select None", AutoSize = true };
        var selectAllButton = new Button { Text = "Select All", AutoSize = true };
        selectNoneButton.Click += (_, _) => SetAllChecked(false);
        selectAllButton.Click += (_, _) => SetAllChecked(true);
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(okButton);
        buttonPanel.Controls.Add(selectNoneButton);
        buttonPanel.Controls.Add(selectAllButton);

        Controls.Add(_listView);
        Controls.Add(buttonPanel);
        AcceptButton = okButton;
        CancelButton = cancelButton;

        PopulateProcesses(currentlySelected, targetProcessName);

        okButton.Click += (_, _) => CollectSelection();
    }

    private void SetAllChecked(bool value)
    {
        foreach (ListViewItem item in _listView.Items) item.Checked = value;
    }

    private void PopulateProcesses(IReadOnlyCollection<string> currentlySelected, string? targetProcessName)
    {
        var alreadySelected = new HashSet<string>(currentlySelected, StringComparer.OrdinalIgnoreCase);
        var currentProcessId = Environment.ProcessId;

        var grouped = new Dictionary<string, (long MemoryBytes, string Title)>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == currentProcessId) continue;
                if (!string.IsNullOrEmpty(targetProcessName) &&
                    string.Equals(process.ProcessName, targetProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (ProtectedList.IsProtectedProcess(process.ProcessName)) continue;

                long memory;
                string title;
                try
                {
                    memory = process.WorkingSet64;
                    title = process.MainWindowTitle;
                }
                catch
                {
                    continue; // No access to this instance — skip rather than show a half-populated row.
                }

                if (grouped.TryGetValue(process.ProcessName, out var existing))
                {
                    grouped[process.ProcessName] = (
                        existing.MemoryBytes + memory,
                        string.IsNullOrEmpty(existing.Title) ? title : existing.Title);
                }
                else
                {
                    grouped[process.ProcessName] = (memory, title);
                }
            }
        }

        var entries = new List<ListViewItem>();
        foreach (var (name, info) in grouped)
        {
            var item = new ListViewItem(name) { Tag = name };
            item.SubItems.Add(info.Title);
            item.SubItems.Add((info.MemoryBytes / 1024 / 1024).ToString("N0"));
            item.Checked = alreadySelected.Contains(name);
            entries.Add(item);
        }

        entries.Sort((a, b) => string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase));
        _listView.Items.AddRange(entries.ToArray());
    }

    private void CollectSelection()
    {
        SelectedProcessNames.Clear();
        foreach (ListViewItem item in _listView.CheckedItems)
        {
            if (item.Tag is string processName) SelectedProcessNames.Add(processName);
        }
    }
}

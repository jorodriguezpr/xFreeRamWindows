// Developer: Jose Rodriguez Arroyo
// Email: jrpcone@gmail.com
// GitHub: https://github.com/jorodriguezpr

using System.ServiceProcess;

namespace xFreeRamWindows;

/// <summary>
/// Lets the user pick exactly which currently-running services Boost Now is allowed to
/// stop. Services required for Safe Mode with Networking (<see cref="SafeBootServices"/>)
/// or on the <see cref="ProtectedList"/> backstop are never listed here — they can't be
/// selected no matter what the user chooses, and <see cref="BoostEngine"/> re-checks both
/// again at stop time regardless of what this dialog saved.
/// </summary>
internal sealed class ServiceSelectionForm : Form
{
    private readonly ListView _listView;

    public HashSet<string> SelectedServiceNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ServiceSelectionForm(IReadOnlyCollection<string> currentlySelected)
    {
        Text = "Select Services Boost Now Can Stop";
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
        _listView.Columns.Add("Display Name", 260);
        _listView.Columns.Add("Service Name", 140);
        _listView.Columns.Add("Publisher", 140);

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

        PopulateServices(currentlySelected);

        okButton.Click += (_, _) => CollectSelection();
    }

    private void SetAllChecked(bool value)
    {
        foreach (ListViewItem item in _listView.Items) item.Checked = value;
    }

    private void PopulateServices(IReadOnlyCollection<string> currentlySelected)
    {
        var alreadySelected = new HashSet<string>(currentlySelected, StringComparer.OrdinalIgnoreCase);

        // Best-effort: if the registry read fails, essentialNames comes back small/incomplete
        // rather than null. Worst case here is an essential service shows up as pickable —
        // BoostEngine independently re-verifies this same set at stop time and fails closed
        // there, so this dialog being slightly stale is not itself a safety hole.
        SafeBootServices.TryGetEssentialServiceNames(out var essentialNames);

        var entries = new List<ListViewItem>();
        foreach (var sc in ServiceController.GetServices())
        {
            using (sc)
            {
                if (sc.Status is not (ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)) continue;
                if (essentialNames.Contains(sc.ServiceName)) continue;
                if (ProtectedList.IsProtectedService(sc.ServiceName)) continue;

                var item = new ListViewItem(sc.DisplayName) { Tag = sc.ServiceName };
                item.SubItems.Add(sc.ServiceName);
                item.SubItems.Add(ServicePublisher.GetPublisherLabel(sc.ServiceName));
                item.Checked = alreadySelected.Contains(sc.ServiceName);
                entries.Add(item);
            }
        }

        entries.Sort((a, b) => string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase));
        _listView.Items.AddRange(entries.ToArray());
    }

    private void CollectSelection()
    {
        SelectedServiceNames.Clear();
        foreach (ListViewItem item in _listView.CheckedItems)
        {
            if (item.Tag is string serviceName) SelectedServiceNames.Add(serviceName);
        }
    }
}

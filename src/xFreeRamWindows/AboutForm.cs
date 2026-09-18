using System.Diagnostics;
using System.Reflection;

namespace xFreeRamWindows;

internal sealed class AboutForm : Form
{
    private const string RepoUrl = "https://github.com/jorodriguezpr/xFreeRamWindows";

    public AboutForm()
    {
        Text = "About xFreeRamWindows";
        Width = 460;
        Height = 300;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowIcon = false;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = version is null ? "" : $"v{version.Major}.{version.Minor}.{version.Build}";

        var iconBox = new PictureBox
        {
            Image = SystemIcons.Application.ToBitmap(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(20, 20),
            Size = new Size(48, 48),
        };
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon is not null) iconBox.Image = icon.ToBitmap();
        }
        catch
        {
            // Keep the SystemIcons.Application fallback set above.
        }

        var titleLabel = new Label
        {
            Text = $"xFreeRamWindows {versionText}",
            Font = new Font(Font.FontFamily, 13, FontStyle.Bold),
            Location = new Point(80, 25),
            AutoSize = true,
        };

        var infoLabel = new Label
        {
            Text = "Developer: Jose Rodriguez Arroyo\n" +
                   "Email: jrpcone@gmail.com\n" +
                   "License: Apache License 2.0",
            Location = new Point(20, 90),
            Size = new Size(340, 60),
        };

        var linkLabel = new LinkLabel
        {
            Text = RepoUrl,
            Location = new Point(20, 155),
            AutoSize = true,
        };
        linkLabel.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true });
            }
            catch
            {
                // Best-effort - nothing sensible to do if the shell can't open a browser here.
            }
        };

        var closeButton = new Button
        {
            Text = "Close",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Location = new Point(350, 210),
        };

        Controls.Add(iconBox);
        Controls.Add(titleLabel);
        Controls.Add(infoLabel);
        Controls.Add(linkLabel);
        Controls.Add(closeButton);
        AcceptButton = closeButton;
        CancelButton = closeButton;
    }
}

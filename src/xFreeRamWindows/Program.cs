// Developer: Jose Rodriguez Arroyo
// Email: jrpcone@gmail.com
// GitHub: https://github.com/jorodriguezpr

namespace xFreeRamWindows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstanceMutex = new Mutex(true, "xFreeRamWindows_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("xFreeRamWindows is already running (check the system tray).", "xFreeRamWindows",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.Run(new TrayApplicationContext());
    }
}

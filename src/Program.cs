namespace XMouse;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        bool startMinimized = args.Any(a =>
            a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/minimized", StringComparison.OrdinalIgnoreCase));

        using var mutex = new Mutex(true, "xmouse_single_instance_mutex", out bool isNewInstance);
        if (!isNewInstance)
        {
            MessageBox.Show("xmouse sudah berjalan (cek tray icon).", "xmouse",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.Run(new MainForm(startMinimized));
    }
}

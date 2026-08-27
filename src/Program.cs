namespace XMouse;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Tangani exception yang tidak tertangani di UI thread & thread lain
        // supaya aplikasi tidak diam-diam hang/Not Responding tanpa pesan apapun --
        // sebelumnya exception di tempat tak terduga bisa membuat message loop
        // tersangkut tanpa penjelasan ke pengguna.
        Application.ThreadException += (_, e) =>
            MessageBox.Show($"xmouse mengalami error tak terduga:\n{e.Exception.Message}",
                "xmouse - Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                MessageBox.Show($"xmouse mengalami error fatal:\n{ex.Message}",
                    "xmouse - Error Fatal", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

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

        try
        {
            Application.Run(new MainForm(startMinimized));
        }
        catch (InvalidOperationException ex)
        {
            // Kemungkinan besar gagal pasang hook (butuh Administrator, dsb).
            MessageBox.Show(ex.Message, "xmouse - Gagal Memulai",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

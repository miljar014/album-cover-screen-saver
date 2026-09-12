using AlbumCoverScreenSaver.Shared;

namespace AlbumCoverScreenSaver.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // One instance only. Two copies would poll the same session, download
        // the same covers twice, and race each other writing the archive.
        using var only = new Mutex(initiallyOwned: true, "AlbumCoverScreenSaverTray", out var isFirst);
        if (!isFirst) return;

        Log.Start("Album Cover Screen Saver, tray app");
        Log.Write($"data folder: {SharedStore.DefaultRoot()}");

        // Declared before any window exists, so the settings window is sharp on
        // a scaled display rather than being bitmap-stretched by Windows. The
        // screen saver gets the same thing from its manifest.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            Application.Run(new TrayApp());
        }
        catch (Exception error)
        {
            // Never let this escape. A tray app that throws simply vanishes
            // from the tray, with nothing said anywhere.
            Log.Failure("Main", error);
        }
    }
}

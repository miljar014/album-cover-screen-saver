using AlbumCoverScreenSaver.Shared;

namespace AlbumCoverScreenSaver.Saver;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var command = SaverCommandLine.Parse(args);

        Log.Start($"Album Cover Screen Saver, {command}");
        Log.Write($"arguments: {(args.Length == 0 ? "(none)" : string.Join(" ", args))}");

        // Both halves of the product have to agree on this folder. Proving it
        // with a log line is cheaper than finding out later that settings
        // appear to do nothing because they are written to one place and read
        // from another.
        Log.Write(SharedStore.Default.Describe());

        try
        {
            return command.Mode switch
            {
                SaverMode.FullScreen => SaverHost.RunFullScreen(),
                SaverMode.Preview => SaverHost.RunPreview(command.ParentWindow),
                SaverMode.Ignore => 0,
                _ => ShowSettings(),
            };
        }
        catch (Exception error)
        {
            // Never let this escape. A screen saver that throws is reported by
            // Windows as nothing at all: it simply reverts to the previously
            // selected saver, with no error anywhere. The log is the only trace.
            Log.Failure("Main", error);
            return 1;
        }
    }

    /// <summary>
    /// Settings live in the tray app, not in here. A nineteen-style settings
    /// window hosted inside a .scr launched by the Screen Saver dialog is a
    /// poor place for them, which is the same conclusion the macOS build
    /// reached about Apple's Options button.
    /// </summary>
    private static int ShowSettings()
    {
        Log.Write("settings requested, but the tray app does not exist yet (step 5)");

        Native.MessageBoxW(
            0,
            "Settings for Album Cover Screen Saver will live in its tray app.\n\n" +
            "That app is built in step 5. Until then there is nothing to change here.\n\n" +
            $"Data folder:\n{SharedStore.DefaultRoot()}",
            "Album Cover Screen Saver",
            Native.MB_OK | Native.MB_ICONINFORMATION);

        return 0;
    }
}

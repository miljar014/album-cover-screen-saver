using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AlbumCoverScreenSaver.Tray;

/// <summary>
/// Makes this the user's screen saver, without needing administrator rights.
/// </summary>
/// <remarks>
/// <para>
/// Windows builds its Screen Saver dropdown by listing <c>.scr</c> files in
/// <c>%SystemRoot%\System32</c>, which needs administrator rights to write to.
/// We do not go there. <c>SCRNSAVE.EXE</c> under
/// <c>HKCU\Control Panel\Desktop</c> accepts a fully qualified path, so the
/// saver can live in the app's own folder and still be the selected one.
/// </para>
/// <para>
/// <b>The honest caveat:</b> installed this way it does not appear in the
/// Windows dropdown list, though it does show as the one selected. The tray app
/// becomes the place to choose it and set the timeout, which is where all the
/// other settings already are, and is the same shape the Mac version ended up
/// with. The alternative is demanding administrator rights at install time,
/// which is a worse trade.
/// </para>
/// </remarks>
internal static class SaverInstaller
{
    private const string DesktopKey = @"Control Panel\Desktop";

    private const uint SPI_SETSCREENSAVETIMEOUT = 0x000F;
    private const uint SPI_SETSCREENSAVEACTIVE = 0x0011;
    private const uint SPIF_UPDATEINIFILE = 0x01;
    private const uint SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfoW(
        uint action, uint param, nint pointer, uint winIni);

    /// <summary>Where the .scr is, or null if it cannot be found.</summary>
    public static string? FindScreenSaver()
    {
        var appFolder = AppContext.BaseDirectory;

        // Shipping layout: the .scr sits beside the tray app.
        var beside = Path.Combine(appFolder, "AlbumCoverScreenSaver.scr");
        if (File.Exists(beside)) return beside;

        // Development layout: the two projects build into sibling bin folders.
        try
        {
            var directory = new DirectoryInfo(appFolder);
            while (directory is not null && !string.Equals(directory.Name, "src", StringComparison.OrdinalIgnoreCase))
            {
                directory = directory.Parent;
            }

            if (directory is not null)
            {
                var found = directory
                    .GetFiles("AlbumCoverScreenSaver.scr", SearchOption.AllDirectories)
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (found is not null) return found.FullName;
            }
        }
        catch (Exception error)
        {
            Log.Failure("looking for the screen saver", error);
        }

        return null;
    }

    /// <summary>The path Windows currently has selected, or null.</summary>
    public static string? CurrentlySelected()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(DesktopKey);
            return key?.GetValue("SCRNSAVE.EXE") as string;
        }
        catch (Exception error)
        {
            Log.Failure("reading the selected screen saver", error);
            return null;
        }
    }

    public static bool IsInstalled()
    {
        var selected = CurrentlySelected();
        if (string.IsNullOrEmpty(selected)) return false;

        return Path.GetFileName(selected)
            .Equals("AlbumCoverScreenSaver.scr", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>How long Windows waits before starting a screen saver, in seconds.</summary>
    public static int CurrentTimeoutSeconds()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(DesktopKey);
            var value = key?.GetValue("ScreenSaveTimeOut") as string;
            return int.TryParse(value, out var seconds) && seconds > 0 ? seconds : 300;
        }
        catch (Exception error)
        {
            Log.Failure("reading the screen saver timeout", error);
            return 300;
        }
    }

    /// <summary>
    /// Selects this saver and sets the idle timeout.
    /// </summary>
    /// <remarks>
    /// The registry values alone are not enough: Windows caches them, so
    /// SystemParametersInfo is called afterwards with UPDATEINIFILE and
    /// SENDCHANGE, which is what makes it take effect now rather than at the
    /// next sign-in.
    /// </remarks>
    public static bool Install(int timeoutSeconds, out string message)
    {
        var saver = FindScreenSaver();
        if (saver is null)
        {
            message = "The screen saver has not been built yet, so there is nothing to install.";
            Log.Write("install refused: no .scr found");
            return false;
        }

        timeoutSeconds = Math.Clamp(timeoutSeconds, 60, 60 * 60 * 4);

        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(DesktopKey, writable: true))
            {
                if (key is null)
                {
                    message = "Windows would not let the setting be written.";
                    return false;
                }

                key.SetValue("SCRNSAVE.EXE", saver);
                key.SetValue("ScreenSaveActive", "1");
                key.SetValue("ScreenSaveTimeOut", timeoutSeconds.ToString());
            }

            SystemParametersInfoW(SPI_SETSCREENSAVEACTIVE, 1, 0, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            SystemParametersInfoW(SPI_SETSCREENSAVETIMEOUT, (uint)timeoutSeconds, 0,
                SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

            Log.Write($"installed as the screen saver: {saver}, idle {timeoutSeconds}s");

            var minutes = timeoutSeconds / 60;
            message = "Done. Album Cover Screen Saver will start after "
                      + $"{minutes} minute{(minutes == 1 ? "" : "s")} of no activity.\n\n"
                      + "It will not appear in the Windows Screen Saver list. That is "
                      + "expected: Windows only lists screen savers kept in a protected "
                      + "system folder, and putting it there would need administrator "
                      + "rights. It is still the one selected.\n\n"
                      + "One thing to avoid: if you open the Windows Screen Saver dialog "
                      + "it will probably show (None), because it cannot find this one in "
                      + "its own list. Close that dialog with Cancel. Pressing OK there "
                      + "writes the (None) back and switches this off.";
            return true;
        }
        catch (Exception error)
        {
            Log.Failure("installing the screen saver", error);
            message = $"It could not be installed: {error.Message}";
            return false;
        }
    }

    /// <summary>Turns the screen saver off, leaving whatever was selected alone.</summary>
    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(DesktopKey, writable: true);
            key?.SetValue("ScreenSaveActive", "0");
            SystemParametersInfoW(SPI_SETSCREENSAVEACTIVE, 0, 0, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            Log.Write("screen saver turned off");
        }
        catch (Exception error)
        {
            Log.Failure("turning the screen saver off", error);
        }
    }
}

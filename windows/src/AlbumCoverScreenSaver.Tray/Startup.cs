using Microsoft.Win32;

namespace AlbumCoverScreenSaver.Tray;

/// <summary>
/// Whether the tray app starts with Windows.
/// </summary>
/// <remarks>
/// The per-user Run key, not a scheduled task and not the machine-wide key.
/// It needs no administrator rights, it is the place a user can find and undo
/// it themselves, and it is what Task Manager's Startup tab lists.
/// </remarks>
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AlbumCoverScreenSaver";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string existing && existing.Length > 0;
        }
        catch (Exception error)
        {
            Log.Failure("reading the startup setting", error);
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return;

            if (enabled)
            {
                var path = Environment.ProcessPath;
                if (string.IsNullOrEmpty(path)) return;

                // Quoted, because the path runs through Program Files or a user
                // folder and an unquoted path with a space in it is a decades
                // old way to launch the wrong program.
                key.SetValue(ValueName, $"\"{path}\"");
                Log.Write($"start with Windows enabled: {path}");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Log.Write("start with Windows disabled");
            }
        }
        catch (Exception error)
        {
            Log.Failure("writing the startup setting", error);
        }
    }
}

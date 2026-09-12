using AlbumCoverScreenSaver.Shared;

namespace AlbumCoverScreenSaver.Tray;

/// <summary>
/// A plain text log beside the data, separate from the screen saver's.
/// </summary>
/// <remarks>
/// A tray app has nowhere to print either. It runs for hours with no window,
/// and the interesting failures are the quiet ones: a cover that never
/// downloads, a player the media API does not report, an archive write refused
/// by something. None of those announce themselves.
/// </remarks>
internal static class Log
{
    private const long MaximumBytes = 512 * 1024;

    private static readonly object Gate = new();
    private static string? _path;

    public static string Path => _path ??= System.IO.Path.Combine(SharedStore.DefaultRoot(), "tray.log");

    public static void Start(string what)
    {
        try
        {
            Directory.CreateDirectory(SharedStore.DefaultRoot());

            // Unlike the saver, this runs for days, so the log is trimmed rather
            // than truncated: recent history is worth more than a clean start.
            if (File.Exists(Path) && new FileInfo(Path).Length > MaximumBytes)
            {
                var keep = File.ReadAllLines(Path);
                File.WriteAllLines(Path, keep.Skip(Math.Max(0, keep.Length - 2000)));
            }

            Write($"==== {what} ====");
        }
        catch (Exception)
        {
            // An app that cannot log must still run.
        }
    }

    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch (Exception)
            {
            }
        }
    }

    public static void Failure(string where, Exception error) =>
        Write($"FAILED in {where}: {error.GetType().Name}: {error.Message}");
}

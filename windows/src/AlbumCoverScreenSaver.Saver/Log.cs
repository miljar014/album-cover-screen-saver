using AlbumCoverScreenSaver.Shared;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// A plain text log beside the data.
/// </summary>
/// <remarks>
/// A screen saver has nowhere to print. It is launched by Windows, it has no
/// console, and when it fails the only symptom is that the screen does not go
/// dark, or that Windows quietly reverts to the previously selected saver. The
/// macOS build hit exactly that and it took a long time to find, because there
/// is no error anywhere.
///
/// So this writes what it did to a file. It is the only way to find out what
/// happened inside a run nobody could watch.
/// </remarks>
internal static class Log
{
    private static readonly object Gate = new();
    private static string? _path;

    public static string Path => _path ??= System.IO.Path.Combine(SharedStore.DefaultRoot(), "saver.log");

    public static void Start(string what)
    {
        try
        {
            Directory.CreateDirectory(SharedStore.DefaultRoot());
            // Truncate each run. A log that grows forever gets ignored.
            File.WriteAllText(Path, $"{Stamp()} ==== {what} ===={Environment.NewLine}");
        }
        catch (Exception)
        {
            // A saver that cannot log must still run.
        }
    }

    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                File.AppendAllText(Path, $"{Stamp()} {message}{Environment.NewLine}");
            }
            catch (Exception)
            {
            }
        }
    }

    public static void Failure(string where, Exception error) =>
        Write($"FAILED in {where}: {error.GetType().Name}: {error.Message}{Environment.NewLine}{error.StackTrace}");

    private static string Stamp() => DateTime.Now.ToString("HH:mm:ss.fff");
}

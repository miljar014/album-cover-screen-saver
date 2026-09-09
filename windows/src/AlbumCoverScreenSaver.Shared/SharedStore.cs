using System.Text;
using System.Text.Json;

namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// The one folder where the tray app and the screen saver meet.
/// </summary>
/// <remarks>
/// <para>
/// The macOS build writes every file to two locations and reads whichever is
/// newer, because of how its sandbox works. None of that machinery exists here
/// and none of it should be reintroduced. Both Windows processes run as the
/// same user with full access to one folder:
/// </para>
/// <code>
/// %LOCALAPPDATA%\AlbumCoverScreenSaver\
///     archive.json
///     nowplaying.json
///     settings.json
///     art\&lt;albumID&gt;.jpg
/// </code>
/// <para>
/// If you ever find yourself writing "whichever root is newest", stop: that is
/// a fix for a problem this platform does not have.
/// </para>
/// <para>
/// The class takes its root as a constructor argument rather than resolving it
/// statically, so tests can point it at a temporary folder. Everything shipping
/// uses <see cref="Default"/>.
/// </para>
/// </remarks>
public sealed class SharedStore
{
    public const string FolderName = "AlbumCoverScreenSaver";

    /// <summary>
    /// Artwork below this size is refused. It is how a truncated download or a
    /// saved error page gets caught before it reaches the archive as a cover.
    /// </summary>
    public const int MinimumArtBytes = 512;

    private static readonly Lazy<SharedStore> Shared = new(() => new SharedStore(DefaultRoot()));

    /// <summary>The store both shipping processes use.</summary>
    public static SharedStore Default => Shared.Value;

    private readonly object _errorLock = new();
    private readonly List<string> _writeErrors = new();

    public SharedStore(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Root is required.", nameof(root));
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }

    public string ArtDirectory => Path.Combine(Root, "art");
    public string ArchivePath => Path.Combine(Root, "archive.json");
    public string NowPlayingPath => Path.Combine(Root, "nowplaying.json");
    public string SettingsPath => Path.Combine(Root, "settings.json");

    public string ArtFile(string albumId) => Path.Combine(ArtDirectory, albumId + ".jpg");

    /// <summary>
    /// Where the shared folder lives. <c>%LOCALAPPDATA%</c> on Windows, and the
    /// platform equivalent elsewhere so the library can be exercised off
    /// Windows.
    /// </summary>
    public static string DefaultRoot()
    {
        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (string.IsNullOrWhiteSpace(local))
        {
            local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }
        return Path.Combine(local, FolderName);
    }

    /// <summary>
    /// Both halves agreeing on the folder is the thing to prove with a log
    /// line, not assume. Settings appearing to do nothing was exactly this bug
    /// on macOS: written to one place, read from another.
    /// </summary>
    public string Describe() =>
        $"SharedStore root={Root} exists={Directory.Exists(Root)} " +
        $"archive={File.Exists(ArchivePath)} settings={File.Exists(SettingsPath)} " +
        $"nowplaying={File.Exists(NowPlayingPath)} art={ArtCount()}";

    public int ArtCount() =>
        Directory.Exists(ArtDirectory) ? Directory.EnumerateFiles(ArtDirectory, "*.jpg").Count() : 0;

    public void EnsureDirectories() => Directory.CreateDirectory(ArtDirectory);

    /// <summary>Write failures, so a blocked write shows up instead of vanishing.</summary>
    public IReadOnlyList<string> WriteErrors
    {
        get { lock (_errorLock) { return _writeErrors.ToArray(); } }
    }

    public void ClearWriteErrors()
    {
        lock (_errorLock) { _writeErrors.Clear(); }
    }

    // --- reading ---

    public Archive LoadArchive() => Load<Archive>(ArchivePath) ?? new Archive();

    public NowPlaying LoadNowPlaying() => Load<NowPlaying>(NowPlayingPath) ?? new NowPlaying();

    public Settings LoadSettings() => Load<Settings>(SettingsPath) ?? Settings.Default;

    private T? Load<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllBytes(path);
            if (json.Length == 0) return null;
            return JsonSerializer.Deserialize<T>(json, SharedJson.Pretty);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            Record($"{path} -> {error.Message}");
            return null;
        }
    }

    // --- writing ---

    public bool SaveArchive(Archive archive) => Save(ArchivePath, archive);

    public bool SaveNowPlaying(NowPlaying nowPlaying) => Save(NowPlayingPath, nowPlaying);

    public bool SaveSettings(Settings settings) => Save(SettingsPath, settings);

    private bool Save<T>(string path, T value)
    {
        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(value, SharedJson.Pretty);
            return WriteAtomic(path, json);
        }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        {
            Record($"{path} -> {error.Message}");
            return false;
        }
    }

    /// <summary>
    /// Stores one cover. Refuses anything under
    /// <see cref="MinimumArtBytes"/>, which is how a truncated download or an
    /// error page saved as a JPEG is caught.
    /// </summary>
    public bool SaveArtwork(string albumId, byte[] data)
    {
        if (string.IsNullOrEmpty(albumId)) return false;
        if (data.Length < MinimumArtBytes)
        {
            Record($"{ArtFile(albumId)} -> refused, {data.Length} bytes is below the {MinimumArtBytes} byte minimum");
            return false;
        }
        return WriteAtomic(ArtFile(albumId), data);
    }

    public bool HasArtwork(string albumId)
    {
        if (string.IsNullOrEmpty(albumId)) return false;
        var file = new FileInfo(ArtFile(albumId));
        return file.Exists && file.Length >= MinimumArtBytes;
    }

    /// <summary>
    /// Writes through a temporary file and moves it into place, so a reader
    /// never sees a half-written file. The screen saver reads these while the
    /// tray app is writing them, constantly.
    /// </summary>
    public bool WriteAtomic(string path, byte[] data)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllBytes(temporary, data);
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Record($"{path} -> {error.Message}");
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { }
            return false;
        }
    }

    /// <summary>
    /// Appending to a plain list from several threads is what turned four
    /// concurrent art downloads into a segfault on macOS, with no error of any
    /// kind. Downloads are capped at three at a time and this stays locked.
    /// </summary>
    private void Record(string failure)
    {
        lock (_errorLock)
        {
            _writeErrors.Add(failure);
            if (_writeErrors.Count > 40) _writeErrors.RemoveRange(0, _writeErrors.Count - 40);
        }
    }

    /// <summary>Dumps recorded write failures beside the data, for support.</summary>
    public void FlushWriteErrorLog()
    {
        string text;
        lock (_errorLock)
        {
            if (_writeErrors.Count == 0) return;
            text = string.Join(Environment.NewLine, _writeErrors) + Environment.NewLine;
        }
        try
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, "write-errors.log"), text, Encoding.UTF8);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Nothing sensible to do when even the error log will not write.
        }
    }
}

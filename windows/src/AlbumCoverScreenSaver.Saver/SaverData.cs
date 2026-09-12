using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// The data every window draws from: the archive, the settings, the playback
/// signal and the decoded covers.
/// </summary>
/// <remarks>
/// Shared across all displays, because loading it twice would be waste, while
/// the <em>composition</em> on each display is not shared at all. Each screen
/// gets its own <see cref="Scene"/> with its own tiles and its own random
/// source, which is what "each screen independent" in doc 04 section 4 means.
/// </remarks>
internal sealed class SaverData : IDisposable
{
    private const double ReloadInterval = 3.0;

    /// <summary>
    /// Deliberately far more frequent than the archive check. It is one
    /// timestamp read unless the file actually changed, and a track change has
    /// to show up within a quarter second.
    /// </summary>
    private const double NowPlayingInterval = 0.25;

    private readonly SharedStore _store;

    private double _lastReloadCheck = -100;
    private double _lastNowPlayingCheck = -100;
    private DateTime _lastArchiveStamp = DateTime.MinValue;
    private DateTime _lastNowPlayingStamp = DateTime.MinValue;

    public SaverData(SharedStore store)
    {
        _store = store;
        Images = new ImageStore(store);
        Reload(force: true, phase: 0);
    }

    public ImageStore Images { get; }

    public Settings Settings { get; private set; } = Settings.Default;

    public NowPlaying NowPlaying { get; private set; } = new();

    /// <summary>
    /// Newest first, and filtered to albums whose cover is actually on disk.
    /// An album with no art would draw a hole, so it is dropped entirely.
    /// </summary>
    public IReadOnlyList<AlbumEntry> Albums { get; private set; } = [];

    public IReadOnlyDictionary<string, int> AlbumIndexById { get; private set; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// Bumped whenever <see cref="Albums"/> is replaced. Every scene compares it
    /// against its own copy and rebuilds its layout when it moves, which is how
    /// the specification's "set layoutMode to nil after a reload" is expressed
    /// without a nullable sentinel.
    /// </summary>
    public int Generation { get; private set; }

    /// <summary>The album being played right now, if its cover is on disk.</summary>
    public int? LiveAlbumIndex =>
        NowPlaying.IsLive && AlbumIndexById.TryGetValue(NowPlaying.AlbumId, out var index)
            ? index
            : null;

    /// <summary>
    /// The cover for an album by its position in <see cref="Albums"/>, or null.
    /// Every draw path treats null as "draw nothing" rather than as an error,
    /// which is also what makes an index left stale by a reload harmless.
    /// </summary>
    public SKBitmap? ImageFor(int index)
    {
        if (index < 0 || index >= Albums.Count) return null;
        return Images.Get(Albums[index].Id);
    }

    public void Poll(double phase)
    {
        if (phase - _lastReloadCheck > ReloadInterval) Reload(force: false, phase);
        if (phase - _lastNowPlayingCheck > NowPlayingInterval) RefreshNowPlaying(phase);
    }

    private void Reload(bool force, double phase)
    {
        _lastReloadCheck = phase;

        try
        {
            // Always, regardless of force. This is how a settings change made in
            // the tray app takes effect within three seconds.
            Settings = _store.LoadSettings();
            NowPlaying = _store.LoadNowPlaying();

            var stamp = Stamp(_store.ArchivePath);
            if (!force && stamp <= _lastArchiveStamp) return;
            _lastArchiveStamp = stamp;

            var archive = _store.LoadArchive();

            var albums = archive.ByRecency()
                .Where(album => _store.HasArtwork(album.Id))
                .ToArray();

            var byId = new Dictionary<string, int>(albums.Length, StringComparer.Ordinal);
            for (var i = 0; i < albums.Length; i++) byId[albums[i].Id] = i;

            Albums = albums;
            AlbumIndexById = byId;
            Generation++;

            Log.Write($"archive reloaded: {archive.Count} albums, {albums.Length} with art on disk");
        }
        catch (Exception error)
        {
            Log.Failure("SaverData.Reload", error);
        }
    }

    private void RefreshNowPlaying(double phase)
    {
        _lastNowPlayingCheck = phase;

        try
        {
            var stamp = Stamp(_store.NowPlayingPath);
            if (stamp <= _lastNowPlayingStamp) return;
            _lastNowPlayingStamp = stamp;

            NowPlaying = _store.LoadNowPlaying();
        }
        catch (Exception error)
        {
            Log.Failure("SaverData.RefreshNowPlaying", error);
        }
    }

    private static DateTime Stamp(string path) =>
        File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;

    public void Dispose() => Images.Dispose();
}

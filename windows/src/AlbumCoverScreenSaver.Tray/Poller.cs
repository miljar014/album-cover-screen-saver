using AlbumCoverScreenSaver.Shared;

namespace AlbumCoverScreenSaver.Tray;

/// <summary>
/// Watches what is playing, grows the archive, and keeps nowplaying.json
/// current.
/// </summary>
internal sealed class Poller : IDisposable
{
    private readonly SharedStore _store;
    private readonly ArtworkService _artwork;

    private readonly Archive _archive;
    private NowPlaying? _lastWritten;
    private DateTime _lastWriteAt = DateTime.MinValue;
    private string _lastTrackKey = "";

    private IMusicSource _source;
    private IHistorySource? _history;
    private MusicSourceKind _sourceKind;
    private IDisposable? _owned;

    private DateTime _lastSweepAt = DateTime.MinValue;
    private bool _seeded;

    public Poller(SharedStore store)
    {
        _store = store;
        _artwork = new ArtworkService(store);

        _store.EnsureDirectories();
        _archive = _store.LoadArchive();

        _sourceKind = TrayConfig.Source;
        _source = Build(_sourceKind);

        Log.Write($"folder: {_store.Describe()}");
        Log.Write($"archive holds {_archive.Count} album(s) to start with");
        Log.Write($"music source: {_sourceKind.Title()}");
    }

    /// <summary>
    /// Builds the source the settings ask for.
    /// </summary>
    /// <remarks>
    /// A Last.fm source that is not configured yet would report nothing playing
    /// forever and look like a broken app, so an unconfigured one falls back to
    /// reading this PC. The settings window is where the user finds out why.
    /// </remarks>
    private IMusicSource Build(MusicSourceKind kind)
    {
        _owned?.Dispose();
        _owned = null;
        _history = null;

        if (kind == MusicSourceKind.LastFm && LastFmSource.IsConfigured)
        {
            var lastfm = new LastFmSource();
            _owned = lastfm;
            _history = lastfm;
            return lastfm;
        }

        if (kind == MusicSourceKind.LastFm)
        {
            Log.Write("last.fm is chosen but not set up yet; reading this PC instead");
        }

        return new GsmtcMusicSource();
    }

    /// <summary>
    /// Rebuilds the source when the settings have changed under us.
    /// </summary>
    /// <remarks>
    /// Checked on every poll rather than pushed from the settings window,
    /// because the username can change without the source doing so and a
    /// half-typed one must not leave the app wired to a dead account.
    /// </remarks>
    private void FollowSettings()
    {
        var wanted = TrayConfig.Source;
        var configured = wanted != MusicSourceKind.LastFm || LastFmSource.IsConfigured;
        var haveLastFm = _history is not null;

        if (wanted == _sourceKind && configured == haveLastFm) return;

        _sourceKind = wanted;
        _source = Build(wanted);
        _seeded = false;
        _lastSweepAt = DateTime.MinValue;

        Log.Write($"music source is now {_sourceKind.Title()}");
    }

    /// <summary>The last snapshot published. What the tray menu shows.</summary>
    public NowPlaying Current { get; private set; } = new();

    public int AlbumCount => _archive.Count;

    /// <summary>One line for the tray menu, in plain language.</summary>
    public string Status { get; private set; } = "Starting up";

    /// <summary>
    /// One look. Returns whether music is playing, which decides how soon to
    /// look again.
    /// </summary>
    public async Task<bool> PollOnceAsync(CancellationToken token)
    {
        FollowSettings();
        await SweepHistoryAsync(token);

        MusicReading? reading;
        try
        {
            reading = await _source.ReadAsync(token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            Log.Failure("reading the media session", error);
            Status = "Could not read what Windows is playing";
            return false;
        }

        if (reading is null || !reading.IsUsable)
        {
            // Either nothing is loaded, or it is a single with no album and so
            // has no stable id to file it under. Same rule the Last.fm source
            // applies for the same reason.
            if (reading is not null && !reading.IsUsable)
            {
                Status = $"Playing something with no album: {Describe(reading)}";
            }
            else
            {
                Status = "Nothing playing";
            }

            PublishSilence();
            return false;
        }

        var albumId = AlbumEntry.MakeId(reading.Artist, reading.AlbumTitle);
        var trackKey = albumId + "|" + reading.Track;

        // Once per track that starts, not once per look. Counting every poll
        // would turn a four minute song into eighty plays.
        if (reading.IsPlaying && trackKey != _lastTrackKey)
        {
            _lastTrackKey = trackKey;

            if (ArchiveUpdates.RecordPlay(
                    _archive, albumId, reading.AlbumTitle, reading.Artist, "", DateTime.UtcNow))
            {
                _store.SaveArchive(_archive);
                Log.Write($"heard {reading.Artist} - {reading.AlbumTitle} ({reading.Track}); archive now {_archive.Count}");
            }
        }

        var haveArtwork = await _artwork.EnsureArtworkAsync(
            albumId, reading.Artist, reading.AlbumTitle, reading.ReadArtworkAsync, token);

        if (!haveArtwork)
        {
            // Published only once the cover is on disk, so the screen saver
            // never features an album it has nothing to draw.
            Status = $"Waiting for artwork: {Describe(reading)}";
            return reading.IsPlaying;
        }

        var next = new NowPlaying
        {
            AlbumId = albumId,
            AlbumName = reading.AlbumTitle,
            Artist = reading.Artist,
            Track = reading.Track,
            ImageUrl = "",
            IsPlaying = reading.IsPlaying,
            Updated = DateTime.UtcNow,
            ProgressMs = (int)Math.Clamp(reading.Position.TotalMilliseconds, 0, int.MaxValue),
            DurationMs = (int)Math.Clamp(reading.Duration.TotalMilliseconds, 0, int.MaxValue),
        };

        Current = next;
        Status = reading.IsPlaying ? $"Playing {Describe(reading)}" : $"Paused: {Describe(reading)}";

        if (PollingPlan.ShouldRewrite(_lastWritten, next, _lastWriteAt, DateTime.UtcNow))
        {
            if (_store.SaveNowPlaying(next))
            {
                _lastWritten = next;
                _lastWriteAt = DateTime.UtcNow;
            }
        }

        return reading.IsPlaying;
    }

    /// <summary>
    /// Reads the listening history, on its own much slower clock.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Half hourly, against three seconds for the now-playing poll. A history is
    /// hundreds of plays and changes a few times an hour, so asking for it on
    /// the fast clock would be six hundred times the requests for the same
    /// answer.
    /// </para>
    /// <para>
    /// The seed runs once, first, and only into an archive that would otherwise
    /// leave the screen nearly empty. That ordering matters: seeding after a
    /// sweep would be seeding an archive the sweep had just filled, and nothing
    /// would happen.
    /// </para>
    /// </remarks>
    private async Task SweepHistoryAsync(CancellationToken token)
    {
        if (_history is null) return;

        var now = DateTime.UtcNow;
        var due = now - _lastSweepAt >= PollingPlan.HistorySweep;

        if (_seeded && !due) return;

        var changed = false;

        if (!_seeded)
        {
            _seeded = true;

            if (LastFmSync.NeedsSeeding(_archive))
            {
                Status = "Reading your listening history";
                changed = await _history.SeedAsync(_archive, token) > 0;
            }
        }

        if (due || changed)
        {
            _lastSweepAt = now;
            changed |= (await _history.SweepAsync(_archive, token)).Recorded > 0;
        }

        if (changed) _store.SaveArchive(_archive);
    }

    /// <summary>
    /// Says plainly that nothing is on, once, rather than repeating it forever.
    /// </summary>
    private void PublishSilence()
    {
        var silence = new NowPlaying { IsPlaying = false, Updated = DateTime.UtcNow };

        if (!PollingPlan.ShouldRewrite(_lastWritten, silence, _lastWriteAt, DateTime.UtcNow)) return;

        if (_store.SaveNowPlaying(silence))
        {
            _lastWritten = silence;
            _lastWriteAt = DateTime.UtcNow;
        }

        Current = silence;
        _lastTrackKey = "";
    }

    private static string Describe(MusicReading reading)
    {
        var artist = string.IsNullOrWhiteSpace(reading.Artist) ? "Unknown artist" : reading.Artist;
        var track = string.IsNullOrWhiteSpace(reading.Track) ? "Unknown track" : reading.Track;
        return $"{track} - {artist}";
    }

    public void Dispose()
    {
        _artwork.Dispose();
        _owned?.Dispose();
    }
}

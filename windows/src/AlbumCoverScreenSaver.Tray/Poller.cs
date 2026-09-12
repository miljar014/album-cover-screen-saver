using AlbumCoverScreenSaver.Shared;

namespace AlbumCoverScreenSaver.Tray;

/// <summary>
/// Watches what is playing, grows the archive, and keeps nowplaying.json
/// current.
/// </summary>
internal sealed class Poller : IDisposable
{
    private readonly SharedStore _store;
    private readonly IMusicSource _source;
    private readonly ArtworkService _artwork;

    private readonly Archive _archive;
    private NowPlaying? _lastWritten;
    private DateTime _lastWriteAt = DateTime.MinValue;
    private string _lastTrackKey = "";

    public Poller(SharedStore store, IMusicSource source)
    {
        _store = store;
        _source = source;
        _artwork = new ArtworkService(store);

        _store.EnsureDirectories();
        _archive = _store.LoadArchive();

        Log.Write($"folder: {_store.Describe()}");
        Log.Write($"archive holds {_archive.Count} album(s) to start with");
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

    public void Dispose() => _artwork.Dispose();
}

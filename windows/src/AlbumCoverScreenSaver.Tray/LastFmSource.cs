using AlbumCoverScreenSaver.Shared;

namespace AlbumCoverScreenSaver.Tray;

/// <summary>A source that can hand over a whole listening history, not just now.</summary>
internal interface IHistorySource
{
    /// <summary>Reads plays since the archive's cursor and records them.</summary>
    Task<SweepResult> SweepAsync(Archive archive, CancellationToken token);

    /// <summary>Fills an archive that would otherwise open nearly empty.</summary>
    Task<int> SeedAsync(Archive archive, CancellationToken token);
}

/// <summary>
/// Last.fm: one API key, one username, and a whole history.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes a fresh install open as a wall of the listener's own music
/// rather than four covers and a lot of black. It also works whatever they play
/// through, because Last.fm is scrobbled to by Spotify, Apple Music, YouTube
/// Music and the rest alike.
/// </para>
/// <para>
/// Every rule about what to keep and what to throw away lives in
/// <see cref="LastFm"/> and <see cref="LastFmSync"/> in the shared library,
/// where it is tested against saved replies. What is left here is the network,
/// which is the part that cannot be tested off Windows and therefore should
/// contain as little thinking as possible.
/// </para>
/// </remarks>
internal sealed class LastFmSource : IMusicSource, IHistorySource, IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public LastFmSource()
    {
        // Last.fm asks that applications identify themselves, and an unnamed
        // client is the first thing a service throttles.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AlbumCoverScreenSaver/1.0 (+Windows)");
    }

    private static string Key => TrayConfig.LastFmKey;

    private static string User => TrayConfig.LastFmUser;

    public static bool IsConfigured =>
        TrayConfig.HasLastFmKey && MusicSourceKinds.LooksLikeUsername(User);

    /// <summary>The last thing that went wrong, in the service's own words.</summary>
    public static string? LastError { get; private set; }

    public async Task<MusicReading?> ReadAsync(CancellationToken token)
    {
        if (!IsConfigured) return null;

        var json = await GetAsync(LastFm.NowPlaying(User, Key), token);
        if (json is null) return null;

        var live = LastFm.ParseNowPlaying(json);
        if (live is not { } playing) return null;

        // Last.fm reports no position at all, so both of these stay at zero and
        // the styles fall back to their own timers. That is expected, and the
        // Cassette Deck's reels and the Record Player's arm are written to cope
        // with it.
        return new MusicReading(
            Track: playing.Track,
            Artist: playing.Artist,
            AlbumTitle: playing.Album,
            IsPlaying: true,
            Position: TimeSpan.Zero,
            Duration: TimeSpan.Zero,
            ReadArtworkAsync: Artwork(playing.ImageUrl));
    }

    public async Task<SweepResult> SweepAsync(Archive archive, CancellationToken token)
    {
        if (!IsConfigured) return new SweepResult(0, 0, null);

        var since = LastFmSync.Cursor(archive);
        var json = await GetAsync(LastFm.RecentTracks(User, Key, LastFm.HistoryLimit, since), token);

        if (json is null) return new SweepResult(0, 0, null);

        var plays = LastFm.ParseRecent(json);
        var result = LastFmSync.Record(archive, plays);

        Log.Write(
            $"last.fm sweep: {plays.Count} play(s) read, {result.Recorded} recorded, "
            + $"{result.Skipped} already known; archive now {archive.Count}");

        return result;
    }

    public async Task<int> SeedAsync(Archive archive, CancellationToken token)
    {
        if (!IsConfigured) return 0;

        var json = await GetAsync(LastFm.TopAlbums(User, Key, LastFm.SeedLimit), token);
        if (json is null) return 0;

        var albums = LastFm.ParseTopAlbums(json);
        var seeded = LastFmSync.SeedFrom(archive, albums, LastFm.SeedLimit);

        Log.Write($"last.fm seed: {albums.Count} top album(s) read, {seeded} added");

        return seeded;
    }

    /// <summary>
    /// Checks a username without changing anything.
    /// </summary>
    /// <returns>Null when the account answers, otherwise what the service said.</returns>
    /// <remarks>
    /// The settings window uses this, which is the whole reason
    /// <see cref="LastFm.ErrorMessage"/> exists: a mistyped username comes back
    /// as a perfectly good reply with a complaint inside it, so a check that
    /// only looked at the status code would report success.
    /// </remarks>
    public async Task<string?> TestAsync(string user, CancellationToken token)
    {
        if (!TrayConfig.HasLastFmKey) return "This build has no Last.fm key in it.";

        var cleaned = MusicSourceKinds.CleanUsername(user);
        if (!MusicSourceKinds.LooksLikeUsername(cleaned)) return "That does not look like a username.";

        string? body;
        try
        {
            body = await _http.GetStringAsync(LastFm.NowPlaying(cleaned, Key), token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            return error.Message;
        }

        return LastFm.ErrorMessage(body);
    }

    /// <summary>
    /// Downloads the cover Last.fm names, if it named one.
    /// </summary>
    /// <remarks>
    /// Null when there is no usable URL, which sends the artwork service
    /// straight to iTunes. That path has better coverage than Last.fm's own
    /// images anyway, so this is a shortcut rather than the main road.
    /// </remarks>
    private Func<CancellationToken, Task<byte[]?>>? Artwork(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || ArtworkUrls.IsPlaceholder(url)) return null;

        return async token =>
        {
            try
            {
                return await _http.GetByteArrayAsync(url, token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Log.Failure("downloading a cover from Last.fm", error);
                return null;
            }
        };
    }

    /// <summary>
    /// One request.
    /// </summary>
    /// <remarks>
    /// A refusal from Last.fm is a well formed reply with a message in it rather
    /// than an HTTP failure, so both are checked. The message is kept for the
    /// settings window and the tray menu; the caller gets null either way and
    /// does nothing, because a failed sweep must leave the archive exactly as it
    /// was rather than half updated.
    /// </remarks>
    private async Task<string?> GetAsync(string url, CancellationToken token)
    {
        try
        {
            var body = await _http.GetStringAsync(url, token);

            if (LastFm.ErrorMessage(body) is { } complaint)
            {
                LastError = complaint;
                Log.Write($"last.fm refused the request: {complaint}");
                return null;
            }

            LastError = null;
            return body;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            LastError = error.Message;
            Log.Failure("asking Last.fm", error);
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}

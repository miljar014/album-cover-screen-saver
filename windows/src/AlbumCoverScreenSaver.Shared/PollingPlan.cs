namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// How often the tray app looks, and when it bothers writing.
/// </summary>
/// <remarks>
/// The numbers are from doc 00 section 4e. They matter more than they look: too
/// slow and a track change takes visible seconds to reach the screen, too fast
/// and a background app is burning battery asking the same question.
/// </remarks>
public static class PollingPlan
{
    /// <summary>Checked this often while something is playing.</summary>
    public static readonly TimeSpan WhilePlaying = TimeSpan.FromSeconds(3);

    /// <summary>And this often when nothing is.</summary>
    public static readonly TimeSpan WhileIdle = TimeSpan.FromSeconds(20);

    /// <summary>How often a listening history is swept. Last.fm only, step 8.</summary>
    public static readonly TimeSpan HistorySweep = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Even with nothing changing, the file is rewritten this often, to keep the
    /// position snapshot honest. The saver extrapolates the play position from
    /// it, and the further it extrapolates the more it drifts.
    /// </summary>
    public static readonly TimeSpan RewriteFloor = TimeSpan.FromSeconds(10);

    public static TimeSpan NowPlayingInterval(bool isPlaying) =>
        isPlaying ? WhilePlaying : WhileIdle;

    /// <summary>
    /// Whether nowplaying.json is worth writing again.
    /// </summary>
    /// <remarks>
    /// Writing on every poll would mean a disk write every three seconds
    /// forever, and the screen saver watches this file's timestamp to decide
    /// whether to re-read it, so a pointless write costs a pointless parse on
    /// the other side too.
    ///
    /// Note it compares the album, the track and the play state, but not the
    /// position: the position changes constantly by design and is what the
    /// ten second floor is for.
    /// </remarks>
    public static bool ShouldRewrite(
        NowPlaying? previous, NowPlaying next, DateTime lastWrite, DateTime now)
    {
        if (previous is null) return true;

        if (!string.Equals(previous.AlbumId, next.AlbumId, StringComparison.Ordinal)) return true;
        if (!string.Equals(previous.Track, next.Track, StringComparison.Ordinal)) return true;
        if (previous.IsPlaying != next.IsPlaying) return true;

        // Nothing moves while nothing is playing, so there is nothing to keep
        // honest. The snapshot going stale after ninety seconds already handles
        // a machine that was left paused, and rewriting every ten seconds all
        // day for no reason is a write the disk did not need.
        if (!next.IsPlaying) return false;

        return SharedJson.AsUtc(now) - SharedJson.AsUtc(lastWrite) >= RewriteFloor;
    }
}

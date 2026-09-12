namespace AlbumCoverScreenSaver.Tray;

/// <summary>One look at what a music player says it is doing.</summary>
/// <param name="Track">The track title.</param>
/// <param name="Artist">
/// The <em>track</em> artist, not the album artist. Last.fm reports the track
/// artist, and the album id is derived from artist and album title, so using
/// the album artist here would give the same record two different ids
/// depending on which source saw it. That failure is silent: the album simply
/// appears twice.
/// </param>
/// <param name="AlbumTitle">The album title. Empty for a single.</param>
/// <param name="IsPlaying">False when paused, stopped, or nothing is loaded.</param>
/// <param name="Position">How far into the track, at the moment of the reading.</param>
/// <param name="Duration">Zero when the player does not report one.</param>
/// <param name="ReadArtworkAsync">
/// Fetches the cover the player is already showing, if it offers one. Null when
/// it does not.
/// </param>
internal sealed record MusicReading(
    string Track,
    string Artist,
    string AlbumTitle,
    bool IsPlaying,
    TimeSpan Position,
    TimeSpan Duration,
    Func<CancellationToken, Task<byte[]?>>? ReadArtworkAsync)
{
    /// <summary>
    /// Whether this can be filed at all. Without both an artist and an album
    /// there is no stable id to file it under, which is the same reason the
    /// Last.fm source skips singles.
    /// </summary>
    public bool IsUsable =>
        !string.IsNullOrWhiteSpace(Artist) && !string.IsNullOrWhiteSpace(AlbumTitle);
}

internal interface IMusicSource
{
    /// <summary>What is playing now, or null if nothing is.</summary>
    Task<MusicReading?> ReadAsync(CancellationToken token);
}

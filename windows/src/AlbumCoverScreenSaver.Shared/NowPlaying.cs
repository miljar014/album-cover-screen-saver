using System.Text.Json.Serialization;

namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// What is playing right now, written by the tray app so the screen saver can
/// feature it without ever making a network request of its own.
/// </summary>
public sealed class NowPlaying
{
    /// <summary>How stale a snapshot may be before it stops counting as live.</summary>
    public static readonly TimeSpan LiveWindow = TimeSpan.FromSeconds(90);

    [JsonPropertyName("albumID")]
    public string AlbumId { get; set; } = "";

    [JsonPropertyName("albumName")]
    public string AlbumName { get; set; } = "";

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = "";

    [JsonPropertyName("track")]
    public string Track { get; set; } = "";

    [JsonPropertyName("imageURL")]
    public string ImageUrl { get; set; } = "";

    [JsonPropertyName("isPlaying")]
    public bool IsPlaying { get; set; }

    /// <summary>When this snapshot was taken.</summary>
    [JsonPropertyName("updated")]
    public DateTime Updated { get; set; } = SharedJson.DistantPast;

    [JsonPropertyName("progressMs")]
    public int ProgressMs { get; set; }

    /// <summary>
    /// Zero when the source does not report one. Last.fm never does, and the
    /// styles fall back to their own timers when it is zero. That is expected.
    /// </summary>
    [JsonPropertyName("durationMs")]
    public int DurationMs { get; set; }

    /// <summary>How far through the track we are, 0 to 1.</summary>
    [JsonIgnore]
    public double ProgressFraction => ProgressFractionAt(DateTime.UtcNow);

    /// <summary>
    /// How far through the track we are at a given instant.
    /// </summary>
    /// <remarks>
    /// The file is only rewritten every few seconds, so the time since the
    /// snapshot is added back in. Without that the tonearm and the tape reels
    /// sit still and then jump. The elapsed time counts only while playing: a
    /// paused track does not creep forward.
    ///
    /// Taking the instant as an argument is what makes this testable. Nothing
    /// else should call it.
    /// </remarks>
    public double ProgressFractionAt(DateTime now)
    {
        if (DurationMs <= 0) return 0;

        var elapsedMs = IsPlaying
            ? Math.Max(0, (SharedJson.AsUtc(now) - SharedJson.AsUtc(Updated)).TotalMilliseconds)
            : 0;

        return Math.Min(1, Math.Max(0, (ProgressMs + elapsedMs) / DurationMs));
    }

    /// <summary>
    /// Whether this snapshot still describes something actually playing.
    /// A machine that went to sleep must not keep claiming a track from an
    /// hour ago is on.
    /// </summary>
    [JsonIgnore]
    public bool IsLive => IsLiveAt(DateTime.UtcNow);

    public bool IsLiveAt(DateTime now) =>
        IsPlaying
        && !string.IsNullOrEmpty(AlbumId)
        && (SharedJson.AsUtc(now) - SharedJson.AsUtc(Updated)) < LiveWindow;
}

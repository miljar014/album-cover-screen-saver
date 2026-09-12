using Windows.Media.Control;
using Windows.Storage.Streams;

namespace AlbumCoverScreenSaver.Tray;

/// <summary>
/// Reads what is playing from Windows itself.
/// </summary>
/// <remarks>
/// <para>
/// This is the public API behind the media flyout, the little panel that
/// appears when you press a volume key. It needs no permission prompt, no
/// package, and it sees every app that publishes transport controls: Spotify,
/// Apple Music, browsers, foobar, everything.
/// </para>
/// <para>
/// It is a large improvement on the macOS build, which asks Spotify and Music
/// by AppleScript. That needs a permission prompt, sees only those two desktop
/// apps, and never gets artwork without a network lookup. Here the player hands
/// the cover over directly.
/// </para>
/// <para>
/// The one wrinkle: the reported position only refreshes every few seconds.
/// That is the same problem the Mac has, and it already has the same solution
/// in <c>NowPlaying.ProgressFraction</c>, which extrapolates from the snapshot.
/// Do not invent a second mechanism here.
/// </para>
/// </remarks>
internal sealed class GsmtcMusicSource : IMusicSource
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    public async Task<MusicReading?> ReadAsync(CancellationToken token)
    {
        _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

        var session = _manager.GetCurrentSession();
        if (session is null) return null;

        var media = await session.TryGetMediaPropertiesAsync();
        if (media is null) return null;

        var playback = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();

        // Players report a track as a window between StartTime and EndTime
        // rather than as a plain length, and a few report nonsense for one or
        // both, so both are floored at zero rather than trusted.
        var duration = timeline.EndTime - timeline.StartTime;
        var position = timeline.Position - timeline.StartTime;

        var thumbnail = media.Thumbnail;

        return new MusicReading(
            Track: media.Title ?? "",
            Artist: media.Artist ?? "",
            AlbumTitle: media.AlbumTitle ?? "",
            IsPlaying: playback.PlaybackStatus ==
                       GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            Position: position < TimeSpan.Zero ? TimeSpan.Zero : position,
            Duration: duration < TimeSpan.Zero ? TimeSpan.Zero : duration,
            ReadArtworkAsync: thumbnail is null
                ? null
                : cancellation => ReadArtworkAsync(thumbnail, cancellation));
    }

    private static async Task<byte[]?> ReadArtworkAsync(
        IRandomAccessStreamReference reference, CancellationToken token)
    {
        try
        {
            using var stream = await reference.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > int.MaxValue) return null;

            var bytes = new byte[stream.Size];
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch (Exception error)
        {
            // A player can offer a thumbnail and then fail to produce it,
            // usually while it is still loading. The iTunes lookup covers it.
            Log.Write($"the player's own artwork could not be read: {error.Message}");
            return null;
        }
    }
}

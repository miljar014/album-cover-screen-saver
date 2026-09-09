using System.Text.Json;

namespace AlbumCoverScreenSaver.Shared.Tests;

public static class NowPlayingTests
{
    private static readonly DateTime Snapshot = new(2026, 9, 4, 22, 31, 2, DateTimeKind.Utc);

    public static void Register(TestRunner runner)
    {
        runner.Group("Now playing");

        runner.Add("is zero when the source reports no duration", () =>
        {
            // Last.fm never reports a position, so this is the normal case for
            // that source, not an error. The styles fall back to their timers.
            var playing = new NowPlaying { IsPlaying = true, Updated = Snapshot, ProgressMs = 84000, DurationMs = 0 };
            Check.Equal(0d, playing.ProgressFractionAt(Snapshot), "fraction");
        });

        runner.Add("uses the raw position at the moment of the snapshot", () =>
        {
            var playing = new NowPlaying { IsPlaying = true, Updated = Snapshot, ProgressMs = 84000, DurationMs = 213000 };
            Check.Close(84000d / 213000d, playing.ProgressFractionAt(Snapshot), 1e-9, "fraction");
        });

        runner.Add("adds the time since the snapshot while playing", () =>
        {
            // Without this the tonearm and the tape reels sit still and then
            // jump every few seconds when the file is rewritten.
            var playing = new NowPlaying { IsPlaying = true, Updated = Snapshot, ProgressMs = 84000, DurationMs = 213000 };
            var later = Snapshot.AddSeconds(10);
            Check.Close(94000d / 213000d, playing.ProgressFractionAt(later), 1e-9, "fraction after 10 seconds");
        });

        runner.Add("does not creep forward while paused", () =>
        {
            var paused = new NowPlaying { IsPlaying = false, Updated = Snapshot, ProgressMs = 84000, DurationMs = 213000 };
            var later = Snapshot.AddMinutes(5);
            Check.Close(84000d / 213000d, paused.ProgressFractionAt(later), 1e-9, "fraction is unchanged");
        });

        runner.Add("clamps to one when the extrapolation overruns the track", () =>
        {
            var playing = new NowPlaying { IsPlaying = true, Updated = Snapshot, ProgressMs = 200000, DurationMs = 213000 };
            Check.Equal(1d, playing.ProgressFractionAt(Snapshot.AddHours(1)), "fraction");
        });

        runner.Add("ignores a snapshot stamped in the future", () =>
        {
            var playing = new NowPlaying { IsPlaying = true, Updated = Snapshot, ProgressMs = 84000, DurationMs = 213000 };
            var earlier = Snapshot.AddSeconds(-30);
            Check.Close(84000d / 213000d, playing.ProgressFractionAt(earlier), 1e-9, "fraction is not pushed backwards");
        });

        runner.Add("is live for a fresh, playing snapshot", () =>
        {
            var playing = new NowPlaying { IsPlaying = true, AlbumId = "lfm-a-b", Updated = Snapshot };
            Check.True(playing.IsLiveAt(Snapshot.AddSeconds(10)), "live after 10 seconds");
            Check.True(playing.IsLiveAt(Snapshot.AddSeconds(89)), "live at 89 seconds");
        });

        runner.Add("goes stale after ninety seconds", () =>
        {
            // A machine that went to sleep must not keep claiming a track from
            // an hour ago is on.
            var playing = new NowPlaying { IsPlaying = true, AlbumId = "lfm-a-b", Updated = Snapshot };
            Check.False(playing.IsLiveAt(Snapshot.AddSeconds(90)), "not live at exactly 90 seconds");
            Check.False(playing.IsLiveAt(Snapshot.AddHours(1)), "not live an hour later");
        });

        runner.Add("is not live when paused or when there is no album", () =>
        {
            var paused = new NowPlaying { IsPlaying = false, AlbumId = "lfm-a-b", Updated = Snapshot };
            Check.False(paused.IsLiveAt(Snapshot), "paused");

            var anonymous = new NowPlaying { IsPlaying = true, AlbumId = "", Updated = Snapshot };
            Check.False(anonymous.IsLiveAt(Snapshot), "no album id");

            Check.False(new NowPlaying().IsLiveAt(Snapshot), "a default snapshot");
        });

        runner.Add("uses the wire names the Mac build uses", () =>
        {
            var playing = new NowPlaying
            {
                AlbumId = "lfm-brothers-osborne-skeletons",
                AlbumName = "Skeletons",
                Artist = "Brothers Osborne",
                Track = "Skeletons",
                ImageUrl = "https://example.test/art.jpg",
                IsPlaying = true,
                Updated = Snapshot,
                ProgressMs = 84000,
                DurationMs = 213000,
            };
            var json = JsonSerializer.Serialize(playing, SharedJson.Pretty);

            foreach (var key in new[] { "\"albumID\"", "\"albumName\"", "\"artist\"", "\"track\"", "\"imageURL\"", "\"isPlaying\"", "\"updated\"", "\"progressMs\"", "\"durationMs\"" })
            {
                Check.Contains(key, json, "wire key present");
            }
            Check.DoesNotContain("progressFraction", json, "derived values are not written");
            Check.DoesNotContain("isLive", json, "derived values are not written");

            var back = JsonSerializer.Deserialize<NowPlaying>(json, SharedJson.Pretty)!;
            Check.Equal(playing.AlbumId, back.AlbumId, "album id");
            Check.Equal(playing.DurationMs, back.DurationMs, "duration");
            Check.Equal(playing.Updated, back.Updated, "updated");
        });
    }
}

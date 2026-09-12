namespace AlbumCoverScreenSaver.Shared.Tests;

/// <summary>
/// The decisions the tray app makes, tested away from the tray app. None of
/// these need Windows, and all of them are the sort of thing that is very hard
/// to check by watching an icon in the corner of a screen.
/// </summary>
public static class TrayLogicTests
{
    private static readonly DateTime Noon = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    public static void Register(TestRunner runner)
    {
        runner.Group("Archive growth");

        runner.Add("a new album enters with one play", () =>
        {
            var archive = new Archive();
            var changed = ArchiveUpdates.RecordPlay(
                archive, "lfm-a-b", "B", "A", "https://art", Noon);

            Check.True(changed, "reported a change");
            Check.Equal(1, archive.Count, "album count");

            var entry = archive.Albums["lfm-a-b"];
            Check.Equal(1, entry.PlayCount, "play count");
            Check.Equal(Noon, entry.FirstSeen, "first seen");
            Check.Equal(Noon, entry.LastPlayed, "last played");
            Check.Equal(Noon, archive.Updated, "archive updated stamp");
        });

        runner.Add("playing it again counts once more and moves lastPlayed", () =>
        {
            var archive = new Archive();
            ArchiveUpdates.RecordPlay(archive, "lfm-a-b", "B", "A", "https://art", Noon);
            ArchiveUpdates.RecordPlay(archive, "lfm-a-b", "B", "A", "https://art", Noon.AddHours(3));

            var entry = archive.Albums["lfm-a-b"];
            Check.Equal(2, entry.PlayCount, "play count");
            Check.Equal(Noon, entry.FirstSeen, "first seen did not move");
            Check.Equal(Noon.AddHours(3), entry.LastPlayed, "last played moved forward");
        });

        runner.Add("an out of order observation cannot rewind lastPlayed", () =>
        {
            // A history sweep hands over plays in whatever order it likes.
            var archive = new Archive();
            ArchiveUpdates.RecordPlay(archive, "lfm-a-b", "B", "A", "", Noon);
            ArchiveUpdates.RecordPlay(archive, "lfm-a-b", "B", "A", "", Noon.AddDays(-30));

            var entry = archive.Albums["lfm-a-b"];
            Check.Equal(Noon, entry.LastPlayed, "last played stayed at the newest");
            Check.Equal(Noon.AddDays(-30), entry.FirstSeen, "first seen moved earlier");
            Check.Equal(2, entry.PlayCount, "both plays counted");
        });

        runner.Add("a blank value never overwrites one already known", () =>
        {
            var archive = new Archive();
            ArchiveUpdates.RecordPlay(archive, "lfm-a-b", "B", "A", "https://art", Noon);
            ArchiveUpdates.RecordPlay(archive, "lfm-a-b", "", "", "", Noon.AddMinutes(5));

            var entry = archive.Albums["lfm-a-b"];
            Check.Equal("B", entry.Name, "name kept");
            Check.Equal("A", entry.Artist, "artist kept");
            Check.Equal("https://art", entry.ImageUrl, "image url kept");
        });

        runner.Add("a value that was missing gets filled in later", () =>
        {
            var archive = new Archive();
            ArchiveUpdates.RecordPlay(archive, "lfm-a-b", "B", "A", "", Noon);
            ArchiveUpdates.RecordPlay(archive, "lfm-a-b", "B", "A", "https://art", Noon.AddMinutes(5));

            Check.Equal("https://art", archive.Albums["lfm-a-b"].ImageUrl, "image url filled in");
        });

        runner.Add("an empty id is refused rather than filed under nothing", () =>
        {
            var archive = new Archive();
            Check.False(ArchiveUpdates.RecordPlay(archive, "", "B", "A", "", Noon), "refused");
            Check.Equal(0, archive.Count, "nothing was added");
        });

        runner.Add("seeded albums sort behind every real play", () =>
        {
            var archive = new Archive();
            ArchiveUpdates.Seed(archive, "lfm-seed", "Old Favourite", "A", "", 40);
            ArchiveUpdates.RecordPlay(archive, "lfm-real", "Just Played", "A", "", Noon);

            Check.Sequence(
                new[] { "lfm-real", "lfm-seed" },
                archive.ByRecency().Select(album => album.Id),
                "the real play comes first");

            Check.Equal(ArchiveUpdates.SeedStamp, archive.Albums["lfm-seed"].LastPlayed, "seed stamp");
            Check.True(ArchiveUpdates.SeedStamp > SharedJson.DistantPast, "and it is ahead of never");
        });

        runner.Add("a seed never overwrites something already played", () =>
        {
            var archive = new Archive();
            ArchiveUpdates.RecordPlay(archive, "lfm-a-b", "B", "A", "", Noon);

            Check.False(ArchiveUpdates.Seed(archive, "lfm-a-b", "B", "A", "", 99), "refused");
            Check.Equal(Noon, archive.Albums["lfm-a-b"].LastPlayed, "the real play survived");
            Check.Equal(1, archive.Albums["lfm-a-b"].PlayCount, "and its count was not replaced");
        });

        runner.Group("Polling cadence");

        runner.Add("looks often while playing and rarely when not", () =>
        {
            Check.Equal(TimeSpan.FromSeconds(3), PollingPlan.NowPlayingInterval(true), "playing");
            Check.Equal(TimeSpan.FromSeconds(20), PollingPlan.NowPlayingInterval(false), "idle");
            Check.Equal(TimeSpan.FromMinutes(30), PollingPlan.HistorySweep, "history sweep");
        });

        runner.Add("always writes the first time", () =>
            Check.True(
                PollingPlan.ShouldRewrite(null, new NowPlaying(), Noon, Noon),
                "no previous snapshot"));

        runner.Add("writes when the album, the track or the play state changes", () =>
        {
            var previous = new NowPlaying { AlbumId = "a", Track = "One", IsPlaying = true };

            Check.True(PollingPlan.ShouldRewrite(previous,
                new NowPlaying { AlbumId = "b", Track = "One", IsPlaying = true }, Noon, Noon),
                "album changed");

            Check.True(PollingPlan.ShouldRewrite(previous,
                new NowPlaying { AlbumId = "a", Track = "Two", IsPlaying = true }, Noon, Noon),
                "track changed, same album");

            Check.True(PollingPlan.ShouldRewrite(previous,
                new NowPlaying { AlbumId = "a", Track = "One", IsPlaying = false }, Noon, Noon),
                "paused");
        });

        runner.Add("does not write again just because the position moved", () =>
        {
            // Otherwise this is a disk write every three seconds forever, and a
            // pointless re-parse in the screen saver each time.
            var previous = new NowPlaying { AlbumId = "a", Track = "One", IsPlaying = true, ProgressMs = 1000 };
            var next = new NowPlaying { AlbumId = "a", Track = "One", IsPlaying = true, ProgressMs = 4000 };

            Check.False(PollingPlan.ShouldRewrite(previous, next, Noon, Noon.AddSeconds(3)), "3 seconds in");
            Check.False(PollingPlan.ShouldRewrite(previous, next, Noon, Noon.AddSeconds(9)), "9 seconds in");
        });

        runner.Add("but does write every ten seconds regardless", () =>
        {
            // The saver extrapolates the play position from this file, and the
            // further it extrapolates the more it drifts.
            var previous = new NowPlaying { AlbumId = "a", Track = "One", IsPlaying = true };
            var next = new NowPlaying { AlbumId = "a", Track = "One", IsPlaying = true };

            Check.True(PollingPlan.ShouldRewrite(previous, next, Noon, Noon.AddSeconds(10)), "at 10 seconds");
            Check.True(PollingPlan.ShouldRewrite(previous, next, Noon, Noon.AddMinutes(1)), "and later");
        });

        runner.Add("stops rewriting once nothing is playing", () =>
        {
            // The ten second floor exists to keep a moving position honest.
            // Nothing moves while nothing plays, and the snapshot going stale
            // after ninety seconds already covers a machine left paused.
            var idle = new NowPlaying { AlbumId = "", Track = "", IsPlaying = false };

            Check.False(PollingPlan.ShouldRewrite(idle, idle, Noon, Noon.AddMinutes(5)),
                "no write five minutes into silence");
            Check.False(PollingPlan.ShouldRewrite(idle, idle, Noon, Noon.AddHours(8)),
                "and none eight hours in");

            // But the moment it stops, that is a change and it is written once.
            var playing = new NowPlaying { AlbumId = "a", Track = "One", IsPlaying = true };
            Check.True(PollingPlan.ShouldRewrite(playing, idle, Noon, Noon.AddSeconds(1)),
                "the stop itself is written");
        });

        runner.Group("Artwork of last resort");

        runner.Add("builds the iTunes lookup with the terms joined by a plus", () =>
        {
            var url = ArtworkUrls.ItunesSearch("Brothers Osborne", "Skeletons");
            Check.Equal(
                "https://itunes.apple.com/search?term=Brothers%20Osborne+Skeletons&entity=album&limit=1",
                url,
                "search url");

            Check.Contains("entity=album", url, "restricted to albums");
            Check.Contains("limit=1", url, "one result");
        });

        runner.Add("escapes each term separately, so the joiner survives", () =>
        {
            // Escaping the joined string would turn the "+" into %2B and the
            // search would look for one long nonsense term.
            var url = ArtworkUrls.ItunesSearch("AC/DC", "Back in Black");
            Check.Contains("AC%2FDC+Back%20in%20Black", url, "terms");
            Check.DoesNotContain("%2BBack", url, "the joiner was not escaped");
        });

        runner.Add("copes with a missing artist or album", () =>
        {
            Check.Contains("term=Skeletons", ArtworkUrls.ItunesSearch("", "Skeletons"), "album only");
            Check.Contains("term=Osborne", ArtworkUrls.ItunesSearch("Osborne", ""), "artist only");
            Check.Contains("term=&", ArtworkUrls.ItunesSearch("", ""), "neither");
        });

        runner.Add("rewrites the 100 pixel thumbnail to the 600 pixel version", () =>
        {
            Check.Equal(
                "https://is1-ssl.mzstatic.com/image/thumb/abc/600x600bb.jpg",
                ArtworkUrls.Upscale("https://is1-ssl.mzstatic.com/image/thumb/abc/100x100bb.jpg"),
                "upscaled");

            Check.Equal(null, ArtworkUrls.Upscale(null), "null in, null out");
            Check.Equal(null, ArtworkUrls.Upscale("  "), "blank in, null out");
        });

        runner.Add("recognises the Last.fm grey star placeholder", () =>
        {
            // Treating it as real art fills the collage with identical grey
            // squares, which reads as a bug rather than as missing data.
            Check.True(
                ArtworkUrls.IsPlaceholder(
                    "https://lastfm.freetls.fastly.net/i/u/300x300/2a96cbd8b46e442fc41c2b86b821562f.png"),
                "the placeholder");

            Check.False(ArtworkUrls.IsPlaceholder("https://lastfm.example/i/u/300x300/abc123.png"), "real art");
            Check.False(ArtworkUrls.IsPlaceholder(null), "null");
            Check.False(ArtworkUrls.IsPlaceholder(""), "empty");
        });
    }
}

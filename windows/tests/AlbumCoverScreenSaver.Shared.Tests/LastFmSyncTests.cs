namespace AlbumCoverScreenSaver.Shared.Tests;

/// <summary>
/// Folding a Last.fm history into the archive.
/// </summary>
/// <remarks>
/// The archive is the only thing in this product that cannot be regenerated, so
/// these are the tests that matter most in step 8. Everything here is about not
/// corrupting it.
/// </remarks>
public static class LastFmSyncTests
{
    private static readonly DateTime Noon = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);

    private static Scrobble Play(string artist, string album, double hoursAfterNoon) =>
        new(artist, album, album, "https://art.example/x.png", Noon.AddHours(hoursAfterNoon));

    public static void Register(TestRunner runner)
    {
        Sweeping(runner);
        Cursors(runner);
        Seeding(runner);
    }

    private static void Sweeping(TestRunner runner)
    {
        runner.Group("Last.fm: folding a history in");

        runner.Add("plays become albums", () =>
        {
            var archive = new Archive();

            var result = LastFmSync.Record(archive, new[]
            {
                Play("Kyle Hume", "Happy", 0),
                Play("Nic D", "Serotonin", 1),
                Play("Kyle Hume", "Happy", 2),
            });

            Check.Equal(3, result.Recorded, "three plays");
            Check.Equal(2, archive.Albums.Count, "of two albums");

            var happy = archive.Albums[AlbumEntry.MakeId("Kyle Hume", "Happy")];
            Check.Equal(2, happy.PlayCount, "counted twice");
        });

        runner.Add("the stamps land the right way round", () =>
        {
            // Last.fm sends newest first, so this is exactly the order a real
            // sweep arrives in. First seen must end up at the oldest play and
            // last played at the newest, whichever way they came.
            var archive = new Archive();

            LastFmSync.Record(archive, new[]
            {
                Play("Kyle Hume", "Happy", 5),
                Play("Kyle Hume", "Happy", 3),
                Play("Kyle Hume", "Happy", 1),
            });

            var happy = archive.Albums[AlbumEntry.MakeId("Kyle Hume", "Happy")];

            Check.Equal(Noon.AddHours(1), happy.FirstSeen, "first seen is the oldest");
            Check.Equal(Noon.AddHours(5), happy.LastPlayed, "last played is the newest");
        });

        runner.Add("a second sweep does not re-record the first", () =>
        {
            // The whole risk of this step. Without the cursor every sweep
            // re-reads the same two hundred plays and every play count climbs by
            // one every half hour, for as long as the app is installed.
            var archive = new Archive();

            var plays = new[]
            {
                Play("Kyle Hume", "Happy", 0),
                Play("Nic D", "Serotonin", 1),
            };

            LastFmSync.Record(archive, plays);
            var second = LastFmSync.Record(archive, plays);

            Check.Equal(0, second.Recorded, "nothing new");
            Check.Equal(2, second.Skipped, "both already known");

            var happy = archive.Albums[AlbumEntry.MakeId("Kyle Hume", "Happy")];
            Check.Equal(1, happy.PlayCount, "and the count did not climb");
        });

        runner.Add("the play on the boundary is not counted twice", () =>
        {
            // The service's from parameter is inclusive of the second, so the
            // newest play of one sweep comes back as the oldest of the next.
            var archive = new Archive();

            LastFmSync.Record(archive, new[] { Play("Kyle Hume", "Happy", 0) });

            var again = LastFmSync.Record(archive, new[]
            {
                Play("Kyle Hume", "Happy", 0),
                Play("Nic D", "Serotonin", 1),
            });

            Check.Equal(1, again.Recorded, "only the genuinely new one");
            Check.Equal(1, archive.Albums[AlbumEntry.MakeId("Kyle Hume", "Happy")].PlayCount, "still one");
        });

        runner.Add("a sweep with nothing in it changes nothing", () =>
        {
            var archive = new Archive();
            LastFmSync.Record(archive, new[] { Play("Kyle Hume", "Happy", 0) });

            var before = archive.LastCursorMs;
            var empty = LastFmSync.Record(archive, Array.Empty<Scrobble>());

            Check.Equal(0, empty.Recorded, "nothing recorded");
            Check.Equal(before, archive.LastCursorMs, "and the cursor did not move");
        });

        runner.Add("a play with no artist or album is dropped, not filed blank", () =>
        {
            var archive = new Archive();

            var result = LastFmSync.Record(archive, new[]
            {
                new Scrobble("", "", "Nameless", "", Noon.AddHours(1)),
                Play("Kyle Hume", "Happy", 2),
            });

            Check.Equal(1, result.Recorded, "one real play");
            Check.Equal(1, archive.Albums.Count, "and one album");
        });
    }

    private static void Cursors(TestRunner runner)
    {
        runner.Group("Last.fm: where the next sweep resumes");

        runner.Add("a fresh archive asks for the whole history", () =>
        {
            // Which is the point of the step. It can double count the handful of
            // plays another source already saw today, and that is a far smaller
            // harm than the history never arriving at all.
            Check.True(LastFmSync.Cursor(new Archive()) is null, "a fresh archive has a cursor");
        });

        runner.Add("the cursor follows the newest play recorded", () =>
        {
            var archive = new Archive();

            LastFmSync.Record(archive, new[]
            {
                Play("Kyle Hume", "Happy", 1),
                Play("Nic D", "Serotonin", 4),
            });

            Check.Equal(Noon.AddHours(4), LastFmSync.Cursor(archive), "the newest of the batch");
        });

        runner.Add("the cursor never goes backwards", () =>
        {
            // An out of order sweep, which a retry or a clock skew will produce,
            // must not rewind the archive and cause the whole history to be read
            // again.
            var archive = new Archive();

            LastFmSync.SetCursor(archive, Noon.AddHours(10));
            LastFmSync.SetCursor(archive, Noon.AddHours(2));

            Check.Equal(Noon.AddHours(10), LastFmSync.Cursor(archive), "it moved back");
        });

        runner.Add("a seeded album does not drag the cursor back to 1970", () =>
        {
            // Seeds are stamped one second after the epoch. Deriving the cursor
            // from the newest entry in the archive would work until the first
            // seed, and then quietly ask for the entire history every half hour.
            var archive = new Archive();

            LastFmSync.Record(archive, new[] { Play("Kyle Hume", "Happy", 3) });
            LastFmSync.SeedFrom(archive, new[] { new TopAlbum("Nic D", "Serotonin", "", 400) }, 10);

            Check.Equal(Noon.AddHours(3), LastFmSync.Cursor(archive), "the cursor moved");
        });

        runner.Add("the cursor survives a round trip through the file", () =>
        {
            // It is part of the contract shared with the Mac, so it has to be
            // written and read as milliseconds, not seconds.
            var archive = new Archive();
            LastFmSync.SetCursor(archive, Noon);

            Check.Equal(
                LastFm.ToUnixSeconds(Noon) * 1000L, archive.LastCursorMs,
                "milliseconds, not seconds");
        });
    }

    private static void Seeding(TestRunner runner)
    {
        runner.Group("Last.fm: the first-run seed");

        runner.Add("an empty archive is filled from the top albums", () =>
        {
            var archive = new Archive();

            var seeded = LastFmSync.SeedFrom(archive, new[]
            {
                new TopAlbum("Kyle Hume", "Happy", "https://art.example/h.png", 412),
                new TopAlbum("Nic D", "Serotonin", "https://art.example/s.png", 88),
            }, cap: 150);

            Check.Equal(2, seeded, "both seeded");
            Check.Equal(2, archive.Albums.Count, "and both in the archive");

            // Deliberately not 412. Last.fm's number is a lifetime total across
            // every device somebody has ever scrobbled from, and the archive's
            // other counts mean "times this app saw it play". The macOS build
            // drops it for the same reason.
            var happy = archive.Albums[AlbumEntry.MakeId("Kyle Hume", "Happy")];
            Check.Equal(0, happy.PlayCount, "a seed is not a play");
        });

        runner.Add("a seed never overwrites something really played", () =>
        {
            // A real listen outranks a seed, always. Overwriting one would stamp
            // it at 1970 and drop it to the back of every recency-biased pick.
            var archive = new Archive();

            LastFmSync.Record(archive, new[] { Play("Kyle Hume", "Happy", 0) });

            var seeded = LastFmSync.SeedFrom(archive, new[]
            {
                new TopAlbum("Kyle Hume", "Happy", "", 999),
            }, cap: 150);

            Check.Equal(0, seeded, "it was seeded over a real play");

            var happy = archive.Albums[AlbumEntry.MakeId("Kyle Hume", "Happy")];
            Check.Equal(Noon, happy.LastPlayed, "the real timestamp survived");
            Check.Equal(1, happy.PlayCount, "and the real count did");
        });

        runner.Add("the cap is honoured", () =>
        {
            var archive = new Archive();

            var many = Enumerable.Range(0, 40)
                .Select(i => new TopAlbum($"Artist {i}", $"Album {i}", "", 10))
                .ToArray();

            Check.Equal(5, LastFmSync.SeedFrom(archive, many, cap: 5), "five seeded");
            Check.Equal(5, archive.Albums.Count, "and five kept");
        });

        runner.Add("only an archive that would look empty gets seeded", () =>
        {
            // Seeding one that already holds an evening's listening would bury
            // it under a hundred and fifty albums nobody put on today.
            Check.True(LastFmSync.NeedsSeeding(new Archive()), "an empty archive");

            var full = new Archive();
            for (var i = 0; i < LastFmSync.SeedBelow; i++)
            {
                ArchiveUpdates.RecordPlay(full, $"id-{i}", $"Album {i}", "Someone", "", Noon);
            }

            Check.False(LastFmSync.NeedsSeeding(full), "an archive with a session in it");
        });

        runner.Add("seeding twice adds nothing the second time", () =>
        {
            var archive = new Archive();
            var albums = new[] { new TopAlbum("Kyle Hume", "Happy", "", 412) };

            LastFmSync.SeedFrom(archive, albums, cap: 150);

            Check.Equal(0, LastFmSync.SeedFrom(archive, albums, cap: 150), "seeded again");
            Check.Equal(1, archive.Albums.Count, "and duplicated");
        });
    }
}

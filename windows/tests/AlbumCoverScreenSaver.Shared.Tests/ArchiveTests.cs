using System.Text.Json;

namespace AlbumCoverScreenSaver.Shared.Tests;

public static class ArchiveTests
{
    private static AlbumEntry Album(string id, string played, int count = 1) => new()
    {
        Id = id,
        Name = id,
        Artist = "Artist " + id,
        ImageUrl = "https://example.test/" + id + ".jpg",
        FirstSeen = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastPlayed = DateTime.Parse(played, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal),
        PlayCount = count,
    };

    public static void Register(TestRunner runner)
    {
        runner.Group("Archive");

        runner.Add("round trips", () =>
        {
            var archive = new Archive
            {
                LastCursorMs = 1788561066092,
                Updated = new DateTime(2026, 9, 4, 22, 31, 2, DateTimeKind.Utc),
            };
            archive.Albums["a"] = Album("a", "2026-09-04T10:00:00Z", 4);
            archive.Albums["b"] = Album("b", "2026-09-04T11:00:00Z", 9);

            var json = JsonSerializer.Serialize(archive, SharedJson.Pretty);
            var back = JsonSerializer.Deserialize<Archive>(json, SharedJson.Pretty)!;

            Check.Equal(2, back.Count, "album count");
            Check.Equal(1788561066092L, back.LastCursorMs, "cursor");
            Check.Equal(archive.Updated, back.Updated, "updated");
            Check.Equal(9, back.Albums["b"].PlayCount, "play count");
            Check.Equal("Artist a", back.Albums["a"].Artist, "artist");
        });

        runner.Add("holds a cursor too large for a 32 bit integer", () =>
        {
            // A present day millisecond timestamp is about 1.79e12. Storing this
            // as an int would overflow, which is the kind of bug that only shows
            // up as history being re-ingested forever.
            const long cursor = 1788561066092;
            Check.True(cursor > int.MaxValue, "the value really does exceed int range");

            var json = JsonSerializer.Serialize(new Archive { LastCursorMs = cursor }, SharedJson.Pretty);
            var back = JsonSerializer.Deserialize<Archive>(json, SharedJson.Pretty)!;
            Check.Equal(cursor, back.LastCursorMs, "cursor survived");
        });

        runner.Add("orders newest first", () =>
        {
            var archive = new Archive();
            archive.Albums["old"] = Album("old", "2026-01-01T00:00:00Z");
            archive.Albums["new"] = Album("new", "2026-09-04T00:00:00Z");
            archive.Albums["mid"] = Album("mid", "2026-05-01T00:00:00Z");

            Check.Sequence(
                new[] { "new", "mid", "old" },
                archive.ByRecency().Select(entry => entry.Id),
                "recency order");
        });

        runner.Add("breaks ties by id so the same archive renders the same twice", () =>
        {
            var archive = new Archive();
            foreach (var id in new[] { "c", "a", "b" })
            {
                archive.Albums[id] = Album(id, "2026-09-04T00:00:00Z");
            }

            Check.Sequence(
                new[] { "a", "b", "c" },
                archive.ByRecency().Select(entry => entry.Id),
                "tie order");
        });

        runner.Add("skips one broken album instead of losing the archive", () =>
        {
            const string json = """
                {
                  "albums": {
                    "good":   {"id":"good","name":"G","artist":"A","imageURL":"u",
                               "firstSeen":"2026-01-01T00:00:00Z","lastPlayed":"2026-09-04T00:00:00Z","playCount":2},
                    "wrong":  "this should be an object",
                    "broken": {"id":"broken","playCount":"not a number"},
                    "also":   {"id":"also","name":"A","artist":"B","imageURL":"u",
                               "firstSeen":"2026-01-01T00:00:00Z","lastPlayed":"2026-09-03T00:00:00Z","playCount":1}
                  },
                  "lastCursorMs": 42,
                  "updated": "2026-09-04T00:00:00Z"
                }
                """;

            var archive = JsonSerializer.Deserialize<Archive>(json, SharedJson.Pretty)!;

            Check.Equal(2, archive.Count, "the two good albums survived");
            Check.True(archive.Albums.ContainsKey("good"), "good album kept");
            Check.True(archive.Albums.ContainsKey("also"), "second good album kept");
            Check.False(archive.Albums.ContainsKey("wrong"), "non-object entry dropped");
            Check.Equal(42L, archive.LastCursorMs, "cursor still read");
        });

        runner.Add("fills a missing id from the key it is filed under", () =>
        {
            const string json = """
                {"albums":{"lfm-a-b":{"name":"B","artist":"A","imageURL":"u","playCount":1}}}
                """;
            var archive = JsonSerializer.Deserialize<Archive>(json, SharedJson.Pretty)!;
            Check.Equal("lfm-a-b", archive.Albums["lfm-a-b"].Id, "id recovered from key");
        });

        runner.Add("returns an empty archive for a file that is not an object", () =>
        {
            var archive = JsonSerializer.Deserialize<Archive>("[]", SharedJson.Pretty)!;
            Check.Equal(0, archive.Count, "album count");
            Check.Equal(0L, archive.LastCursorMs, "cursor");
        });
    }
}

using System.Text.Json;

namespace AlbumCoverScreenSaver.Shared.Tests;

/// <summary>
/// The date format is the part of the contract most likely to break quietly.
/// .NET's own default writes fractional seconds and a local offset, and
/// Foundation's ISO8601 decoder refuses both, so an archive written by the
/// Windows build would simply fail to load on the Mac.
/// </summary>
public static class JsonFormatTests
{
    public static void Register(TestRunner runner)
    {
        runner.Group("Dates and JSON shape");

        runner.Add("writes the exact form Foundation produces", () =>
        {
            var moment = new DateTime(2026, 9, 4, 21, 52, 10, DateTimeKind.Utc);
            Check.Equal("2026-09-04T21:52:10Z", SharedJson.FormatDate(moment), "formatted date");
        });

        runner.Add("never writes fractional seconds", () =>
        {
            var moment = new DateTime(2026, 9, 4, 21, 52, 10, 456, DateTimeKind.Utc);
            Check.Equal("2026-09-04T21:52:10Z", SharedJson.FormatDate(moment), "formatted date");
        });

        runner.Add("converts a local time to UTC before writing", () =>
        {
            var utc = new DateTime(2026, 9, 4, 21, 52, 10, DateTimeKind.Utc);
            var local = utc.ToLocalTime();
            Check.Equal("2026-09-04T21:52:10Z", SharedJson.FormatDate(local), "formatted date");
        });

        runner.Add("treats an unspecified kind as UTC rather than local", () =>
        {
            var unspecified = new DateTime(2026, 9, 4, 21, 52, 10, DateTimeKind.Unspecified);
            Check.Equal("2026-09-04T21:52:10Z", SharedJson.FormatDate(unspecified), "formatted date");
        });

        runner.Add("writes the never sentinel the same way Swift does", () =>
            Check.Equal("0001-01-01T00:00:00Z", SharedJson.FormatDate(SharedJson.DistantPast), "distant past"));

        runner.Add("reads back what it writes", () =>
        {
            var moment = new DateTime(2026, 9, 4, 21, 52, 10, DateTimeKind.Utc);
            Check.True(SharedJson.TryParseDate(SharedJson.FormatDate(moment), out var parsed), "parsed");
            Check.Equal(moment, parsed, "round tripped date");
        });

        runner.Add("accepts fractional seconds on the way in", () =>
        {
            Check.True(SharedJson.TryParseDate("2026-09-04T21:52:10.123Z", out var parsed), "parsed");
            Check.Equal(new DateTime(2026, 9, 4, 21, 52, 10, DateTimeKind.Utc), parsed.AddTicks(-parsed.Ticks % TimeSpan.TicksPerSecond), "seconds");
        });

        runner.Add("accepts an offset and normalises it to UTC", () =>
        {
            Check.True(SharedJson.TryParseDate("2026-09-04T14:52:10-07:00", out var parsed), "parsed");
            Check.Equal(new DateTime(2026, 9, 4, 21, 52, 10, DateTimeKind.Utc), parsed, "normalised date");
        });

        runner.Add("refuses nonsense without throwing", () =>
        {
            Check.False(SharedJson.TryParseDate("not a date", out var parsed), "parse result");
            Check.Equal(SharedJson.DistantPast, parsed, "fallback");
            Check.False(SharedJson.TryParseDate(null, out _), "null");
            Check.False(SharedJson.TryParseDate("", out _), "empty");
        });

        runner.Add("a broken date costs its own field, not the whole record", () =>
        {
            const string json = """
                {"id":"x","name":"N","artist":"A","imageURL":"u",
                 "firstSeen":"nonsense","lastPlayed":"2026-09-04T21:52:10Z","playCount":3}
                """;
            var entry = JsonSerializer.Deserialize<AlbumEntry>(json, SharedJson.Pretty);

            Check.True(entry is not null, "entry decoded");
            Check.Equal(SharedJson.DistantPast, entry!.FirstSeen, "bad date fell back");
            Check.Equal(new DateTime(2026, 9, 4, 21, 52, 10, DateTimeKind.Utc), entry.LastPlayed, "good date survived");
            Check.Equal(3, entry.PlayCount, "rest of the record survived");
        });

        runner.Add("uses the wire names the Mac build uses", () =>
        {
            var entry = new AlbumEntry
            {
                Id = "lfm-a-b",
                Name = "B",
                Artist = "A",
                ImageUrl = "https://example.test/art.jpg",
                FirstSeen = new DateTime(2026, 9, 4, 21, 52, 10, DateTimeKind.Utc),
                LastPlayed = new DateTime(2026, 9, 4, 22, 31, 2, DateTimeKind.Utc),
                PlayCount = 7,
            };
            var json = JsonSerializer.Serialize(entry, SharedJson.Pretty);

            foreach (var key in new[] { "\"id\"", "\"name\"", "\"artist\"", "\"imageURL\"", "\"firstSeen\"", "\"lastPlayed\"", "\"playCount\"" })
            {
                Check.Contains(key, json, "wire key present");
            }
            Check.Contains("2026-09-04T21:52:10Z", json, "date written in the strict form");
            Check.DoesNotContain("+00:00", json, "no offset suffix");
            Check.DoesNotContain(".000", json, "no fractional seconds");
        });

        runner.Add("leaves non-ASCII unescaped, as Foundation does", () =>
        {
            var entry = new AlbumEntry { Id = "x", Artist = "Björk", Name = "Post" };
            var json = JsonSerializer.Serialize(entry, SharedJson.Pretty);
            Check.Contains("Björk", json, "artist written as-is");
            Check.DoesNotContain("\\u00", json, "no unicode escapes");
        });
    }
}

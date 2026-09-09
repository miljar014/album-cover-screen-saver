namespace AlbumCoverScreenSaver.Shared.Tests;

/// <summary>
/// The id rule decides whether Windows and macOS agree about what counts as the
/// same album. When they disagree the album quietly appears twice in the
/// archive and twice on screen, with no error anywhere.
/// </summary>
public static class MakeIdTests
{
    public static void Register(TestRunner runner)
    {
        runner.Group("Album ids");

        runner.Add("matches the worked example from the brief", () =>
            Check.Equal(
                "lfm-brothers-osborne-skeletons",
                AlbumEntry.MakeId("Brothers Osborne", "Skeletons"),
                "id"));

        runner.Add("case folds before anything else", () =>
            Check.Equal(
                AlbumEntry.MakeId("Brothers Osborne", "Skeletons"),
                AlbumEntry.MakeId("BROTHERS OSBORNE", "SkElEtOnS"),
                "id is case insensitive"));

        runner.Add("keeps digits", () =>
            Check.Equal(
                "lfm-blink-182-take-off-your-pants",
                AlbumEntry.MakeId("Blink 182", "Take Off Your Pants"),
                "id"));

        runner.Add("turns punctuation and the separator into hyphens", () =>
            Check.Equal(
                "lfm-ac-dc-back-in-black",
                AlbumEntry.MakeId("AC/DC", "Back in Black"),
                "id"));

        runner.Add("keeps accented letters rather than stripping them", () =>
            Check.Equal("lfm-björk-post", AlbumEntry.MakeId("Björk", "Post"), "id"));

        runner.Add("treats a decomposed accent as one character, like Swift does", () =>
        {
            // "Björk" written as B, j, o + combining diaeresis, r, k. Swift
            // walks grapheme clusters, so this is five characters there and
            // must be five here. A plain char loop would see six and emit an
            // extra hyphen.
            const string decomposed = "Björk";
            var id = AlbumEntry.MakeId(decomposed, "Post");
            Check.DoesNotContain("-rk", id, "no stray hyphen inside the name");
            Check.Equal("lfm-björk-post", id, "id");
        });

        runner.Add("counts an emoji as one character, not two", () =>
        {
            // The single most likely way a naive port diverges: an emoji is one
            // grapheme but two UTF-16 chars, so a char loop writes two hyphens.
            Check.Equal("lfm-a-b-c", AlbumEntry.MakeId("a\U0001F44Db", "c"), "id");
        });

        runner.Add("caps the safe part at 80 characters, not the whole id", () =>
        {
            var artist = new string('a', 100);
            var id = AlbumEntry.MakeId(artist, "ignored");

            Check.Equal(84, id.Length, "id length is the 4 character prefix plus 80");
            Check.Equal("lfm-" + new string('a', 80), id, "id");
        });

        runner.Add("survives empty input", () =>
        {
            Check.Equal("lfm--", AlbumEntry.MakeId("", ""), "id for two empty strings");
            Check.Equal("lfm--", AlbumEntry.MakeId(null, null), "id for nulls");
        });

        runner.Add("produces a filename-safe id, since it doubles as one", () =>
        {
            var id = AlbumEntry.MakeId("A: B / C \\ D * E? \"F\" <G> |H|", "I");
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                Check.False(id.Contains(invalid), $"id contains invalid filename char {(int)invalid}");
            }
        });
    }
}

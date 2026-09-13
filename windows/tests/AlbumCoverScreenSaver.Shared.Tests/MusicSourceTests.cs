namespace AlbumCoverScreenSaver.Shared.Tests;

/// <summary>
/// Which source the music comes from, and who to ask for it.
/// </summary>
public static class MusicSourceTests
{
    public static void Register(TestRunner runner)
    {
        runner.Group("Music source: the setting");

        runner.Add("the wire values match the Mac's", () =>
        {
            // Both builds store this under musicSource in their own preferences,
            // never in settings.json. Different spellings would be a silent
            // disagreement between two apps meant to read each other's files.
            Check.Equal("local", MusicSourceKind.Local.Id(), "this PC");
            Check.Equal("lastfm", MusicSourceKind.LastFm.Id(), "Last.fm");

            Check.Equal(2, MusicSourceKinds.All.Count, "two sources on Windows");
        });

        runner.Add("an unreadable setting falls back to the source that needs nothing", () =>
        {
            // Including spotify, which the Mac can store and Windows has never
            // been able to serve. A preference written by another version must
            // never leave the app unable to start.
            foreach (var stored in new[] { null, "", "   ", "spotify", "nonsense" })
            {
                Check.Equal(
                    MusicSourceKind.Local, MusicSourceKinds.ParseOr(stored),
                    $"stored value {stored ?? "null"}");
            }

            Check.Equal(MusicSourceKind.LastFm, MusicSourceKinds.ParseOr("lastfm"), "a good value");
            Check.Equal(MusicSourceKind.LastFm, MusicSourceKinds.ParseOr(" LastFM "), "and a sloppy one");
        });

        runner.Add("a pasted profile address becomes a username", () =>
        {
            // People paste the address of their profile page far more often than
            // they type the bare name, and refusing that is a worse answer than
            // understanding it.
            foreach (var pasted in new[]
                     {
                         "https://www.last.fm/user/miljar014",
                         "https://www.last.fm/user/miljar014/",
                         "last.fm/user/miljar014",
                         "https://www.last.fm/user/miljar014?date_preset=LAST_7_DAYS",
                         "  miljar014  ",
                     })
            {
                Check.Equal("miljar014", MusicSourceKinds.CleanUsername(pasted), pasted);
            }
        });

        runner.Add("the obvious mistakes are caught before a request is made", () =>
        {
            Check.False(MusicSourceKinds.LooksLikeUsername(""), "a blank box");
            Check.False(MusicSourceKinds.LooksLikeUsername("   "), "spaces");
            Check.False(MusicSourceKinds.LooksLikeUsername("jared miller"), "a name with a space");
            Check.False(MusicSourceKinds.LooksLikeUsername("jar@example.com"), "an email address");
            Check.False(MusicSourceKinds.LooksLikeUsername("last.fm/user/x"), "an unparsed address");

            Check.True(MusicSourceKinds.LooksLikeUsername("miljar014"), "a real one");
            Check.True(MusicSourceKinds.LooksLikeUsername("a_b-c.d"), "and a punctuated one");
        });

        runner.Add("cleaning then checking accepts what a person actually pastes", () =>
        {
            // The two are used together, in that order. Checking the raw text
            // would reject a pasted address that cleaning would have fixed.
            const string pasted = "https://www.last.fm/user/miljar014";

            Check.False(MusicSourceKinds.LooksLikeUsername(pasted), "raw, it is not a username");
            Check.True(
                MusicSourceKinds.LooksLikeUsername(MusicSourceKinds.CleanUsername(pasted)),
                "cleaned, it is");
        });

        runner.Add("both sources say what they are in plain words", () =>
        {
            // The settings window shows these, so they are part of the product
            // rather than a comment.
            foreach (var kind in MusicSourceKinds.All)
            {
                Check.True(kind.Title().Length > 0, $"{kind} has no title");
                Check.True(kind.Blurb().Length > 40, $"{kind} has no real description");
            }

            Check.Equal("This PC", MusicSourceKind.Local.Title(), "not This Mac");
        });
    }
}

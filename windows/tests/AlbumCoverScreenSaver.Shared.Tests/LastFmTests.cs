namespace AlbumCoverScreenSaver.Shared.Tests;

/// <summary>
/// Reading a listening history out of Last.fm.
/// </summary>
/// <remarks>
/// Every reply below is hand written to be the shape the service actually
/// sends, including the parts that are inconsistent: a field that is a bare
/// string in one place and an object in another, numbers that arrive as
/// strings, and a list that stops being a list when it has one item in it.
/// </remarks>
public static class LastFmTests
{
    private const string Key = "abc123";

    public static void Register(TestRunner runner)
    {
        Addresses(runner);
        History(runner);
        Images(runner);
        Seed(runner);
    }

    private static void Addresses(TestRunner runner)
    {
        runner.Group("Last.fm: the addresses");

        runner.Add("every call carries the user, the key and the format", () =>
        {
            foreach (var url in new[]
                     {
                         LastFm.RecentTracks("jared", Key, 200),
                         LastFm.NowPlaying("jared", Key),
                         LastFm.TopAlbums("jared", Key, 150),
                     })
            {
                Check.True(url.StartsWith(LastFm.BaseUrl, StringComparison.Ordinal), "the base address");
                Check.True(url.Contains("user=jared", StringComparison.Ordinal), $"the user in {url}");
                Check.True(url.Contains($"api_key={Key}", StringComparison.Ordinal), "the key");
                Check.True(url.Contains("format=json", StringComparison.Ordinal), "the format");
            }
        });

        runner.Add("a username with a space or an ampersand does not break the address", () =>
        {
            // Last.fm usernames can contain characters that mean something in a
            // query string. Pasting one in unescaped sends a request for a
            // different user, or for no user at all.
            var url = LastFm.RecentTracks("jar ed&admin=1", Key, 10);

            Check.False(url.Contains(" ", StringComparison.Ordinal), "a raw space");
            Check.True(url.Contains("jar%20ed%26admin%3D1", StringComparison.Ordinal), $"escaped, in {url}");
        });

        runner.Add("a resumed sweep asks only for what is new", () =>
        {
            // Without this every sweep re-reads the whole two hundred and the
            // archive does a great deal of work to learn nothing.
            var since = new DateTime(2026, 9, 12, 18, 0, 0, DateTimeKind.Utc);
            var url = LastFm.RecentTracks("jared", Key, 200, since);

            Check.True(url.Contains("&from=", StringComparison.Ordinal), "a from parameter");
            Check.True(
                url.Contains($"&from={LastFm.ToUnixSeconds(since)}", StringComparison.Ordinal),
                $"in seconds, in {url}");

            Check.False(
                LastFm.RecentTracks("jared", Key, 200).Contains("&from=", StringComparison.Ordinal),
                "and none at all on a first sweep");
        });

        runner.Add("unix seconds go out and come back unchanged", () =>
        {
            var when = new DateTime(2026, 9, 12, 18, 30, 45, DateTimeKind.Utc);

            Check.Equal(
                when, LastFm.FromUnixSeconds(LastFm.ToUnixSeconds(when)), "a round trip");

            Check.Equal(0L, LastFm.ToUnixSeconds(DateTime.UnixEpoch), "the epoch itself");
        });

        runner.Add("the seed asks for the whole of a listener's history", () =>
        {
            var url = LastFm.TopAlbums("jared", Key, LastFm.SeedLimit);

            Check.True(url.Contains("method=user.gettopalbums", StringComparison.Ordinal), "the method");
            Check.True(url.Contains("period=overall", StringComparison.Ordinal), "all time, not this week");
            Check.True(url.Contains("limit=150", StringComparison.Ordinal), "enough to fill a screen");
        });

        runner.Add("now playing asks for one track, not two hundred", () =>
        {
            var url = LastFm.NowPlaying("jared", Key);

            Check.True(url.Contains("limit=1", StringComparison.Ordinal), $"one track, in {url}");
            Check.True(url.Contains("method=user.getrecenttracks", StringComparison.Ordinal), "the method");
        });
    }

    private static void History(TestRunner runner)
    {
        runner.Group("Last.fm: reading a history");

        runner.Add("the track playing right now is never taken as history", () =>
        {
            // It has no date, because it has not finished. Recorded as a play it
            // would be ingested again on every sweep for as long as it is on,
            // and its play count would climb by one every thirty minutes.
            var plays = LastFm.ParseRecent(Recent);

            Check.Equal(2, plays.Count, "two finished plays");

            foreach (var play in plays)
            {
                Check.False(
                    play.Track.Contains("Still Going", StringComparison.Ordinal),
                    "the live track was recorded as history");
            }
        });

        runner.Add("but it is reported on its own", () =>
        {
            var live = LastFm.ParseNowPlaying(Recent);

            Check.True(live is not null, "nothing was playing");
            Check.Equal("Still Going", live!.Value.Track, "the track");
            Check.Equal("Kyle Hume", live.Value.Artist, "the artist");
            Check.Equal("Happy", live.Value.Album, "the album");
        });

        runner.Add("nothing is reported as playing when nothing is", () =>
        {
            // The first entry in the list is then just the most recent finished
            // play, and calling that live would be a lie the whole app repeats.
            Check.True(LastFm.ParseNowPlaying(Finished) is null, "a finished play was called live");
            Check.Equal(1, LastFm.ParseRecent(Finished).Count, "and it is still history");
        });

        runner.Add("singles are skipped rather than filed under nothing", () =>
        {
            // There is no album to file them under and no cover to show, so an
            // empty name would become an archive entry with a blank title.
            var plays = LastFm.ParseRecent(Recent);

            foreach (var play in plays)
            {
                Check.True(play.Album.Length > 0, "an album with no name got through");
                Check.True(play.Artist.Length > 0, "an artist with no name got through");
            }

            Check.False(
                plays.Any(play => play.Track.Contains("Loose Single", StringComparison.Ordinal)),
                "the single was recorded");
        });

        runner.Add("the times come back as real times", () =>
        {
            var plays = LastFm.ParseRecent(Recent);

            Check.Equal(
                new DateTime(2026, 9, 12, 18, 0, 0, DateTimeKind.Utc),
                plays[0].PlayedAt, "the newest play");

            Check.True(plays[0].PlayedAt > plays[1].PlayedAt, "newest first, as the service sends them");
        });

        runner.Add("a listener with exactly one play is not read as having none", () =>
        {
            // The service sends an object where everyone else gets an array of
            // one. Treating that as empty is how a brand new account ends up
            // with an archive that never fills.
            Check.Equal(1, LastFm.ParseRecent(OnlyOne).Count, "the one play");
            Check.Equal("Wide Awake", LastFm.ParseRecent(OnlyOne)[0].Album, "and it reads properly");
        });

        runner.Add("a refusal is a well formed reply, not an HTTP failure", () =>
        {
            // Last.fm answers a mistyped username with 200 OK and a message in
            // the body. A caller that only checks the status code sees an empty
            // history and reports nothing wrong, so the settings window would
            // say "connected" about an account that does not exist.
            const string refused = """{"message":"User not found","error":6}""";

            Check.Equal("User not found", LastFm.ErrorMessage(refused), "the service's own words");
            Check.Equal(0, LastFm.ParseRecent(refused).Count, "and no history came of it");

            Check.True(LastFm.ErrorMessage(Recent) is null, "a good reply is not an error");
        });

        runner.Add("an artist is found whichever name the field goes by", () =>
        {
            // It arrives as #text under recent tracks and as name under top
            // albums. The Mac tries both at each call site; this tries both in
            // one place so neither has to know.
            Check.Equal("Kyle Hume", LastFm.ParseRecent(Recent)[0].Artist, "from #text");
            Check.Equal("Kyle Hume", LastFm.ParseTopAlbums(Top)[0].Artist, "from name");
        });

        runner.Add("rubbish in does not throw", () =>
        {
            // A reply can be an error page, an empty body, or half a response
            // from a dropped connection. None of those may take the tray app
            // down with them.
            foreach (var bad in new[] { null, "", "   ", "not json", "{", "[]", "{\"error\":6}" })
            {
                Check.Equal(0, LastFm.ParseRecent(bad).Count, $"recent from {bad ?? "null"}");
                Check.Equal(0, LastFm.ParseTopAlbums(bad).Count, $"top albums from {bad ?? "null"}");
                Check.True(LastFm.ParseNowPlaying(bad) is null, $"now playing from {bad ?? "null"}");
            }
        });
    }

    private static void Images(TestRunner runner)
    {
        runner.Group("Last.fm: the artwork");

        runner.Add("the biggest usable image wins", () =>
        {
            var plays = LastFm.ParseRecent(Recent);

            Check.Equal(
                "https://lastfm.example/i/u/300x300/real.png", plays[0].ImageUrl,
                "extralarge, not large or medium");
        });

        runner.Add("the grey star placeholder is refused", () =>
        {
            // It is a real URL that fetches a real picture, so nothing
            // downstream notices. The result is a collage of identical grey
            // squares, which reads as a rendering fault rather than as missing
            // artwork.
            var plays = LastFm.ParseRecent(Recent);

            Check.Equal("", plays[1].ImageUrl, "the placeholder was taken as artwork");
        });

        runner.Add("it falls back through the sizes rather than giving up", () =>
        {
            Check.Equal(
                "https://lastfm.example/medium.png",
                LastFm.ParseRecent(OnlyMedium)[0].ImageUrl,
                "medium when there is nothing bigger");
        });
    }

    private static void Seed(TestRunner runner)
    {
        runner.Group("Last.fm: the first-run seed");

        runner.Add("top albums come back in order with their play counts", () =>
        {
            var albums = LastFm.ParseTopAlbums(Top);

            Check.Equal(2, albums.Count, "two albums");
            Check.Equal("Happy", albums[0].Album, "the most played");
            Check.Equal("Kyle Hume", albums[0].Artist, "its artist");
            Check.Equal(412, albums[0].PlayCount, "counted, even though it arrives as a string");
        });

        runner.Add("an album with no artist is skipped", () =>
        {
            foreach (var album in LastFm.ParseTopAlbums(Top))
            {
                Check.True(album.Artist.Length > 0, "an artist with no name got through");
                Check.True(album.Album.Length > 0, "an album with no name got through");
            }
        });

        runner.Add("seeded albums sort behind everything really played", () =>
        {
            // The seed exists so a fresh install opens full rather than empty,
            // but a seeded album is not a play. Stamping them one second after
            // the epoch puts them behind every real listen, so recency bias
            // still favours what you actually put on.
            Check.True(
                ArchiveUpdates.SeedStamp < new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                "a seeded album could outrank a real play");

            Check.True(
                ArchiveUpdates.SeedStamp > DateTime.UnixEpoch,
                "and it is still distinguishable from never played at all");
        });

        runner.Add("a history sweep is half-hourly, not every three seconds", () =>
        {
            // Reading a whole history on the now-playing cadence would be a
            // request every three seconds for data that changes a few times an
            // hour.
            Check.Equal(
                TimeSpan.FromMinutes(30), PollingPlan.HistorySweep, "the sweep interval");

            Check.True(
                PollingPlan.HistorySweep > PollingPlan.NowPlayingInterval(true) * 100,
                "the sweep is not far slower than the now-playing poll");
        });
    }

    /// <summary>
    /// Two finished plays, a single with no album, and a track playing now.
    /// </summary>
    private const string Recent = """
    {
      "recenttracks": {
        "track": [
          {
            "@attr": { "nowplaying": "true" },
            "artist": { "#text": "Kyle Hume" },
            "album": { "#text": "Happy" },
            "name": "Still Going",
            "image": [
              { "size": "medium", "#text": "https://lastfm.example/m.png" },
              { "size": "extralarge", "#text": "https://lastfm.example/xl.png" }
            ]
          },
          {
            "artist": { "#text": "Kyle Hume" },
            "album": { "#text": "Happy" },
            "name": "Happy",
            "date": { "uts": "1789236000", "#text": "12 Sep 2026, 18:00" },
            "image": [
              { "size": "medium", "#text": "https://lastfm.example/i/u/64x64/real.png" },
              { "size": "large", "#text": "https://lastfm.example/i/u/174x174/real.png" },
              { "size": "extralarge", "#text": "https://lastfm.example/i/u/300x300/real.png" }
            ]
          },
          {
            "artist": { "#text": "Braden Bales" },
            "album": { "#text": "" },
            "name": "Loose Single",
            "date": { "uts": "1789235000" }
          },
          {
            "artist": { "#text": "Quinn XCII" },
            "album": { "#text": "The People's Champ" },
            "name": "Common",
            "date": { "uts": "1789230000" },
            "image": [
              {
                "size": "extralarge",
                "#text": "https://lastfm.example/i/u/300x300/2a96cbd8b46e442fc41c2b86b821562f.png"
              }
            ]
          }
        ]
      }
    }
    """;

    /// <summary>Nothing playing: the newest entry is simply the last finished play.</summary>
    private const string Finished = """
    {
      "recenttracks": {
        "track": [
          {
            "artist": { "#text": "Nic D" },
            "album": { "#text": "Serotonin" },
            "name": "Serotonin",
            "date": { "uts": "1789200000" }
          }
        ]
      }
    }
    """;

    /// <summary>One play, sent as an object rather than as a list of one.</summary>
    private const string OnlyOne = """
    {
      "recenttracks": {
        "track": {
          "artist": { "#text": "Nic D" },
          "album": { "#text": "Wide Awake" },
          "name": "Wide Awake",
          "date": { "uts": "1789200000" }
        }
      }
    }
    """;

    private const string OnlyMedium = """
    {
      "recenttracks": {
        "track": [
          {
            "artist": { "#text": "Nic D" },
            "album": { "#text": "Wide Awake" },
            "name": "Wide Awake",
            "date": { "uts": "1789200000" },
            "image": [ { "size": "medium", "#text": "https://lastfm.example/medium.png" } ]
          }
        ]
      }
    }
    """;

    private const string Top = """
    {
      "topalbums": {
        "album": [
          {
            "name": "Happy",
            "playcount": "412",
            "artist": { "name": "Kyle Hume" },
            "image": [ { "size": "extralarge", "#text": "https://lastfm.example/happy.png" } ]
          },
          {
            "name": "Nameless",
            "playcount": "9",
            "artist": { "name": "" }
          },
          {
            "name": "Serotonin",
            "playcount": 88,
            "artist": { "name": "Nic D" },
            "image": [ { "size": "large", "#text": "https://lastfm.example/sero.png" } ]
          }
        ]
      }
    }
    """;
}

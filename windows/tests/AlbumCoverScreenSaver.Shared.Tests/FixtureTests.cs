namespace AlbumCoverScreenSaver.Shared.Tests;

/// <summary>
/// The fixtures are hand-built data standing in for the tray app, which does
/// not exist until step 4. Step 2 draws from them and step 3 renders them, so
/// they need to be valid against the real contract, not just look right.
/// </summary>
public static class FixtureTests
{
    /// <summary>
    /// Finds windows/fixtures by walking up from the test binary, so this works
    /// from `dotnet run`, from `dotnet test`, and from a published build.
    /// </summary>
    public static string? FindFixtureRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "fixtures");
            if (Directory.Exists(candidate) && Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return candidate;
            }
            directory = directory.Parent;
        }
        return null;
    }

    public static void Register(TestRunner runner)
    {
        runner.Group("Fixtures");

        var root = FindFixtureRoot();

        runner.Add("are where the tests can find them", () =>
            Check.True(root is not null, "windows/fixtures was not found above the test binary"));

        if (root is null) return;

        var store = new SharedStore(root);

        runner.Add("archive.json parses and holds real albums", () =>
        {
            var archive = store.LoadArchive();
            Check.True(archive.Count >= 8, $"expected at least 8 albums, found {archive.Count}");

            foreach (var album in archive.Albums.Values)
            {
                Check.Equal(album.Id, album.Id.Trim(), $"{album.Id} has no stray whitespace");
                Check.True(album.Name.Length > 0, $"{album.Id} has a name");
                Check.True(album.Artist.Length > 0, $"{album.Id} has an artist");
                Check.NotEqual(SharedJson.DistantPast, album.LastPlayed, $"{album.Id} has a real lastPlayed");
                Check.True(album.PlayCount >= 0, $"{album.Id} has a sane play count");
            }
        });

        runner.Add("every album is filed under its own id", () =>
        {
            foreach (var (key, album) in store.LoadArchive().Albums)
            {
                Check.Equal(key, album.Id, "key matches the album id");
            }
        });

        runner.Add("derived ids match the rule", () =>
        {
            foreach (var album in store.LoadArchive().Albums.Values)
            {
                if (!album.Id.StartsWith("lfm-", StringComparison.Ordinal)) continue;
                Check.Equal(AlbumEntry.MakeId(album.Artist, album.Name), album.Id, "derived id");
            }
        });

        runner.Add("every album has a cover on disk", () =>
        {
            // A style that features an album with no cover draws a hole, so the
            // fixtures must not contain one.
            foreach (var album in store.LoadArchive().Albums.Values)
            {
                Check.True(store.HasArtwork(album.Id), $"{album.Id} has artwork of at least 512 bytes");
            }
        });

        runner.Add("the covers really are JPEGs", () =>
        {
            foreach (var file in Directory.GetFiles(store.ArtDirectory, "*.jpg"))
            {
                var header = new byte[3];
                using (var stream = File.OpenRead(file)) _ = stream.Read(header, 0, 3);

                Check.Equal(0xFF, header[0], $"{Path.GetFileName(file)} starts with a JPEG marker");
                Check.Equal(0xD8, header[1], $"{Path.GetFileName(file)} starts with a JPEG marker");
                Check.Equal(0xFF, header[2], $"{Path.GetFileName(file)} starts with a JPEG marker");
            }
        });

        runner.Add("settings.json parses", () =>
        {
            var settings = store.LoadSettings();
            Check.Equal(CollageMode.Mosaic, settings.Mode, "mode");
            Check.Equal(MultiMonitorMode.Separate, settings.MultiMonitor, "multiMonitor");
        });

        runner.Add("nowplaying.json parses and describes something playing", () =>
        {
            var playing = store.LoadNowPlaying();
            Check.True(playing.AlbumId.Length > 0, "has an album id");
            Check.True(playing.DurationMs > 0, "has a duration, so the position styles have something to track");

            var archive = store.LoadArchive();
            Check.True(archive.Albums.ContainsKey(playing.AlbumId), "the playing album is in the archive");
        });

        runner.Add("the newest album in the archive is the one playing", () =>
        {
            // Several styles assume index 0 of the recency ordering is the most
            // recently played album, so the fixture should not contradict that.
            var archive = store.LoadArchive();
            var playing = store.LoadNowPlaying();
            Check.Equal(playing.AlbumId, archive.ByRecency()[0].Id, "newest album");
        });
    }
}

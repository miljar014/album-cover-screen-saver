namespace AlbumCoverScreenSaver.Shared.Tests;

public static class SharedStoreTests
{
    private static void InTemporaryStore(Action<SharedStore> body)
    {
        var root = Path.Combine(Path.GetTempPath(), "acss-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            body(new SharedStore(root));
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }

    public static void Register(TestRunner runner)
    {
        runner.Group("Shared folder");

        runner.Add("puts everything under one root", () =>
        {
            // The macOS build writes to two locations and reads whichever is
            // newer, to work around its sandbox. Windows has no such problem
            // and reintroducing that machinery would be porting a fix for a
            // problem this platform does not have.
            var store = new SharedStore(Path.Combine(Path.GetTempPath(), "acss-shape"));

            Check.Equal(Path.Combine(store.Root, "archive.json"), store.ArchivePath, "archive path");
            Check.Equal(Path.Combine(store.Root, "nowplaying.json"), store.NowPlayingPath, "now playing path");
            Check.Equal(Path.Combine(store.Root, "settings.json"), store.SettingsPath, "settings path");
            Check.Equal(Path.Combine(store.Root, "art"), store.ArtDirectory, "art path");
            Check.Equal(Path.Combine(store.ArtDirectory, "lfm-a-b.jpg"), store.ArtFile("lfm-a-b"), "art file");
        });

        runner.Add("names the folder AlbumCoverScreenSaver under the local app data folder", () =>
        {
            Check.Equal("AlbumCoverScreenSaver", SharedStore.FolderName, "folder name");
            Check.Contains("AlbumCoverScreenSaver", SharedStore.DefaultRoot(), "default root");
        });

        runner.Add("returns defaults when nothing has been written yet", () =>
            InTemporaryStore(store =>
            {
                Check.Equal(0, store.LoadArchive().Count, "archive");
                Check.Equal(4.0d, store.LoadSettings().Tempo, "settings");
                Check.False(store.LoadNowPlaying().IsPlaying, "now playing");
                Check.Equal(0, store.WriteErrors.Count, "a missing file is not an error");
            }));

        runner.Add("round trips all three files through the disk", () =>
            InTemporaryStore(store =>
            {
                store.EnsureDirectories();

                var archive = new Archive { LastCursorMs = 1788561066092 };
                archive.Albums["lfm-a-b"] = new AlbumEntry
                {
                    Id = "lfm-a-b",
                    Name = "B",
                    Artist = "A",
                    LastPlayed = new DateTime(2026, 9, 4, 22, 31, 2, DateTimeKind.Utc),
                    PlayCount = 3,
                };

                Check.True(store.SaveArchive(archive), "archive saved");
                Check.True(store.SaveSettings(new Settings { Mode = CollageMode.Cassette, Tempo = 2.5 }), "settings saved");
                Check.True(store.SaveNowPlaying(new NowPlaying { AlbumId = "lfm-a-b", IsPlaying = true }), "now playing saved");

                Check.Equal(1, store.LoadArchive().Count, "archive read back");
                Check.Equal(1788561066092L, store.LoadArchive().LastCursorMs, "cursor read back");
                Check.Equal(CollageMode.Cassette, store.LoadSettings().Mode, "settings read back");
                Check.Equal("lfm-a-b", store.LoadNowPlaying().AlbumId, "now playing read back");
                Check.Equal(0, store.WriteErrors.Count, "no write errors");
            }));

        runner.Add("refuses artwork below the minimum size", () =>
            InTemporaryStore(store =>
            {
                // A truncated download or a saved error page is smaller than
                // any real cover, and this is where it gets caught.
                Check.False(store.SaveArtwork("lfm-a-b", new byte[511]), "511 bytes refused");
                Check.False(store.HasArtwork("lfm-a-b"), "nothing was written");
                Check.Equal(1, store.WriteErrors.Count, "the refusal was recorded rather than swallowed");

                Check.True(store.SaveArtwork("lfm-a-b", new byte[512]), "512 bytes accepted");
                Check.True(store.HasArtwork("lfm-a-b"), "artwork is there");
                Check.Equal(1, store.ArtCount(), "one cover on disk");
            }));

        runner.Add("survives a corrupt settings file instead of throwing", () =>
            InTemporaryStore(store =>
            {
                store.EnsureDirectories();
                File.WriteAllText(store.SettingsPath, "{ this is not json");

                Check.Equal(4.0d, store.LoadSettings().Tempo, "fell back to defaults");
                Check.Equal(1, store.WriteErrors.Count, "the failure was recorded");
            }));

        runner.Add("leaves no temporary files behind", () =>
            InTemporaryStore(store =>
            {
                store.EnsureDirectories();
                for (var i = 0; i < 5; i++) store.SaveSettings(new Settings { Tempo = i });

                var leftovers = Directory.GetFiles(store.Root, "*.tmp-*");
                Check.Equal(0, leftovers.Length, "temporary files cleaned up");
                Check.Equal(4d, store.LoadSettings().Tempo, "last write won");
            }));

        runner.Add("can say out loud where it is reading and writing", () =>
            InTemporaryStore(store =>
            {
                // Settings appearing to do nothing was exactly this bug on
                // macOS: written to one place, read from another. Proving the
                // folder with a log line is cheaper than finding it twice.
                store.EnsureDirectories();
                store.SaveSettings(Settings.Default);

                var described = store.Describe();
                Check.Contains(store.Root, described, "the root is named");
                Check.Contains("settings=True", described, "it reports what is actually there");
            }));

        runner.Add("writes a log when a write fails", () =>
            InTemporaryStore(store =>
            {
                store.EnsureDirectories();
                store.SaveArtwork("lfm-a-b", new byte[10]);
                store.FlushWriteErrorLog();

                var log = Path.Combine(store.Root, "write-errors.log");
                Check.True(File.Exists(log), "log written");
                Check.Contains("below the 512 byte minimum", File.ReadAllText(log), "log content");
            }));
    }
}

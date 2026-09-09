using System.Reflection;
using System.Text.Json;

namespace AlbumCoverScreenSaver.Shared.Tests;

public static class SettingsTests
{
    public static void Register(TestRunner runner)
    {
        runner.Group("Settings defaults");

        runner.Add("match the table in the brief", () =>
        {
            var settings = Settings.Default;
            Check.Equal(CollageMode.Mosaic, settings.Mode, "mode");
            Check.Equal(200d, settings.TileSize, "tileSize");
            Check.Equal(4.0d, settings.Tempo, "tempo");
            Check.Equal(0.4d, settings.RecencyBias, "recencyBias");
            Check.True(settings.ShowTrackLabel, "showTrackLabel");
            Check.Equal(MultiMonitorMode.Separate, settings.MultiMonitor, "multiMonitor");
            Check.Equal(0, settings.Displays.Count, "displays");
            Check.Equal(1, settings.FlipsAtOnce, "flipsAtOnce");
            Check.Equal(0.75d, settings.FlipDuration, "flipDuration");
            Check.True(settings.MosaicRandomSize, "mosaicRandomSize");
            Check.Equal(16, settings.DriftCount, "driftCount");
            Check.Equal(1.0d, settings.DriftSpeed, "driftSpeed");
            Check.Equal(1.0d, settings.DriftScale, "driftScale");
            Check.True(settings.DriftRandomSize, "driftRandomSize");
            Check.Equal(0.3d, settings.DriftBackdropBlur, "driftBackdropBlur");
            Check.Equal(6.0d, settings.WallHold, "wallHold");
            Check.Equal(1.0d, settings.WallBuildSpeed, "wallBuildSpeed");
            Check.Equal(0.56d, settings.HeroSize, "heroSize");
            Check.Equal(0.35d, settings.HeroDim, "heroDim");
            Check.Equal(12.0d, settings.HeroInterval, "heroInterval");
            Check.True(settings.HeroFollowNowPlaying, "heroFollowNowPlaying");
            Check.Equal(180d, settings.VinylSecondsPerRecord, "vinylSecondsPerRecord");
            Check.Equal(9d, settings.VinylRpm, "vinylRPM");
            Check.True(settings.VinylFollowNowPlaying, "vinylFollowNowPlaying");
            Check.Equal(26, settings.VinylSleeveCount, "vinylSleeveCount");
            Check.Equal(1.0d, settings.VinylSleeveScale, "vinylSleeveScale");
            Check.Equal(CollageMode.Mosaic, settings.VinylFallbackMode, "vinylFallbackMode");
            Check.Equal(30d, settings.FeatureSeconds, "featureSeconds");
            Check.Equal(1.0d, settings.AmbientMotion, "ambientMotion");
            Check.Equal(16, settings.CrateCount, "crateCount");
            Check.Equal(4, settings.GalleryColumns, "galleryColumns");
            Check.Equal(16, settings.FlowCount, "flowCount");
            Check.Equal(14, settings.OrbitCount, "orbitCount");
            Check.Equal(1.0d, settings.OrbitSpeed, "orbitSpeed");
            Check.True(settings.CrtAmber, "crtAmber");
            Check.Equal(9, settings.CdCaseCount, "cdCaseCount");
            Check.Equal(9, settings.PolaroidCount, "polaroidCount");
            Check.Equal(14, settings.ZoetropeCount, "zoetropeCount");
        });

        runner.Add("all nineteen styles have a distinct id and a display name", () =>
        {
            Check.Equal(19, CollageModes.All.Count, "style count");

            var ids = CollageModes.All.Select(mode => mode.Id()).ToArray();
            Check.Equal(19, ids.Distinct(StringComparer.Ordinal).Count(), "distinct ids");

            var titles = CollageModes.All.Select(mode => mode.Title()).ToArray();
            Check.Equal(19, titles.Distinct(StringComparer.Ordinal).Count(), "distinct titles");

            foreach (var mode in CollageModes.All)
            {
                Check.True(CollageModes.TryParse(mode.Id(), out var back), $"{mode.Id()} parses");
                Check.Equal(mode, back, $"{mode.Id()} round trips");
            }

            Check.Equal("Mosaic Grid", CollageMode.Mosaic.Title(), "mosaic title");
            Check.Equal("Record Player", CollageMode.Vinyl.Title(), "vinyl title");
            Check.Equal("coverflow", CollageMode.CoverFlow.Id(), "cover flow id");
            Check.Equal("cdplayer", CollageMode.CdPlayer.Id(), "cd player id");
        });

        runner.Group("Settings decoding is lenient per field");

        runner.Add("an empty object gives every default", () =>
        {
            var settings = JsonSerializer.Deserialize<Settings>("{}", SharedJson.Pretty)!;
            Check.Equal(4.0d, settings.Tempo, "tempo");
            Check.Equal(CollageMode.Mosaic, settings.Mode, "mode");
            Check.Equal(26, settings.VinylSleeveCount, "vinylSleeveCount");
        });

        runner.Add("a file from an older version keeps the keys it does have", () =>
        {
            const string json = """{"mode":"vinyl","tempo":2.5,"vinylRPM":33.33}""";
            var settings = JsonSerializer.Deserialize<Settings>(json, SharedJson.Pretty)!;

            Check.Equal(CollageMode.Vinyl, settings.Mode, "mode");
            Check.Equal(2.5d, settings.Tempo, "tempo");
            Check.Equal(33.33d, settings.VinylRpm, "vinylRPM");
            Check.Equal(0.4d, settings.RecencyBias, "an absent key kept its default");
            Check.Equal(9, settings.PolaroidCount, "a much newer key kept its default");
        });

        runner.Add("one wrongly typed value costs only that value", () =>
        {
            // This is the bug the macOS build paid for. Strict decoding meant
            // one bad key silently reset every preference the user had set.
            const string json = """
                {"tempo":"fast","tileSize":320,"showTrackLabel":"yes","flipsAtOnce":5}
                """;
            var settings = JsonSerializer.Deserialize<Settings>(json, SharedJson.Pretty)!;

            Check.Equal(4.0d, settings.Tempo, "bad tempo fell back");
            Check.Equal(320d, settings.TileSize, "good tileSize survived");
            Check.True(settings.ShowTrackLabel, "bad boolean fell back");
            Check.Equal(5, settings.FlipsAtOnce, "good int survived");
        });

        runner.Add("an unknown style id falls back rather than failing", () =>
        {
            var settings = JsonSerializer.Deserialize<Settings>("""{"mode":"hologram","tempo":7}""", SharedJson.Pretty)!;
            Check.Equal(CollageMode.Mosaic, settings.Mode, "mode");
            Check.Equal(7d, settings.Tempo, "the rest of the file still loaded");
        });

        runner.Add("a count written as a decimal is still a count", () =>
        {
            var settings = JsonSerializer.Deserialize<Settings>("""{"driftCount":24.0}""", SharedJson.Pretty)!;
            Check.Equal(24, settings.DriftCount, "driftCount");
        });

        runner.Add("a file that is not an object gives defaults", () =>
        {
            var settings = JsonSerializer.Deserialize<Settings>("\"nonsense\"", SharedJson.Pretty)!;
            Check.Equal(4.0d, settings.Tempo, "tempo");
        });

        runner.Group("Multi-monitor settings");

        runner.Add("absent fields mean separate screens, every one inheriting", () =>
        {
            // A settings file written before the feature existed must behave
            // exactly like a build that has never heard of it.
            var settings = JsonSerializer.Deserialize<Settings>("""{"mode":"hero"}""", SharedJson.Pretty)!;
            Check.Equal(MultiMonitorMode.Separate, settings.MultiMonitor, "multiMonitor");
            Check.Equal(0, settings.Displays.Count, "displays");
            Check.Equal(CollageMode.Hero, settings.ModeForDisplay("never-seen-before"), "unknown display inherits");
        });

        runner.Add("reads the two relationship values and ignores anything else", () =>
        {
            Check.Equal(MultiMonitorMode.Linked,
                JsonSerializer.Deserialize<Settings>("""{"multiMonitor":"linked"}""", SharedJson.Pretty)!.MultiMonitor,
                "linked");
            Check.Equal(MultiMonitorMode.Separate,
                JsonSerializer.Deserialize<Settings>("""{"multiMonitor":"separate"}""", SharedJson.Pretty)!.MultiMonitor,
                "separate");
            Check.Equal(MultiMonitorMode.Separate,
                JsonSerializer.Deserialize<Settings>("""{"multiMonitor":"stretch"}""", SharedJson.Pretty)!.MultiMonitor,
                "an unknown value falls back to separate");
        });

        runner.Add("is never labelled Span or Stretch", () =>
        {
            // Windows uses both words for wallpaper cut across the bezels, and
            // this deliberately never straddles a bezel.
            foreach (var mode in new[] { MultiMonitorMode.Separate, MultiMonitorMode.Linked })
            {
                var title = mode.Title();
                Check.DoesNotContain("Span", title, "label");
                Check.DoesNotContain("Stretch", title, "label");
            }
            Check.Equal("Screens change together", MultiMonitorMode.Linked.Title(), "linked label");
        });

        runner.Add("resolves each display against the global style", () =>
        {
            const string json = """
                {
                  "mode": "hero",
                  "displays": {
                    "path-a": {"mode":"inherit",  "friendlyName":"DELL U2720Q", "lastRect":[0,0,3840,2160], "lastSeen":"2026-09-09T18:22:04Z"},
                    "path-b": {"mode":"vinyl",    "friendlyName":"LG 27",       "lastRect":[3840,0,1920,1080]},
                    "path-c": {"mode":"off",      "friendlyName":"Old Monitor"},
                    "path-d": {"mode":"hologram", "friendlyName":"Future Panel"}
                  }
                }
                """;
            var settings = JsonSerializer.Deserialize<Settings>(json, SharedJson.Pretty)!;

            Check.Equal(4, settings.Displays.Count, "display count");
            Check.Equal(CollageMode.Hero, settings.ModeForDisplay("path-a"), "inherit takes the global style");
            Check.Equal(CollageMode.Vinyl, settings.ModeForDisplay("path-b"), "an explicit style wins");
            Check.Equal(null, settings.ModeForDisplay("path-c"), "off resolves to nothing to draw");
            Check.Equal(CollageMode.Hero, settings.ModeForDisplay("path-d"), "an unrecognised style inherits, it does not go dark");
            Check.Equal(CollageMode.Hero, settings.ModeForDisplay("path-never-seen"), "an unknown display inherits");

            Check.True(settings.ForDisplay("path-c").IsOff, "off flag");
            Check.False(settings.ForDisplay("path-never-seen").IsOff, "an unknown display is never off");

            Check.Equal("DELL U2720Q", settings.Displays["path-a"].FriendlyName, "friendly name");
            Check.Sequence(new[] { 0, 0, 3840, 2160 }, settings.Displays["path-a"].LastRect, "rect");
            Check.Sequence(new[] { 0, 0, 0, 0 }, settings.Displays["path-c"].LastRect, "a missing rect is zeroed, not null");
            Check.Equal(SharedJson.DistantPast, settings.Displays["path-b"].LastSeen, "a missing lastSeen falls back");
        });

        runner.Group("Settings round trip");

        runner.Add("every field survives a write and a read", () =>
        {
            // Written with reflection on purpose. A field added to Settings but
            // forgotten in the converter fails here rather than being noticed
            // months later as a preference that will not stick.
            var settings = Settings.Default;
            var properties = typeof(Settings)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.CanRead && property.CanWrite)
                .ToArray();

            Check.True(properties.Length >= 37, $"expected the full settings model, found {properties.Length} properties");

            foreach (var property in properties)
            {
                property.SetValue(settings, Mutate(property.PropertyType, property.GetValue(settings), property.Name));
            }

            var json = JsonSerializer.Serialize(settings, SharedJson.Pretty);
            var back = JsonSerializer.Deserialize<Settings>(json, SharedJson.Pretty)!;

            foreach (var property in properties)
            {
                var expected = property.GetValue(settings);
                var actual = property.GetValue(back);

                if (expected is Dictionary<string, DisplaySetting> left)
                {
                    var right = (Dictionary<string, DisplaySetting>)actual!;
                    Check.Equal(left.Count, right.Count, "displays count");
                    foreach (var (key, value) in left)
                    {
                        Check.True(right.ContainsKey(key), $"displays kept {key}");
                        Check.Equal(value.Mode, right[key].Mode, $"displays[{key}].mode");
                        Check.Equal(value.FriendlyName, right[key].FriendlyName, $"displays[{key}].friendlyName");
                        Check.Sequence(value.LastRect, right[key].LastRect, $"displays[{key}].lastRect");
                        Check.Equal(value.LastSeen, right[key].LastSeen, $"displays[{key}].lastSeen");
                    }
                    continue;
                }

                Check.Equal(expected, actual, $"{property.Name} survived the round trip");
            }
        });
    }

    /// <summary>
    /// Produces a value different from the one given, so a field the converter
    /// forgets shows up as a value that reverted to its default.
    /// </summary>
    private static object Mutate(Type type, object? current, string name)
    {
        if (type == typeof(double)) return (double)current! + 1.25;
        if (type == typeof(int)) return (int)current! + 7;
        if (type == typeof(bool)) return !(bool)current!;

        if (type == typeof(CollageMode))
        {
            return (CollageMode)(((int)(CollageMode)current! + 5) % CollageModes.All.Count);
        }

        if (type == typeof(MultiMonitorMode))
        {
            return (MultiMonitorMode)current! == MultiMonitorMode.Linked
                ? MultiMonitorMode.Separate
                : MultiMonitorMode.Linked;
        }

        if (type == typeof(Dictionary<string, DisplaySetting>))
        {
            return new Dictionary<string, DisplaySetting>(StringComparer.Ordinal)
            {
                ["\\\\?\\DISPLAY#DEL4099#5&1a2b3c&0&UID4353#{guid}"] = new()
                {
                    Mode = "cassette",
                    FriendlyName = "DELL U2720Q",
                    LastRect = [0, 0, 3840, 2160],
                    LastSeen = new DateTime(2026, 9, 9, 18, 22, 4, DateTimeKind.Utc),
                },
                ["edid-1234-5678-0"] = new()
                {
                    Mode = DisplaySetting.Off,
                    FriendlyName = "Virtual Display",
                    LastRect = [3840, 0, 1920, 1080],
                    LastSeen = new DateTime(2026, 9, 9, 18, 22, 4, DateTimeKind.Utc),
                },
            };
        }

        throw new AssertionFailed(
            $"the round trip test does not know how to change {name} of type {type.Name}. " +
            "Teach Mutate about it, or the field is not really being tested.");
    }
}

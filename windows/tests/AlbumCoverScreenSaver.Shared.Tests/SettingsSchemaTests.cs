namespace AlbumCoverScreenSaver.Shared.Tests;

/// <summary>
/// The settings window builds itself from this table, so a mistake here is a
/// slider that cannot reach its own default, or a control that writes to the
/// wrong field. Both look like the app ignoring you.
/// </summary>
public static class SettingsSchemaTests
{
    private static IEnumerable<SliderControl> AllSliders()
    {
        foreach (var control in SettingsSchema.Shared.OfType<SliderControl>()) yield return control;

        foreach (var mode in CollageModes.All)
        {
            foreach (var control in SettingsSchema.For(mode).OfType<SliderControl>())
            {
                yield return control;
            }
        }
    }

    public static void Register(TestRunner runner)
    {
        runner.Group("Settings schema");

        runner.Add("every style has controls", () =>
        {
            foreach (var mode in CollageModes.All)
            {
                var controls = SettingsSchema.For(mode);
                Check.True(controls.Count > 0, $"{mode.Title()} has at least one control");
            }
        });

        runner.Add("every slider can reach its own default", () =>
        {
            // A range that excludes the default is the classic version of this
            // bug: the window opens, the slider snaps somewhere else, and the
            // setting silently changes just by looking at it.
            var defaults = Settings.Default;

            foreach (var slider in AllSliders())
            {
                var value = slider.Read(defaults);
                Check.True(value >= slider.Minimum && value <= slider.Maximum,
                    $"{slider.Key}: default {value} is inside {slider.Minimum} to {slider.Maximum}");
            }
        });

        runner.Add("every slider actually writes the field it reads", () =>
        {
            // Copy and paste across 40 controls is exactly how a slider ends up
            // wired to its neighbour's field.
            foreach (var slider in AllSliders())
            {
                var settings = Settings.Default;
                var target = slider.Whole
                    ? Math.Round((slider.Minimum + slider.Maximum) / 2)
                    : (slider.Minimum + slider.Maximum) / 2;

                slider.Write(settings, target);
                Check.Close(target, slider.Read(settings), 0.51, $"{slider.Key} round trips");
            }
        });

        runner.Add("every toggle writes the field it reads", () =>
        {
            var toggles = SettingsSchema.Shared.OfType<ToggleControl>()
                .Concat(CollageModes.All.SelectMany(mode => SettingsSchema.For(mode).OfType<ToggleControl>()))
                .ToArray();

            Check.True(toggles.Length >= 6, $"found {toggles.Length} toggles");

            foreach (var toggle in toggles)
            {
                var settings = Settings.Default;
                var flipped = !toggle.Read(settings);
                toggle.Write(settings, flipped);
                Check.Equal(flipped, toggle.Read(settings), $"{toggle.Key} round trips");
            }
        });

        runner.Add("no style offers two controls for the same setting", () =>
        {
            foreach (var mode in CollageModes.All)
            {
                var keys = SettingsSchema.For(mode).Select(control => control.Key).ToArray();
                Check.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count(),
                    $"{mode.Title()} has no duplicate controls");
            }
        });

        runner.Add("every single-album style offers the feature timer", () =>
        {
            CollageMode[] singleAlbum =
            [
                CollageMode.Ambient, CollageMode.Cassette, CollageMode.Crate,
                CollageMode.Gallery, CollageMode.CoverFlow, CollageMode.Jukebox,
                CollageMode.Starfield, CollageMode.Crt, CollageMode.CdPlayer,
                CollageMode.Newsstand, CollageMode.Vaporwave, CollageMode.Polaroid,
                CollageMode.Zoetrope, CollageMode.Subway,
            ];

            foreach (var mode in singleAlbum)
            {
                Check.True(
                    SettingsSchema.For(mode).Any(control => control.Key == "featureSeconds"),
                    $"{mode.Title()} has a seconds-per-album control");
            }
        });

        runner.Add("Record Player cannot be its own fallback", () =>
        {
            var choices = SettingsSchema.FallbackStyles();
            Check.Equal(18, choices.Count, "eighteen of the nineteen");
            Check.False(choices.Contains(CollageMode.Vinyl), "Record Player is not in the list");
            Check.True(choices.Contains(CollageMode.Mosaic), "Mosaic is");
        });

        runner.Group("Settings readouts");

        runner.Add("the flip rate reads as English and follows tempo", () =>
        {
            var mosaic = SettingsSchema.For(CollageMode.Mosaic)
                .OfType<SliderControl>().First(control => control.Key == "flipsAtOnce");

            var settings = Settings.Default;
            settings.Tempo = 5;

            // 60 x 1 / 5 = 12
            Check.Equal("≈ 12 per minute", mosaic.Format(settings, 1), "one flip every five seconds");

            settings.Tempo = 4;
            Check.Equal("≈ 15 per minute", mosaic.Format(settings, 1), "the default");
        });

        runner.Add("a spin speed near 33 and a third says so", () =>
        {
            Check.Equal("33⅓ RPM (true LP)", VinylSpeeds.Describe(33.33), "exactly");
            Check.Equal("33⅓ RPM (true LP)", VinylSpeeds.Describe(33.0), "close enough");
            Check.Equal("45 RPM", VinylSpeeds.Describe(45), "a single");
            Check.Equal("9 RPM", VinylSpeeds.Describe(9), "the hypnotic default");
        });

        runner.Add("the slider snaps onto real record speeds", () =>
        {
            // Landing on 33 and a third by hand is otherwise a test of nerve.
            Check.Close(33.33, VinylSpeeds.Snap(33.0), 1e-9, "snapped up to LP");
            Check.Close(33.33, VinylSpeeds.Snap(34.2), 1e-9, "snapped down to LP");
            Check.Close(45.0, VinylSpeeds.Snap(44.5), 1e-9, "snapped to single");
            Check.Close(9.0, VinylSpeeds.Snap(9.0), 1e-9, "left alone well away from either");
            Check.Close(38.0, VinylSpeeds.Snap(38.0), 1e-9, "and left alone between them");
        });

        runner.Add("a count of zero says none rather than nought", () =>
        {
            var cases = SettingsSchema.For(CollageMode.CdPlayer)
                .OfType<SliderControl>().First(control => control.Key == "cdCaseCount");

            Check.Equal("none", cases.Format(Settings.Default, 0), "zero");
            Check.Equal("9", cases.Format(Settings.Default, 9), "nine");
        });

        runner.Add("the recency slider names its ends", () =>
        {
            var recency = SettingsSchema.Shared
                .OfType<SliderControl>().First(control => control.Key == "recencyBias");

            var settings = Settings.Default;
            Check.Equal("Whole archive", recency.Format(settings, 0), "at zero");
            Check.Equal("Only recent", recency.Format(settings, 1), "at one");
            Check.Equal("40%", recency.Format(settings, 0.4), "in between");
        });

        runner.Add("a barely blurred backdrop says Sharp", () =>
        {
            var blur = SettingsSchema.For(CollageMode.Drift)
                .OfType<SliderControl>().First(control => control.Key == "driftBackdropBlur");

            Check.Equal("Sharp", blur.Format(Settings.Default, 0), "none at all");
            Check.Equal("Sharp", blur.Format(Settings.Default, 0.01), "barely any");
            Check.Equal("30%", blur.Format(Settings.Default, 0.3), "the default");
        });

        runner.Add("sizes and speeds carry their units", () =>
        {
            var tile = SettingsSchema.Shared.OfType<SliderControl>().First(c => c.Key == "tileSize");
            var tempo = SettingsSchema.Shared.OfType<SliderControl>().First(c => c.Key == "tempo");
            var speed = SettingsSchema.For(CollageMode.Drift)
                .OfType<SliderControl>().First(c => c.Key == "driftSpeed");

            Check.Equal("200 pt", tile.Format(Settings.Default, 200), "tile size");
            Check.Equal("4.0 s", tempo.Format(Settings.Default, 4), "tempo");
            Check.Equal("1.00×", speed.Format(Settings.Default, 1), "a multiplier");
        });
    }
}

namespace AlbumCoverScreenSaver.Shared.Tests;

/// <summary>
/// Colour maths and palette extraction.
/// </summary>
/// <remarks>
/// Every style from here on is drawn in colours pulled out of the cover rather
/// than in fixed ones. When that goes wrong it does not throw: it produces a
/// screen that is merely a bit muddy, or text that is a bit hard to read, which
/// is the sort of thing you look at for a week without being able to say what is
/// wrong with it.
/// </remarks>
public static class PaletteTests
{
    public static void Register(TestRunner runner)
    {
        Colours(runner);
        Contrast(runner);
        Extraction(runner);
        Fitting(runner);
    }

    // --- the colour type ----------------------------------------------------

    private static void Colours(TestRunner runner)
    {
        runner.Group("Colour: the maths");

        runner.Add("luminance is perceptual, not the average of the channels", () =>
        {
            // Pure green reads far brighter to an eye than pure blue, even
            // though a naive average would call them identical.
            var green = new Rgb(0f, 1f, 0f).Luminance();
            var blue = new Rgb(0f, 0f, 1f).Luminance();

            Check.Close(0.7152, green, 0.0001, "green");
            Check.Close(0.0722, blue, 0.0001, "blue");
            Check.True(green > blue * 9, "green reads far brighter than blue");
        });

        runner.Add("black and white sit at the ends", () =>
        {
            Check.Close(0.0, Rgb.Black.Luminance(), 0.0001, "black");
            Check.Close(1.0, Rgb.White.Luminance(), 0.0001, "white");
        });

        runner.Add("contrast runs from 1 to 21", () =>
        {
            Check.Close(21.0, Rgb.White.ContrastAgainst(Rgb.Black), 0.01, "the extremes");
            Check.Close(1.0, Rgb.White.ContrastAgainst(Rgb.White), 0.0001, "against itself");
        });

        runner.Add("contrast does not care which way round it is asked", () =>
        {
            var a = new Rgb(0.2f, 0.6f, 0.9f);
            var b = new Rgb(0.9f, 0.1f, 0.3f);

            Check.Close(a.ContrastAgainst(b), b.ContrastAgainst(a), 0.0001, "symmetric");
        });

        runner.Add("a colour survives a round trip through hue and saturation", () =>
        {
            var random = new Random(4242);
            for (var i = 0; i < 300; i++)
            {
                var original = new Rgb(
                    (float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());

                var (h, s, v) = original.ToHsv();
                var back = Rgb.FromHsv(h, s, v);

                Check.Close(original.R, back.R, 0.0005, "red");
                Check.Close(original.G, back.G, 0.0005, "green");
                Check.Close(original.B, back.B, 0.0005, "blue");
            }
        });

        runner.Add("setting the brightness keeps the hue", () =>
        {
            var orange = new Rgb(0.9f, 0.5f, 0.1f);
            var dimmed = orange.WithBrightness(0.2f);

            Check.Close(orange.ToHsv().H, dimmed.ToHsv().H, 0.002, "hue is untouched");
            Check.Close(0.2, dimmed.ToHsv().V, 0.0001, "brightness is what was asked for");
        });

        runner.Add("setting the brightness discards any transparency", () =>
        {
            // These are surfaces, not overlays. A half-transparent console
            // would show the desktop through the turntable.
            var ghost = new Rgb(0.9f, 0.5f, 0.1f, 0.25f);
            Check.Close(1.0, ghost.WithBrightness(0.3f).A, 0.0001, "alpha forced to one");
        });

        runner.Add("the room is always dark, however bright the cover", () =>
        {
            // The whole point of deep: a white sleeve must not produce a
            // glaring screen at three in the morning.
            foreach (var cover in new[]
                     {
                         Rgb.White,
                         new Rgb(1f, 0.95f, 0.9f),
                         new Rgb(0.2f, 0.2f, 0.25f),
                         new Rgb(0.9f, 0.1f, 0.1f),
                     })
            {
                var deep = cover.WithBrightness(0.10f, 0.85f);
                Check.True(deep.Luminance() < 0.02f,
                    $"a cover at luminance {cover.Luminance():0.00} gives a room at {deep.Luminance():0.000}");
            }
        });
    }

    // --- the readability loop -----------------------------------------------

    private static void Contrast(TestRunner runner)
    {
        runner.Group("Colour: nudging a colour until it is legible");

        runner.Add("a colour that is already legible is returned untouched", () =>
        {
            var deep = new Rgb(0.05f, 0.04f, 0.03f);
            var bright = new Rgb(0.95f, 0.9f, 0.85f);

            Check.Equal(bright, bright.Readable(deep, target: 5f), "unchanged");
        });

        runner.Add("on a dark ground it brightens, on a light ground it darkens", () =>
        {
            var mid = new Rgb(0.45f, 0.35f, 0.30f);

            // The targets differ because this colour already clears 5 against
            // white and would be returned untouched, which is correct and is
            // not what this test is about.
            var onDark = mid.Readable(new Rgb(0.05f, 0.05f, 0.05f), 5f);
            var onLight = mid.Readable(new Rgb(0.95f, 0.95f, 0.95f), 10f);

            Check.True(onDark.Luminance() > mid.Luminance(), "brightened on a dark ground");
            Check.True(onLight.Luminance() < mid.Luminance(), "darkened on a light ground");
        });

        runner.Add("it keeps the hue, so the colour still reads as the album's", () =>
        {
            var teal = new Rgb(0.10f, 0.38f, 0.36f);
            var fixedUp = teal.Readable(new Rgb(0.06f, 0.06f, 0.06f), 5f);

            Check.Close(teal.ToHsv().H, fixedUp.ToHsv().H, 0.01, "hue survives");
            Check.True(fixedUp.ContrastAgainst(new Rgb(0.06f, 0.06f, 0.06f)) >= 5f, "and it is legible");
        });

        runner.Add("saturation gives way only once brightness has run out", () =>
        {
            // A vivid colour on a dark ground reaches the target on brightness
            // alone, so it stays vivid. A near-white one on a light ground has
            // nowhere to go but toward grey.
            var vivid = new Rgb(0.2f, 0.5f, 0.1f);
            var onDark = vivid.Readable(new Rgb(0.04f, 0.04f, 0.04f), 5f);

            Check.True(onDark.ToHsv().S > 0.5f,
                $"kept its saturation at {onDark.ToHsv().S:0.00}");
        });

        runner.Add("white on the darkest room reaches the text target of twelve", () =>
        {
            var deep = new Rgb(0.02f, 0.02f, 0.02f);
            var text = Rgb.White.Readable(deep, target: 12f);

            Check.True(text.ContrastAgainst(deep) >= 12f, "legible body text");
        });

        runner.Add("it gives up rather than looping on a colour that cannot get there", () =>
        {
            // Mid grey on mid grey. There is no legible relative anywhere, and
            // something close is a better answer than a hang.
            var impossible = new Rgb(0.5f, 0.5f, 0.5f);
            var result = impossible.Readable(new Rgb(0.5f, 0.5f, 0.5f), target: 21f);

            Check.True(result.ContrastAgainst(new Rgb(0.5f, 0.5f, 0.5f)) < 21f,
                "it did not reach the target, which is allowed");
        });

        runner.Add("it terminates for every colour on every ground", () =>
        {
            var random = new Random(7777);
            for (var i = 0; i < 400; i++)
            {
                var colour = new Rgb(
                    (float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
                var ground = new Rgb(
                    (float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());

                var result = colour.Readable(ground, 4.5f);

                Check.True(float.IsFinite(result.R) && float.IsFinite(result.G) && float.IsFinite(result.B),
                    "produced a real colour");
            }
        });
    }

    // --- palette extraction -------------------------------------------------

    /// <summary>A flat square of one colour, in the shape Build wants.</summary>
    private static float[] Flat(Rgb colour, int side = ArtPalette.SampleSide)
    {
        var pixels = new float[side * side * 3];
        for (var i = 0; i < side * side; i++)
        {
            pixels[i * 3] = colour.R;
            pixels[(i * 3) + 1] = colour.G;
            pixels[(i * 3) + 2] = colour.B;
        }
        return pixels;
    }

    private static void Extraction(TestRunner runner)
    {
        runner.Group("Palette: pulling colours out of a cover");

        runner.Add("nothing to sample gives nothing back", () =>
        {
            Check.True(ArtPalette.Build([]) is null, "an empty buffer");
        });

        runner.Add("the room is dark and the text on it is legible, whatever the cover", () =>
        {
            var random = new Random(2024);

            for (var trial = 0; trial < 40; trial++)
            {
                var pixels = new float[ArtPalette.SampleSide * ArtPalette.SampleSide * 3];
                for (var i = 0; i < pixels.Length; i++) pixels[i] = (float)random.NextDouble();

                var palette = ArtPalette.Build(pixels);
                Check.True(palette is not null, "a palette was built");

                Check.True(palette!.Deep.Luminance() < 0.02f,
                    $"the room is at luminance {palette.Deep.Luminance():0.000}");

                // Twelve is the target; the loop is allowed to fall short, but
                // never by much on a real cover.
                Check.True(palette.Text.ContrastAgainst(palette.Deep) > 10f,
                    $"text contrast is {palette.Text.ContrastAgainst(palette.Deep):0.0}");
            }
        });

        runner.Add("the accent is the cover's vivid colour, not its brightest pixel", () =>
        {
            // Mostly near-white, with a small patch of deep red. The red is what
            // a person would call the cover's colour.
            const int side = ArtPalette.SampleSide;
            var pixels = Flat(new Rgb(0.93f, 0.93f, 0.91f));

            for (var i = 0; i < side * 2; i++)
            {
                pixels[i * 3] = 0.72f;
                pixels[(i * 3) + 1] = 0.10f;
                pixels[(i * 3) + 2] = 0.12f;
            }

            var palette = ArtPalette.Build(pixels)!;
            var (hue, saturation, _) = palette.Accent.ToHsv();

            Check.True(saturation > 0.4f, $"the accent is vivid, saturation {saturation:0.00}");
            Check.True(hue < 0.06f || hue > 0.94f, $"and it is red, hue {hue:0.00}");
        });

        runner.Add("a flat grey cover still produces three usable swatches", () =>
        {
            // Every bucket is a near-grey and every one gets dropped, because a
            // grey bubble among coloured ones reads as a bug rather than as a
            // subtle choice. The accent set is what fills the gap.
            var palette = ArtPalette.Build(Flat(new Rgb(0.5f, 0.5f, 0.5f)))!;

            Check.Equal(3, palette.Swatches.Count, "the replacement set");
        });

        runner.Add("no swatch is ever a near-grey", () =>
        {
            var random = new Random(1312);

            for (var trial = 0; trial < 30; trial++)
            {
                var pixels = new float[ArtPalette.SampleSide * ArtPalette.SampleSide * 3];
                for (var i = 0; i < pixels.Length; i++) pixels[i] = (float)random.NextDouble();

                var palette = ArtPalette.Build(pixels)!;

                // Only meaningful when real swatches survived; the replacement
                // set is built from the accent and is vivid by construction.
                foreach (var swatch in palette.Swatches)
                {
                    Check.True(swatch.ToHsv().S > 0.16f,
                        $"a swatch at saturation {swatch.ToHsv().S:0.00} got through");
                }
            }
        });

        runner.Add("there are never more than six swatches, nor fewer than three", () =>
        {
            var random = new Random(606);

            for (var trial = 0; trial < 40; trial++)
            {
                var pixels = new float[ArtPalette.SampleSide * ArtPalette.SampleSide * 3];
                for (var i = 0; i < pixels.Length; i++) pixels[i] = (float)random.NextDouble();

                var count = ArtPalette.Build(pixels)!.Swatches.Count;
                Check.True(count is >= 3 and <= 6, $"{count} swatches");
            }
        });

        runner.Add("the colour cube's five levels per channel never collide", () =>
        {
            // int(1.0 * 4) is 4, so each channel has levels 0 to 4 and the
            // four-bit fields stay separate. A sixth level would overflow into
            // the neighbouring field and merge unrelated colours.
            var seen = new HashSet<int>();

            for (var r = 0; r <= 4; r++)
            {
                for (var g = 0; g <= 4; g++)
                {
                    for (var b = 0; b <= 4; b++)
                    {
                        Check.True(seen.Add((r << 8) | (g << 4) | b), $"collision at {r},{g},{b}");
                    }
                }
            }

            Check.Equal(125, seen.Count, "five levels cubed");
        });

        runner.Add("the sampling is order independent", () =>
        {
            // The macOS buffer is upside down relative to the visible art and it
            // does not matter, because every statistic is a sum or a maximum.
            // Worth proving rather than assuming, since it is the reason nobody
            // has to flip anything.
            var random = new Random(88);
            var pixels = new float[ArtPalette.SampleSide * ArtPalette.SampleSide * 3];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = (float)random.NextDouble();

            var forwards = ArtPalette.Build(pixels)!;

            var reversed = new float[pixels.Length];
            var count = pixels.Length / 3;
            for (var i = 0; i < count; i++)
            {
                Array.Copy(pixels, i * 3, reversed, (count - 1 - i) * 3, 3);
            }

            var backwards = ArtPalette.Build(reversed)!;

            Check.Equal(forwards.Deep, backwards.Deep, "the room");
            Check.Equal(forwards.Accent, backwards.Accent, "the accent");
            Check.Equal(forwards.Metal, backwards.Metal, "the metal");
        });

        runner.Add("the fallback set is usable in its own right", () =>
        {
            // It is what a cover that will not decode gets, so it has to look
            // deliberate rather than like a failure.
            var fallback = ArtPalette.Fallback;

            Check.True(fallback.Deep.Luminance() < 0.02f, "a dark room");
            Check.True(fallback.Text.ContrastAgainst(fallback.Deep) > 12f, "legible text");
            Check.True(fallback.Accent.ContrastAgainst(fallback.Deep) > 5f, "a visible accent");
            Check.Equal(5, fallback.Swatches.Count, "five swatches");
        });
    }

    // --- shrink to fit ------------------------------------------------------

    private static void Fitting(TestRunner runner)
    {
        runner.Group("Text: shrinking a title until it fits");

        // A stand-in for a text engine: height grows with the length of the
        // text and shrinks with the font size, which is all the loop needs.
        static Func<float, float> Measurer(float characters, float width) =>
            size => MathF.Ceiling(characters * size / width) * size * 1.2f;

        runner.Add("text that already fits keeps the size it was given", () =>
        {
            Check.Close(22.0, TextFit.Size(22f, 500f, Measurer(12f, 400f)), 0.0001, "a short title");
        });

        runner.Add("a long title is shrunk until it fits", () =>
        {
            var measure = Measurer(90f, 400f);
            var chosen = TextFit.Size(22f, 120f, measure);

            Check.True(chosen < 22f, $"shrunk to {chosen:0.0}");
            Check.True(measure(chosen) <= 120f, "and it fits");
        });

        runner.Add("it never goes below 45% of the base size", () =>
        {
            // Better a title that overflows slightly than one rendered at four
            // points, which is not text any more.
            var chosen = TextFit.Size(22f, 1f, Measurer(5000f, 100f));

            Check.Close(22f * TextFit.Floor, chosen, 0.0001, "the floor");
        });

        runner.Add("it always terminates, even when nothing fits", () =>
        {
            Check.Close(10f * TextFit.Floor, TextFit.Size(10f, 0f, _ => 1f), 0.0001, "no size fits");
            Check.Close(0.0, TextFit.Size(0f, 100f, _ => 0f), 0.0001, "a zero base");
        });

        runner.Add("it takes about ten steps to reach the floor", () =>
        {
            // 0.92 to the tenth is about 0.434, just under the floor, so the
            // search is cheap enough to run per frame without thinking about it.
            var steps = 0;
            TextFit.Size(22f, 0f, _ => { steps++; return 1f; });

            Check.True(steps is >= 8 and <= 12, $"{steps} measurements");
        });
    }
}

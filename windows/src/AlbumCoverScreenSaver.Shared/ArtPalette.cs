namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// The colour scheme a cover gives a style: a room to sit it in, a console to
/// build the object out of, and an accent that still reads as the album.
/// </summary>
/// <remarks>
/// <para>
/// Every device style and most scene styles are drawn in these colours rather
/// than in fixed ones, which is why a Record Player showing a Miles Davis record
/// looks like a different object from one showing a Kate Bush record.
/// </para>
/// <para>
/// <b>The room is always kept dark.</b> Deep is the average cover colour at ten
/// per cent brightness whatever the cover was, so a white sleeve does not
/// produce a glaring screen at three in the morning, and the artwork stays the
/// brightest thing present.
/// </para>
/// </remarks>
public sealed record ArtPalette(
    Rgb Accent,
    Rgb Deep,
    Rgb Console,
    Rgb ConsoleLight,
    Rgb Metal,
    Rgb Text,
    Rgb TextMuted,
    IReadOnlyList<Rgb> Swatches)
{
    /// <summary>The side of the square the cover is reduced to before sampling.</summary>
    public const int SampleSide = 28;

    /// <summary>
    /// Used when there is no cover, or when extraction finds nothing. A warm
    /// amber and brown set, chosen to look deliberate rather than like a failure.
    /// </summary>
    public static readonly ArtPalette Fallback = new(
        Accent: new Rgb(0.85f, 0.63f, 0.17f),
        Deep: new Rgb(0.09f, 0.06f, 0.04f),
        Console: new Rgb(0.34f, 0.21f, 0.12f),
        ConsoleLight: new Rgb(0.48f, 0.31f, 0.18f),
        Metal: new Rgb(0.78f, 0.77f, 0.75f),
        Text: new Rgb(0.97f, 0.97f, 0.97f),
        TextMuted: new Rgb(0.72f, 0.72f, 0.72f),
        Swatches:
        [
            new Rgb(0.95f, 0.72f, 0.25f),
            new Rgb(0.90f, 0.40f, 0.30f),
            new Rgb(0.45f, 0.80f, 0.85f),
            new Rgb(0.70f, 0.55f, 0.90f),
            new Rgb(0.55f, 0.85f, 0.55f),
        ]);

    /// <summary>
    /// Builds a palette from a small square of cover pixels.
    /// </summary>
    /// <param name="pixels">
    /// RGB triples, three floats per pixel in 0 to 1, row order irrelevant.
    /// Every statistic below is order-independent, so a buffer that is upside
    /// down relative to the visible art gives identical numbers.
    /// </param>
    /// <returns>The palette, or null if there was nothing to sample.</returns>
    public static ArtPalette? Build(ReadOnlySpan<float> pixels)
    {
        if (pixels.Length < 3) return null;

        var count = pixels.Length / 3;

        double sumR = 0, sumG = 0, sumB = 0;

        var bestScore = -1f;
        var raw = new Rgb(0.5f, 0.4f, 0.3f);

        // A coarse colour cube. int(1.0 * 4) is 4, so each channel has five
        // levels and the four-bit fields never collide.
        var buckets = new Dictionary<int, (int Count, double R, double G, double B)>();

        for (var i = 0; i < count; i++)
        {
            var r = pixels[i * 3];
            var g = pixels[(i * 3) + 1];
            var b = pixels[(i * 3) + 2];

            sumR += r;
            sumG += g;
            sumB += b;

            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var saturation = max <= 0f ? 0f : (max - min) / max;

            // Heavily reward vividness, mildly reward brightness, penalise
            // distance from mid-bright. That is what a person would name as
            // "the cover's colour", which is neither the brightest pixel nor
            // the most common one.
            var score = (saturation * 1.6f) + (max * 0.4f) - Math.Abs(max - 0.65f);
            if (score > bestScore)
            {
                bestScore = score;
                raw = new Rgb(r, g, b);
            }

            var key = ((int)(r * 4) << 8) | ((int)(g * 4) << 4) | (int)(b * 4);
            if (buckets.TryGetValue(key, out var bucket))
            {
                buckets[key] = (bucket.Count + 1, bucket.R + r, bucket.G + g, bucket.B + b);
            }
            else
            {
                buckets[key] = (1, r, g, b);
            }
        }

        if (count == 0) return null;

        var average = new Rgb((float)(sumR / count), (float)(sumG / count), (float)(sumB / count));

        var deep = average.WithBrightness(0.10f, 0.85f);
        var console = average.WithBrightness(0.30f, 0.95f);
        var consoleLight = average.WithBrightness(0.44f, 0.90f);
        var metal = raw.WithBrightness(0.80f, 0.18f);
        var text = Rgb.White.Readable(deep, target: 12f);
        var accent = raw.Readable(deep, target: 5.0f);

        return new ArtPalette(
            Accent: accent,
            Deep: deep,
            Console: console,
            ConsoleLight: consoleLight,
            Metal: metal,
            Text: text,
            TextMuted: text.WithAlpha(0.62f),
            Swatches: BuildSwatches(buckets, accent, metal));
    }

    /// <summary>
    /// A spread of the cover's own colours, lifted so they read as lit glass
    /// rather than as muddy paint.
    /// </summary>
    /// <remarks>
    /// Near-greys are dropped outright: a grey bubble among coloured ones does
    /// not read as a subtle choice, it reads as a bug. If that leaves fewer than
    /// three, the whole list is replaced with a set derived from the accent,
    /// because two swatches look like something failed.
    /// </remarks>
    private static List<Rgb> BuildSwatches(
        Dictionary<int, (int Count, double R, double G, double B)> buckets, Rgb accent, Rgb metal)
    {
        var swatches = buckets
            .OrderByDescending(pair => pair.Value.Count)
            .Select(pair => new Rgb(
                (float)(pair.Value.R / pair.Value.Count),
                (float)(pair.Value.G / pair.Value.Count),
                (float)(pair.Value.B / pair.Value.Count)))
            .Select(colour => colour.WithBrightness(0.88f, 1.5f))
            .Where(colour => colour.ToHsv().S > 0.16f)
            .Take(6)
            .ToList();

        if (swatches.Count >= 3) return swatches;

        return
        [
            accent,
            accent.WithBrightness(0.7f),
            metal.WithBrightness(0.9f, 3f),
        ];
    }
}

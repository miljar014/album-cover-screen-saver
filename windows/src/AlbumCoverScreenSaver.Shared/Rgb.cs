namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// A colour, and the handful of operations the styles do to one.
/// </summary>
/// <remarks>
/// <para>
/// Its own type rather than a drawing library's, because every one of these
/// operations is arithmetic that can be got quietly wrong, and none of them
/// needs a canvas. Kept here, they can be proved by number.
/// </para>
/// <para>
/// Channels run 0 to 1 and are not clamped on construction: an intermediate
/// value may legitimately step outside while a loop is converging, and clamping
/// it early would hide that.
/// </para>
/// </remarks>
public readonly record struct Rgb(float R, float G, float B, float A = 1f)
{
    public static readonly Rgb White = new(1f, 1f, 1f);

    public static readonly Rgb Black = new(0f, 0f, 0f);

    public Rgb WithAlpha(float alpha) => this with { A = alpha };

    /// <summary>
    /// WCAG relative luminance: how bright this colour reads to a human eye,
    /// which is not at all the same as the average of its channels.
    /// </summary>
    public float Luminance()
    {
        static float Channel(float value)
        {
            var v = Math.Clamp(value, 0f, 1f);
            return v <= 0.03928f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
        }

        return (0.2126f * Channel(R)) + (0.7152f * Channel(G)) + (0.0722f * Channel(B));
    }

    /// <summary>The WCAG contrast ratio between two colours, 1 to 21.</summary>
    public float ContrastAgainst(Rgb other)
    {
        var mine = Luminance();
        var theirs = other.Luminance();
        return (Math.Max(mine, theirs) + 0.05f) / (Math.Min(mine, theirs) + 0.05f);
    }

    /// <summary>Hue in 0 to 1, saturation and brightness in 0 to 1.</summary>
    public (float H, float S, float V) ToHsv()
    {
        var r = Math.Clamp(R, 0f, 1f);
        var g = Math.Clamp(G, 0f, 1f);
        var b = Math.Clamp(B, 0f, 1f);

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        var saturation = max <= 0f ? 0f : delta / max;
        if (delta <= 0f) return (0f, saturation, max);

        float hue;
        if (max == r) hue = (g - b) / delta % 6f;
        else if (max == g) hue = ((b - r) / delta) + 2f;
        else hue = ((r - g) / delta) + 4f;

        hue /= 6f;
        if (hue < 0f) hue += 1f;

        return (hue, saturation, max);
    }

    public static Rgb FromHsv(float hue, float saturation, float value, float alpha = 1f)
    {
        hue = ((hue % 1f) + 1f) % 1f;
        saturation = Math.Clamp(saturation, 0f, 1f);
        value = Math.Clamp(value, 0f, 1f);

        var sector = hue * 6f;
        var index = (int)MathF.Floor(sector) % 6;
        var fraction = sector - MathF.Floor(sector);

        var p = value * (1f - saturation);
        var q = value * (1f - (saturation * fraction));
        var t = value * (1f - (saturation * (1f - fraction)));

        return index switch
        {
            0 => new Rgb(value, t, p, alpha),
            1 => new Rgb(q, value, p, alpha),
            2 => new Rgb(p, value, t, alpha),
            3 => new Rgb(p, q, value, alpha),
            4 => new Rgb(t, p, value, alpha),
            _ => new Rgb(value, p, q, alpha),
        };
    }

    /// <summary>
    /// The same hue at a new brightness, optionally more or less saturated.
    /// </summary>
    /// <remarks>
    /// Alpha is forced to 1. Any transparency on the receiver is discarded,
    /// which is what the macOS build does and what every caller wants: these are
    /// surfaces, not overlays.
    /// </remarks>
    public Rgb WithBrightness(float brightness, float saturationMultiplier = 1f)
    {
        var (h, s, _) = ToHsv();
        return FromHsv(h, Math.Min(1f, s * saturationMultiplier), Math.Clamp(brightness, 0f, 1f));
    }

    /// <summary>
    /// Nudges this colour until it is legible on <paramref name="background"/>,
    /// keeping its hue so it still reads as the album's own colour.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Brightness moves first, in the direction the background allows: lighter
    /// on a dark ground, darker on a light one. Saturation only starts giving
    /// way once brightness has run out of room, which is what sends a colour
    /// toward white or black rather than leaving it stuck at a vivid tone that
    /// cannot be read.
    /// </para>
    /// <para>
    /// It gives up after twenty six steps and <b>may return a colour that never
    /// reached the target</b>. That is deliberate: a cover can be a single flat
    /// colour with no legible relative anywhere, and something close is a better
    /// answer than a loop.
    /// </para>
    /// </remarks>
    public Rgb Readable(Rgb background, float target = 4.5f)
    {
        var (h, s, v) = ToHsv();
        var lighten = background.Luminance() < 0.22f;
        var result = this;

        for (var step = 0; step < 26; step++)
        {
            // Note the first test is against the unmodified receiver: a colour
            // that is already legible is returned untouched.
            if (result.ContrastAgainst(background) >= target) return result;

            v = lighten ? Math.Min(1f, v + 0.045f) : Math.Max(0f, v - 0.045f);

            if (lighten && v > 0.995f) s = Math.Max(0f, s - 0.07f);
            if (!lighten && v < 0.005f) s = Math.Max(0f, s - 0.07f);

            result = FromHsv(h, s, v);
        }

        return result;
    }
}

using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// One blurred black rounded rectangle, drawn once and stretched under every
/// cover that needs a shadow.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because asking Skia for a drop shadow per cover per frame is
/// ruinous.</b> A shadow filter makes Skia allocate an offscreen layer the size
/// of the shape, draw into it, run a Gaussian over it, and composite it back.
/// Once is nothing. Thirty three times a frame, thirty times a second, on a
/// four thousand pixel wide surface, is tens of millions of blurred pixels a
/// second and a steady stream of garbage for the collector to clear up. The
/// result is not slow so much as <em>uneven</em>: most frames land and then one
/// does not, which reads as jumpiness rather than as a low frame rate.
/// </para>
/// <para>
/// A shadow is the same shape every time, only at different sizes. So it is
/// blurred once into a small bitmap and stretched, which costs one bitmap draw
/// per cover and allocates nothing at all.
/// </para>
/// </remarks>
internal sealed class ShadowSprite : IDisposable
{
    /// <summary>The side of the rounded rect inside the sprite.</summary>
    private const int Core = 128;

    /// <summary>
    /// Room around it for the blur to spread into. Too little and the shadow is
    /// cut off square at the edges, which is more obvious than no shadow at all.
    /// </summary>
    private const int Pad = 48;

    private const int Side = Core + (Pad * 2);

    /// <summary>The blur, as a fraction of the cover's width. From the specification.</summary>
    private const float BlurFraction = 0.06f;

    /// <summary>How far below the cover the shadow sits, as a fraction of its width.</summary>
    public const float DropFraction = 0.02f;

    /// <summary>Black at this much of the cover's own opacity.</summary>
    public const float Strength = 0.55f;

    private readonly Dictionary<int, SKBitmap> _byRadius = new();

    /// <summary>The sprite for a given corner radius, as a fraction of the side.</summary>
    public SKBitmap Get(float cornerFraction)
    {
        // Keyed to a thousandth, so a style asking for 3% every frame gets the
        // same bitmap rather than a new one each time.
        var key = (int)Math.Round(cornerFraction * 1000f);

        if (_byRadius.TryGetValue(key, out var cached)) return cached;

        var made = Render(cornerFraction);
        _byRadius[key] = made;
        return made;
    }

    private static SKBitmap Render(float cornerFraction)
    {
        var bitmap = new SKBitmap(new SKImageInfo(Side, Side, SKColorType.Bgra8888, SKAlphaType.Premul));

        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        // CoreGraphics states a shadow blur as a diameter, so the standard
        // deviation Skia wants is half of it.
        var sigma = Core * BlurFraction / 2f;

        using var blur = SKImageFilter.CreateBlur(sigma, sigma);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.Black,
            ImageFilter = blur,
        };

        var radius = Core * cornerFraction;
        canvas.DrawRoundRect(SKRect.Create(Pad, Pad, Core, Core), radius, radius, paint);

        return bitmap;
    }

    /// <summary>
    /// Where to stretch the sprite so its shadow falls correctly behind a cover.
    /// </summary>
    /// <remarks>
    /// The padding scales with the cover, so the blur stays the same fraction of
    /// the width at every size, which is what the specification asks for.
    /// </remarks>
    public static SKRect Placement(SKRect cover)
    {
        var grow = Pad * (cover.Width / Core);
        var drop = cover.Width * DropFraction;

        return SKRect.Create(
            cover.Left - grow,
            cover.Top - grow + drop,
            cover.Width + (grow * 2f),
            cover.Height + (grow * 2f));
    }

    public void Dispose()
    {
        foreach (var bitmap in _byRadius.Values) bitmap.Dispose();
        _byRadius.Clear();
    }
}

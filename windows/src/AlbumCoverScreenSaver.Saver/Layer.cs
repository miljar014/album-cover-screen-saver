using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// A piece of a picture that is identical every frame, drawn once into an image
/// and then blitted.
/// </summary>
/// <remarks>
/// <para>
/// <b>The second half of the lesson <see cref="ShadowSprite"/> is the first half
/// of.</b> A style is not slow because any one thing it draws is slow; it is
/// slow because it redraws the same unchanging thing thirty times a second. A CD
/// player's rainbow is sixty four arcs stroked at nearly half a radius, which at
/// four thousand pixels wide is tens of millions of antialiased pixels a frame,
/// and every one of those frames produces exactly the same picture. It turns at
/// eleven rpm, so the only thing that actually changes is the angle it is drawn
/// at.
/// </para>
/// <para>
/// The rule this encodes: if it does not change between frames, it should not be
/// computed between frames. Give it a key that captures everything it depends
/// on, and it is rebuilt when one of those changes and never otherwise.
/// </para>
/// <para>
/// The image is rendered at device pixels rather than at points, so a cached
/// layer on a 200% display is as sharp as one drawn directly. The callback is
/// handed a canvas already scaled to points, so it is written exactly as it
/// would be if it were drawing straight to the screen.
/// </para>
/// </remarks>
internal sealed class Layer : IDisposable
{
    private SKImage? _image;
    private string _key = "";

    /// <summary>How many times this layer has actually been rendered.</summary>
    /// <remarks>Logged by the styles that use it, so a key that is quietly
    /// changing every frame shows up as a number climbing rather than as a
    /// mysteriously slow style.</remarks>
    public int Builds { get; private set; }

    /// <summary>
    /// The cached image, rebuilding it if anything it depends on has changed.
    /// </summary>
    /// <param name="width">Width in points.</param>
    /// <param name="height">Height in points.</param>
    /// <param name="scale">Device pixels per point.</param>
    /// <param name="key">
    /// Everything the picture depends on. Get this wrong in one direction and
    /// the layer goes stale; wrong in the other and it rebuilds every frame and
    /// costs more than not caching at all.
    /// </param>
    public SKImage? Get(float width, float height, float scale, string key, Action<SKCanvas> paint)
    {
        var full = $"{key}|{width:0.#}x{height:0.#}@{scale:0.###}";

        if (_image is not null && _key == full) return _image;

        var pixelWidth = (int)Math.Ceiling(width * scale);
        var pixelHeight = (int)Math.Ceiling(height * scale);

        if (pixelWidth <= 0 || pixelHeight <= 0) return null;

        // A guard rather than a rule: something has gone wrong with the units if
        // a layer wants more than this, and an out of memory error inside a
        // screen saver is a black screen with no explanation.
        if ((long)pixelWidth * pixelHeight > 80_000_000L)
        {
            Log.Write($"layer {key} wanted {pixelWidth}x{pixelHeight} pixels, which is too big to cache");
            return null;
        }

        try
        {
            var info = new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);

            using var surface = SKSurface.Create(info);
            if (surface is null) return null;

            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.Scale(scale);

            paint(surface.Canvas);

            _image?.Dispose();
            _image = surface.Snapshot();
            _key = full;
            Builds++;

            return _image;
        }
        catch (Exception error)
        {
            Log.Write($"layer {key} failed: {error.Message}");
            return null;
        }
    }

    /// <summary>Device pixels per point, recovered from the canvas.</summary>
    /// <remarks>
    /// Renderers are handed a canvas already scaled for the display, so this is
    /// how a layer finds out what resolution to render itself at.
    /// </remarks>
    public static float ScaleOf(SKCanvas canvas)
    {
        var scale = canvas.TotalMatrix.ScaleY;
        return scale > 0f ? scale : 1f;
    }

    public void Dispose()
    {
        _image?.Dispose();
        _image = null;
    }
}

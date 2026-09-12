using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// Heavily blurred album art, cached per album, target size and radius.
/// </summary>
/// <remarks>
/// <para>
/// Blurring every frame would be far too slow, and blurring at full resolution
/// is wasted work: at these radii the result is indistinguishable from blurring
/// a thumbnail and scaling it up, which is what this does. The art is reduced to
/// a 480 pixel square first, so a 10 pixel blur there is a 40 pixel blur once it
/// is stretched across a 4K screen.
/// </para>
/// <para>
/// <b>The cache key includes the target and the radius, not just the album.</b>
/// Two styles can ask for two different blurs of the same cover, and an
/// album-only key hands the first one's result to whoever asks second. Only one
/// style needs this today, which is exactly why the mistake would sit unnoticed
/// until the second one arrives.
/// </para>
/// </remarks>
internal sealed class BlurStore : IDisposable
{
    /// <summary>
    /// Enough for a handful of albums at one setting. The backdrop only ever
    /// shows what is playing and what was playing a moment ago.
    /// </summary>
    private const int Capacity = 12;

    private readonly Dictionary<string, SKBitmap> _blurred = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _order = new();

    public int Count => _blurred.Count;

    /// <summary>
    /// The blurred cover, or null if it cannot be made.
    /// </summary>
    /// <param name="albumId">Part of the cache key, not used to load anything.</param>
    /// <param name="image">The decoded cover.</param>
    /// <param name="target">The longest edge the art is reduced to before blurring.</param>
    /// <param name="radius">The Gaussian radius, in those reduced pixels.</param>
    public SKBitmap? Blurred(string albumId, SKBitmap? image, int target, int radius)
    {
        if (image is null || image.Width <= 0 || image.Height <= 0) return null;

        var key = $"{albumId}|{target}|{radius}";

        if (_blurred.TryGetValue(key, out var cached))
        {
            Touch(key);
            return cached;
        }

        SKBitmap? made = null;
        try
        {
            made = Build(image, target, radius);
        }
        catch (Exception error)
        {
            Log.Write($"blurring {albumId} failed: {error.Message}");
        }

        if (made is null) return null;

        _blurred[key] = made;
        _order.AddLast(key);
        Evict();

        return made;
    }

    /// <summary>
    /// Downsample, then a true Gaussian.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Skia's blur takes a standard deviation, which is what CoreImage's
    /// inputRadius is on the macOS side, so the number carries across directly
    /// and the two platforms produce the same wash from the same setting.
    /// </para>
    /// <para>
    /// <b>The overdraw is the edge mode.</b> A Gaussian at the border of an
    /// image samples past its edge, finds nothing there, and fades to
    /// transparent, which shows as a dark halo creeping in from the corners.
    /// Rather than depending on a clamped sampling mode, the art is drawn
    /// larger than the bitmap it lands in, by two radii on every side, so the
    /// pixels the blur reaches for at the edges are real cover rather than
    /// nothing. What it costs is a slightly tighter crop of the art, and at
    /// this much blur no one could tell either way.
    /// </para>
    /// </remarks>
    private static SKBitmap? Build(SKBitmap image, int target, int radius)
    {
        var longest = Math.Max(image.Width, image.Height);
        if (longest <= 0) return null;

        var scale = (float)target / longest;
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var blurred = new SKBitmap(info);

        var overdraw = radius * 2f;
        var into = SKRect.Create(-overdraw, -overdraw, width + (overdraw * 2f), height + (overdraw * 2f));

        using (var canvas = new SKCanvas(blurred))
        using (var blur = SKImageFilter.CreateBlur(radius, radius))
        using (var paint = new SKPaint
        {
            IsAntialias = true,
            FilterQuality = SKFilterQuality.High,
            ImageFilter = blur,
        })
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawBitmap(image, into, paint);
        }

        return blurred;
    }

    private void Touch(string key)
    {
        _order.Remove(key);
        _order.AddLast(key);
    }

    private void Evict()
    {
        while (_blurred.Count > Capacity && _order.First is { } oldest)
        {
            var key = oldest.Value;
            _order.RemoveFirst();
            if (_blurred.Remove(key, out var bitmap)) bitmap.Dispose();
        }
    }

    /// <summary>
    /// Dropped when the saver stops, not merely when it is destroyed. These hold
    /// unmanaged memory that the garbage collector is in no hurry about.
    /// </summary>
    public void Purge()
    {
        foreach (var bitmap in _blurred.Values) bitmap.Dispose();
        _blurred.Clear();
        _order.Clear();
    }

    public void Dispose() => Purge();
}

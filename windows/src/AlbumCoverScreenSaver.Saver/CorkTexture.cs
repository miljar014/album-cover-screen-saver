using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// A cork board, drawn once into a bitmap and kept.
/// </summary>
/// <remarks>
/// <para>
/// Cork needs thousands of granules to read as cork rather than as noise. At a
/// normal screen size that is about five thousand ellipses, plus seven hundred
/// pits and five hundred flecks, and drawing that every frame would cost more
/// than the whole rest of the scene put together. So it is generated once and
/// cached, and only regenerated when the surface actually changes size.
/// </para>
/// <para>
/// The layer order matters, because each one is composited over the last: a
/// base wash, broad mottling, granules, pits, then pale flecks. The mottling is
/// the layer people forget, and without it cork reads as flat no matter how much
/// fine detail is piled on top. The pits and flecks are the high-contrast specks
/// an eye uses to decide a surface is granular rather than printed.
/// </para>
/// </remarks>
internal sealed class CorkTexture : IDisposable
{
    private SKBitmap? _cached;
    private float _cachedWidth;
    private float _cachedHeight;

    /// <summary>
    /// The board at this size, generating it if the cached one is the wrong
    /// shape.
    /// </summary>
    /// <remarks>
    /// Two points of tolerance on each dimension, so a window that reports a
    /// sub-pixel difference between frames does not send five thousand ellipses
    /// through a redraw.
    /// </remarks>
    public SKBitmap? Get(float width, float height, Random random)
    {
        if (_cached is not null &&
            Math.Abs(_cachedWidth - width) < 2f &&
            Math.Abs(_cachedHeight - height) < 2f)
        {
            return _cached;
        }

        try
        {
            _cached?.Dispose();
            _cached = Generate(Math.Max(64, (int)width), Math.Max(64, (int)height), random);
            _cachedWidth = width;
            _cachedHeight = height;
        }
        catch (Exception error)
        {
            Log.Failure("generating the cork texture", error);
            _cached = null;
        }

        return _cached;
    }

    private static SKBitmap Generate(int width, int height, Random random)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var board = new SKBitmap(info);

        using var canvas = new SKCanvas(board);
        var whole = SKRect.Create(0, 0, width, height);

        float Between(double low, double high) => (float)(low + (random.NextDouble() * (high - low)));

        // 1. The base. A near-vertical gradient, light at the top.
        using (var paint = new SKPaint { IsAntialias = true })
        {
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(width * 0.09f, 0f),
                new SKPoint(width * 0.91f, height),
                new[] { Colour(0.71f, 0.55f, 0.33f), Colour(0.56f, 0.41f, 0.23f) },
                SKShaderTileMode.Clamp);

            canvas.DrawRect(whole, paint);
            paint.Shader?.Dispose();
        }

        // 2. Broad tonal mottling. Real cork is blotchy at a far larger scale
        // than its granules, and without this the board reads as flat however
        // much fine detail goes on top of it.
        var shortest = Math.Min(width, height);
        for (var i = 0; i < 70; i++)
        {
            var radius = Between(shortest * 0.07, shortest * 0.30);
            var centre = new SKPoint(Between(0, width), Between(0, height));
            var light = random.Next(2) == 0;

            var tone = light ? Colour(0.85f, 0.70f, 0.46f) : Colour(0.36f, 0.24f, 0.12f);
            var alpha = light ? 0.10f : 0.13f;

            using var paint = new SKPaint { IsAntialias = true };
            paint.Shader = SKShader.CreateRadialGradient(
                centre, radius,
                new[] { tone.WithAlpha((byte)(alpha * 255f)), tone.WithAlpha(0) },
                SKShaderTileMode.Clamp);

            canvas.DrawCircle(centre, radius, paint);
            paint.Shader?.Dispose();
        }

        // 3. Granules. Hung off the left and bottom edges rather than stopping
        // short of them, or the board has a visible clean border.
        var granules = width * height / 420;
        using (var paint = new SKPaint { IsAntialias = true })
        {
            for (var i = 0; i < granules; i++)
            {
                var w = Between(2.5, 11.0);
                var h = w * Between(0.45, 1.6);
                var x = Between(-w, width);
                var y = Between(-h, height);

                var t = (float)random.NextDouble();
                paint.Color = Colour(0.42f + (0.42f * t), 0.30f + (0.36f * t), 0.15f + (0.26f * t))
                    .WithAlpha((byte)(Between(0.10, 0.34) * 255f));

                canvas.DrawOval(SKRect.Create(x, y, w, h), paint);
            }
        }

        // 4. Pits, and 5. pale flecks. These are the high-contrast specks an eye
        // uses to judge that a surface is granular rather than printed.
        using (var paint = new SKPaint { IsAntialias = true })
        {
            for (var i = 0; i < granules / 7; i++)
            {
                var r = Between(1.0, 3.4);
                paint.Color = Colour(0.20f, 0.13f, 0.06f)
                    .WithAlpha((byte)(Between(0.25, 0.55) * 255f));

                canvas.DrawOval(SKRect.Create(Between(0, width), Between(0, height), r, r * Between(0.7, 1.4)), paint);
            }

            for (var i = 0; i < granules / 9; i++)
            {
                var r = Between(1.0, 3.0);
                paint.Color = Colour(0.93f, 0.83f, 0.62f)
                    .WithAlpha((byte)(Between(0.14, 0.36) * 255f));

                canvas.DrawOval(SKRect.Create(Between(0, width), Between(0, height), r, r), paint);
            }
        }

        return board;
    }

    private static SKColor Colour(float r, float g, float b) => new(
        (byte)Math.Clamp(r * 255f, 0f, 255f),
        (byte)Math.Clamp(g * 255f, 0f, 255f),
        (byte)Math.Clamp(b * 255f, 0f, 255f));

    public void Purge()
    {
        _cached?.Dispose();
        _cached = null;
        _cachedWidth = 0;
        _cachedHeight = 0;
    }

    public void Dispose() => Purge();
}

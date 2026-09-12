using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// The brightness grid a cover is printed from, worked out once per album.
/// </summary>
/// <remarks>
/// Forty six by forty six values, which is a newspaper screen rather than a
/// photograph. Cheap enough to keep for every album that has been on the page.
/// </remarks>
internal sealed class HalftoneStore
{
    private readonly Dictionary<string, float[]> _grids = new(StringComparer.Ordinal);

    public int Count => _grids.Count;

    /// <summary>The grid for an album, or null if the cover will not decode.</summary>
    public float[]? For(string albumId, SKBitmap? image)
    {
        if (string.IsNullOrEmpty(albumId)) return null;

        if (_grids.TryGetValue(albumId, out var cached)) return cached;

        var grid = Build(image);
        if (grid is null) return null;

        _grids[albumId] = grid;
        return grid;
    }

    private static float[]? Build(SKBitmap? image)
    {
        if (image is null || image.Width <= 0 || image.Height <= 0) return null;

        try
        {
            const int n = Halftone.Grid;
            var info = new SKImageInfo(n, n, SKColorType.Rgba8888, SKAlphaType.Unpremul);

            using var small = new SKBitmap(info);
            using (var canvas = new SKCanvas(small))
            using (var paint = new SKPaint { FilterQuality = SKFilterQuality.Medium })
            {
                canvas.Clear(SKColors.White);
                canvas.DrawBitmap(image, SKRect.Create(0, 0, n, n), paint);
            }

            var grid = new float[n * n];

            for (var row = 0; row < n; row++)
            {
                for (var column = 0; column < n; column++)
                {
                    var pixel = small.GetPixel(column, row);
                    grid[(row * n) + column] = Halftone.Luma(pixel.Red, pixel.Green, pixel.Blue);
                }
            }

            return grid;
        }
        catch (Exception error)
        {
            Log.Write($"halftone failed: {error.Message}");
            return null;
        }
    }

    public void Purge() => _grids.Clear();
}

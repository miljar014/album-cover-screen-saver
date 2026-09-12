using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// The palette for each album, worked out once and remembered.
/// </summary>
/// <remarks>
/// <para>
/// The extraction itself is in the shared library and needs no drawing at all.
/// This is the half that does: reducing a cover to a 28 by 28 square and reading
/// the pixels out of it.
/// </para>
/// <para>
/// <b>A failure is cached too.</b> A cover that will not decode keeps its
/// fallback palette until the cache is purged, rather than being retried on
/// every frame. That is the macOS behaviour and it is the right trade: the
/// alternative is a per-frame decode attempt on a file that is not going to
/// start working.
/// </para>
/// </remarks>
internal sealed class PaletteStore
{
    private readonly Dictionary<string, ArtPalette> _palettes = new(StringComparer.Ordinal);

    public int Count => _palettes.Count;

    public ArtPalette For(string albumId, SKBitmap? image)
    {
        if (string.IsNullOrEmpty(albumId)) return ArtPalette.Fallback;

        if (_palettes.TryGetValue(albumId, out var cached)) return cached;

        var palette = Extract(image) ?? ArtPalette.Fallback;
        _palettes[albumId] = palette;
        return palette;
    }

    /// <summary>
    /// Squashes the cover down to 28 by 28 and reads every pixel.
    /// </summary>
    /// <remarks>
    /// Small on purpose. The palette is a handful of averages and one best
    /// pixel, and 784 samples settle those as well as a million would while
    /// costing nothing. The size also stops one very detailed cover from being
    /// slower to open than a plain one.
    /// </remarks>
    private static ArtPalette? Extract(SKBitmap? image)
    {
        if (image is null || image.Width <= 0 || image.Height <= 0) return null;

        try
        {
            const int side = ArtPalette.SampleSide;
            var info = new SKImageInfo(side, side, SKColorType.Rgba8888, SKAlphaType.Unpremul);

            using var small = new SKBitmap(info);
            using (var canvas = new SKCanvas(small))
            using (var paint = new SKPaint { FilterQuality = SKFilterQuality.Medium })
            {
                canvas.Clear(SKColors.Black);
                canvas.DrawBitmap(image, SKRect.Create(0, 0, side, side), paint);
            }

            var pixels = new float[side * side * 3];
            var at = 0;

            for (var y = 0; y < side; y++)
            {
                for (var x = 0; x < side; x++)
                {
                    // Alpha is ignored throughout: covers are opaque, and a
                    // stray transparent pixel should not drag the average.
                    var colour = small.GetPixel(x, y);
                    pixels[at++] = colour.Red / 255f;
                    pixels[at++] = colour.Green / 255f;
                    pixels[at++] = colour.Blue / 255f;
                }
            }

            return ArtPalette.Build(pixels);
        }
        catch (Exception error)
        {
            Log.Write($"palette extraction failed: {error.Message}");
            return null;
        }
    }

    public void Purge() => _palettes.Clear();
}

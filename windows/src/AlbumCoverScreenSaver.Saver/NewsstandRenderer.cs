using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// Newsstand. The album as the front page of a broadsheet: the artist is the
/// paper's name, the track is the headline, and the cover is a halftone
/// photograph.
/// </summary>
/// <remarks>
/// <para>
/// The page is monochrome on purpose, as a broadsheet is. The only album colour
/// anywhere on it is a thin bar at the very bottom, which is the sort of thing a
/// printer adds so the press operator can see the ink is running.
/// </para>
/// <para>
/// The columns of body text are not text. They are bars of varying length, with
/// every ninth one darker to suggest a new paragraph, which is what makes a
/// block of them read as prose rather than as a barcode. Real text would have to
/// be legible, and would then have to say something.
/// </para>
/// </remarks>
internal sealed class NewsstandRenderer : IStyleRenderer, IDisposable
{
    private static readonly SKColor Paper = new(237, 232, 217);
    private static readonly SKColor Ink = new(28, 26, 23);
    private static readonly SKColor Foxing = new(191, 179, 148, 13);

    private readonly SaverData _data;
    private readonly AlbumPicker _picker;
    private readonly PaletteStore _palettes;
    private readonly HalftoneStore _halftones;

    private readonly Featured _featured;
    private readonly SKPaint _fill = new() { IsAntialias = true };
    private readonly SKPaint _blit = new() { IsAntialias = true, FilterQuality = SKFilterQuality.Medium };

    // Nothing on this page moves. Between one album and the next it is the same
    // picture, and it was being redrawn thirty times a second: ninety blotches
    // of foxing, two thousand one hundred and sixteen halftone dots, and all the
    // type. The page cuts rather than crossfades, so rebuilding it at the cut
    // costs one frame nobody can see.
    private readonly Layer _page = new();

    public NewsstandRenderer(
        SaverData data, AlbumPicker picker, PaletteStore palettes, HalftoneStore halftones)
    {
        _data = data;
        _picker = picker;
        _palettes = palettes;
        _halftones = halftones;
        _featured = new Featured(picker, data.Settings.RecencyBias);
    }

    public void BuildLayout(float width, float height)
    {
        if (_data.Albums.Count == 0) return;

        _featured.Reset(0, _picker.Pick(_data.Albums.Count, _data.Settings.RecencyBias));
        Log.Write($"newsstand layout: {width:0}x{height:0} points");
    }

    public void Advance(double phase, float width, float height) =>
        _featured.Advance(phase, _data.LiveAlbumIndex, _data.Settings.FeatureSeconds, _data.Albums.Count);

    public void Draw(SKCanvas canvas, float width, float height, double phase)
    {
        var albums = _data.Albums;
        if (albums.Count == 0) return;

        var index = Math.Clamp(_featured.Index, 0, albums.Count - 1);
        var live = _data.LiveAlbumIndex == index;

        var cached = _page.Get(
            width, height, Layer.ScaleOf(canvas), $"page|{albums[index].Id}|{live}",
            surface => PaintPage(surface, width, height, index));

        if (cached is not null)
        {
            canvas.DrawImage(cached, SKRect.Create(0, 0, width, height), _blit);
            return;
        }

        PaintPage(canvas, width, height, index);
    }

    private void PaintPage(SKCanvas canvas, float width, float height, int index)
    {
        var albums = _data.Albums;
        var palette = _palettes.For(albums[index].Id, _data.ImageFor(index));
        var (title, artist, sub) = FeaturedText.For(_data, index);

        var margin = width * 0.06f;
        var columnWidth = width - (margin * 2f);

        DrawPaper(canvas, width, height);

        var top = height * 0.055f;

        // The masthead: the artist is the paper.
        var mastSize = TextLayout.Fitted(
            artist.ToUpperInvariant(), columnWidth, height * 0.12f, height * 0.095f, bold: true);

        using (var mast = TextLayout.Paint(mastSize, true, Ink))
        {
            mast.TextAlign = SKTextAlign.Left;
            top += TextLayout.Draw(
                canvas, artist.ToUpperInvariant(), margin, top, columnWidth, mast,
                centred: true, tracking: 2f);
        }

        top += height * 0.012f;
        Rule(canvas, margin, top, columnWidth, 3f);
        top += height * 0.016f;

        DrawDateline(canvas, margin, top, columnWidth, height, index);
        top += height * 0.028f;

        Rule(canvas, margin, top, columnWidth, 1f);
        top += height * 0.030f;

        var headSize = TextLayout.Fitted(
            title, columnWidth, height * 0.17f, height * 0.075f, bold: true);

        using (var headline = TextLayout.Paint(headSize, true, Ink))
        {
            top += TextLayout.Draw(canvas, title, margin, top, columnWidth, headline);
        }

        top += height * 0.020f;
        Rule(canvas, margin, top, columnWidth, 1f);
        top += height * 0.028f;

        // The photograph takes the width the design asks for, or whatever the
        // page can still afford by the time it gets here, whichever is less.
        var pageBottom = height * NewsstandPage.BottomFraction;
        var photoSide = NewsstandPage.PhotoSide(columnWidth, height, top);
        var photo = SKRect.Create(margin, top, photoSide, photoSide);

        DrawPhotograph(canvas, photo, index);
        DrawCaption(canvas, photo, height, string.IsNullOrEmpty(sub) ? albums[index].Name : sub);
        DrawColumns(canvas, width, height, margin, columnWidth, photo, top, pageBottom);

        // The one hint of the album's own colour on the whole page.
        _fill.Shader = null;
        _fill.Color = palette.Accent.ToSkia();
        canvas.DrawRect(SKRect.Create(margin, height * 0.959f, columnWidth, height * 0.006f), _fill);
    }

    /// <summary>The paper, and ninety aged blotches on it.</summary>
    /// <remarks>
    /// Seeded from their own index rather than drawn at random each frame, so
    /// the foxing stays where it is instead of crawling. They are flat and
    /// overlapping, and the overlaps are what give the mottling.
    /// </remarks>
    private void DrawPaper(SKCanvas canvas, float width, float height)
    {
        _fill.Shader = null;
        _fill.Color = Paper;
        canvas.DrawRect(SKRect.Create(0, 0, width, height), _fill);

        _fill.Color = Foxing;

        for (var i = 0; i < 90; i++)
        {
            var x = (float)Noise.Hash01(i) * width;
            var y = (float)Noise.Fraction(Math.Sin(i * 78.233) * 12345.6789) * height;
            var radius = ((float)Noise.Fraction(Math.Sin(i * 4.117) * 991.7) * height * 0.05f) + 4f;

            canvas.DrawCircle(x, y, radius, _fill);
        }
    }

    private void Rule(SKCanvas canvas, float left, float top, float width, float thickness)
    {
        _fill.Shader = null;
        _fill.Color = Ink.WithAlpha(217);
        canvas.DrawRect(SKRect.Create(left, top, width, thickness), _fill);
    }

    private void DrawDateline(
        SKCanvas canvas, float margin, float top, float columnWidth, float height, int index)
    {
        var live = _data.LiveAlbumIndex == index;
        var line = live ? "LATE EDITION  ·  NOW PLAYING" : "FROM THE ARCHIVE  ·  COLLECTED EDITION";

        using var paint = new SKPaint
        {
            IsAntialias = true,
            TextSize = Math.Max(9f, height * 0.016f),
            Color = Ink,
            TextAlign = SKTextAlign.Left,
            // The one place a serif face appears, alongside the caption. The
            // masthead and headline go through the shared fitted font, which is
            // the geometric face, and that inconsistency is in the Mac too.
            Typeface = SKTypeface.FromFamilyName(
                           "Bodoni MT", SKFontStyleWeight.Normal,
                           SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                       ?? SKTypeface.FromFamilyName("Times New Roman", SKFontStyleWeight.Normal,
                           SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                       ?? TextLayout.Face(bold: false),
        };

        TextLayout.Draw(canvas, line, margin, top, columnWidth, paint, centred: true, tracking: 3f);
    }

    /// <summary>
    /// The cover, printed as dots.
    /// </summary>
    /// <remarks>
    /// Row zero is the top row and there is no flip. See the note on
    /// <see cref="Halftone.CellOrigin"/>: the Mac needs one and Windows must not
    /// have one, and porting it across would put the photograph upside down.
    /// </remarks>
    private void DrawPhotograph(SKCanvas canvas, SKRect photo, int index)
    {
        var albums = _data.Albums;
        var grid = _halftones.For(albums[index].Id, _data.ImageFor(index));

        if (grid is not null)
        {
            var cell = photo.Width / Halftone.Grid;

            _fill.Shader = null;
            _fill.Color = Ink.WithAlpha(235);

            for (var row = 0; row < Halftone.Grid; row++)
            {
                for (var column = 0; column < Halftone.Grid; column++)
                {
                    var dot = Halftone.DotFor(grid[(row * Halftone.Grid) + column], cell);
                    if (!dot.Print) continue;

                    var (x, y) = Halftone.CellOrigin(row, column, cell, dot.Diameter);

                    canvas.DrawCircle(
                        photo.Left + x + (dot.Diameter / 2f),
                        photo.Top + y + (dot.Diameter / 2f),
                        dot.Diameter / 2f, _fill);
                }
            }
        }

        _fill.Color = Ink.WithAlpha(230);
        _fill.Style = SKPaintStyle.Stroke;
        _fill.StrokeWidth = 1.5f;
        canvas.DrawRect(photo, _fill);
        _fill.Style = SKPaintStyle.Fill;
    }

    private void DrawCaption(SKCanvas canvas, SKRect photo, float height, string caption)
    {
        if (string.IsNullOrEmpty(caption)) return;

        using var paint = new SKPaint
        {
            IsAntialias = true,
            TextSize = Math.Max(9f, height * 0.016f),
            Color = Ink.WithAlpha(191),
            TextAlign = SKTextAlign.Left,
            Typeface = SKTypeface.FromFamilyName(
                           "Bodoni MT", SKFontStyleWeight.Normal,
                           SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                       ?? SKTypeface.FromFamilyName("Times New Roman", SKFontStyleWeight.Normal,
                           SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                       ?? TextLayout.Face(bold: false),
        };

        TextLayout.Draw(
            canvas, caption, photo.Left, photo.Bottom + (height * 0.008f), photo.Width, paint);
    }

    /// <summary>
    /// Two columns of suggested text beside the photograph.
    /// </summary>
    /// <remarks>
    /// Bars, not words. Every ninth one is darker, which is the paragraph cue
    /// that makes the block read as prose instead of as a barcode. Real text
    /// here would have to be legible, and then it would have to say something.
    /// </remarks>
    private void DrawColumns(
        SKCanvas canvas, float width, float height, float margin, float columnWidth,
        SKRect photo, float top, float pageBottom)
    {
        var left = photo.Right + (columnWidth * 0.05f);
        var available = width - margin - left;
        if (available <= 0) return;

        var gutter = available * 0.06f;
        var each = (available - gutter) / 2f;
        var thickness = Math.Max(1f, height * 0.0035f);

        // The columns run to the foot of the page rather than to the bottom of
        // the photograph. Tied to the photograph they would leave a blank
        // quarter of the page whenever the photograph had to be cut down.
        var stop = pageBottom;

        _fill.Shader = null;

        for (var column = 0; column < 2; column++)
        {
            var x = left + (column * (each + gutter));
            var y = top + (height * 0.006f);
            var line = 0;

            while (y < stop)
            {
                var noise = (float)Noise.Hash01((line * 7) + (column * 31));

                _fill.Color = Ink.WithAlpha((byte)(line % 9 == 0 ? 140 : 77));
                canvas.DrawRect(
                    SKRect.Create(x, y, each * (0.55f + (noise * 0.45f)), thickness), _fill);

                y += height * 0.0115f;
                line++;
            }
        }
    }

    public void Dispose()
    {
        _fill.Dispose();
        _blit.Dispose();
        _page.Dispose();
    }
}

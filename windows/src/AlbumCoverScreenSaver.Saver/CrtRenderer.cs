using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// CRT Terminal. A phosphor tube with the cover printed on it in dots and the
/// track typed out beside it a character at a time.
/// </summary>
/// <remarks>
/// <para>
/// The cover is never pasted in. It goes through the same halftone grid as
/// Newsstand, inverted: there the dots are ink on paper, here they are light on
/// a dark tube. A full-colour photograph on a green screen would give the whole
/// thing away in the first frame.
/// </para>
/// <para>
/// The track label setting is deliberately not read. On every other style the
/// words are an addition to the picture and can be turned off; here the words
/// are the style, and there would be nothing left of it.
/// </para>
/// </remarks>
internal sealed class CrtRenderer : IStyleRenderer, IDisposable
{
    private static readonly SKColor Tube = new(5, 8, 5);
    private static readonly SKColor Amber = new(255, 184, 66);
    private static readonly SKColor Green = new(97, 255, 122);

    private readonly SaverData _data;
    private readonly AlbumPicker _picker;
    private readonly HalftoneStore _halftones;

    private readonly Featured _featured;
    private readonly SKPaint _fill = new() { IsAntialias = true };

    // The overlays are the same every frame for a given screen, so they are
    // built once. Rebuilding a gradient shader thirty times a second is exactly
    // the sort of per-frame allocation that showed up as stutter in Drifting
    // Float.
    private SKShader? _glow;
    private SKShader? _vignette;
    private SKShader? _scanlines;
    private SKBitmap? _scanTile;
    private SKPaint? _big;
    private SKPaint? _small;

    // Two thousand one hundred and sixteen circles, redrawn every frame for a
    // picture that only changes when the album does. The fade multiplies every
    // dot's brightness by the same amount, so the grid is rendered at full
    // brightness and the whole image is blitted at the fade instead.
    private readonly Layer _portrait = new();
    private readonly SKPaint _blit = new() { IsAntialias = true, FilterQuality = SKFilterQuality.Medium };
    private float _builtFor;
    private float _builtHeight;
    private float _builtScale;
    private bool _builtAmber;

    public CrtRenderer(SaverData data, AlbumPicker picker, HalftoneStore halftones)
    {
        _data = data;
        _picker = picker;
        _halftones = halftones;
        _featured = new Featured(picker, data.Settings.RecencyBias);
    }

    private SKColor Phosphor => _data.Settings.CrtAmber ? Amber : Green;

    public void BuildLayout(float width, float height)
    {
        if (_data.Albums.Count == 0) return;

        _featured.Reset(0, _picker.Pick(_data.Albums.Count, _data.Settings.RecencyBias));
        Log.Write($"crt layout: {width:0}x{height:0} points");
    }

    public void Advance(double phase, float width, float height) =>
        _featured.Advance(phase, _data.LiveAlbumIndex, _data.Settings.FeatureSeconds, _data.Albums.Count);

    public void Draw(SKCanvas canvas, float width, float height, double phase)
    {
        var albums = _data.Albums;
        if (albums.Count == 0) return;

        var index = Math.Clamp(_featured.Index, 0, albums.Count - 1);
        var live = _data.LiveAlbumIndex == index;

        // The canvas is already scaled to points, so this recovers the number of
        // device pixels in a point. The scanlines are the one thing on the
        // screen measured in pixels rather than points.
        var scale = canvas.TotalMatrix.ScaleY;
        if (scale <= 0f) scale = 1f;

        Rebuild(width, height, scale);

        _fill.Shader = null;
        _fill.Color = Tube;
        canvas.DrawRect(SKRect.Create(0, 0, width, height), _fill);

        Wash(canvas, width, height, _glow);

        var side = CrtTerminal.PortraitSide(width, height);
        var portrait = SKRect.Create(width * 0.10f, (height * 0.5f) - (side / 2f), side, side);

        DrawPortrait(canvas, portrait, index, _featured.Fade(phase), scale);
        DrawReadout(canvas, width, height, side, index, live, phase);

        Wash(canvas, width, height, _scanlines);
        Wash(canvas, width, height, _vignette);
    }

    private void Wash(SKCanvas canvas, float width, float height, SKShader? shader)
    {
        if (shader is null) return;

        _fill.Shader = shader;
        _fill.Color = SKColors.White;
        canvas.DrawRect(SKRect.Create(0, 0, width, height), _fill);
        _fill.Shader = null;
    }

    /// <summary>
    /// The cover, printed in light.
    /// </summary>
    /// <remarks>
    /// Row zero is the top row and there is no flip. The note on
    /// <see cref="Halftone.CellOrigin"/> is the long version: the Mac needs a
    /// flip and Windows must not have one, and this is the second of the two
    /// call sites that have to agree about it.
    /// </remarks>
    private void DrawPortrait(SKCanvas canvas, SKRect portrait, int index, float fade, float scale)
    {
        var albumId = _data.Albums[index].Id;

        var grid = _halftones.For(albumId, _data.ImageFor(index));
        if (grid is null) return;

        var side = portrait.Width;
        var phosphor = Phosphor;

        var image = _portrait.Get(
            side, side, scale, $"crt|{albumId}|{_data.Settings.CrtAmber}|{side:0.#}",
            surface => PaintDots(surface, grid, side, phosphor));

        if (image is null)
        {
            PaintDots(canvas, grid, side, phosphor, portrait.Left, portrait.Top);
            return;
        }

        _blit.Color = SKColors.White.WithAlpha((byte)Math.Clamp(fade * 255f, 0f, 255f));
        canvas.DrawImage(image, portrait, _blit);
    }

    private static void PaintDots(
        SKCanvas canvas, float[] grid, float side, SKColor phosphor,
        float originX = 0f, float originY = 0f)
    {
        var cell = side / Halftone.Grid;

        using var paint = new SKPaint { IsAntialias = true };

        for (var row = 0; row < Halftone.Grid; row++)
        {
            for (var column = 0; column < Halftone.Grid; column++)
            {
                var dot = CrtTerminal.DotFor(grid[(row * Halftone.Grid) + column], cell, 1f);
                if (!dot.Lit) continue;

                var (x, y) = Halftone.CellOrigin(row, column, cell, dot.Diameter);

                paint.Color = phosphor.WithAlpha((byte)Math.Clamp(dot.Alpha * 255f, 0f, 255f));

                canvas.DrawCircle(
                    originX + x + (dot.Diameter / 2f),
                    originY + y + (dot.Diameter / 2f),
                    dot.Diameter / 2f, paint);
            }
        }
    }

    /// <summary>
    /// The typed column, as far as it has got.
    /// </summary>
    /// <remarks>
    /// Nothing below an unfinished line is drawn. The cursor sits at the end of
    /// whatever is still being typed, and once the whole block is out it drops
    /// to the line below and blinks there.
    /// </remarks>
    private void DrawReadout(
        SKCanvas canvas, float width, float height, float side, int index, bool live, double phase)
    {
        var (title, artist, sub) = FeaturedText.For(_data, index);

        var left = CrtTerminal.ReadoutLeft(width, side);
        var columnWidth = CrtTerminal.ReadoutWidth(width, left);
        if (columnWidth <= 0f) return;

        var lines = CrtTerminal.Lines(title, artist, live ? sub : string.Empty, live);
        var transcript = CrtTerminal.Reveal(lines, CrtTerminal.Budget(phase, _featured.ShownSince));

        if (_big is null || _small is null) return;

        var bigSize = _big.TextSize;
        var phosphor = Phosphor;
        var top = CrtTerminal.BlockTop(height, lines.Count);

        foreach (var shown in transcript.Lines)
        {
            var line = lines[shown.Index];
            var paint = line.Small ? _small : _big;

            paint.Color = phosphor.WithAlpha((byte)Math.Clamp(line.Brightness * 255f, 0f, 255f));

            // A blank line is drawn as a space so that it still takes up a
            // line's height. Without that the pauses in the typing would close
            // up and the block would shuffle upward as it went.
            var used = TextLayout.Draw(
                canvas, shown.Text.Length == 0 ? " " : shown.Text, left, top, columnWidth, paint);

            if (shown.Partial)
            {
                Cursor(canvas, left + TextLayout.Width(shown.Text, paint) + 2f,
                    top + used - (used * 0.18f) - (paint.TextSize * 0.9f), paint.TextSize, phosphor);
                return;
            }

            top += used + (height * 0.010f);
        }

        // Everything is out, so the cursor drops to the next line and waits.
        if (CrtTerminal.CursorVisible(phase))
        {
            Cursor(canvas, left, top + (bigSize * 0.1f), bigSize, phosphor);
        }
    }

    private void Cursor(SKCanvas canvas, float x, float top, float size, SKColor phosphor)
    {
        _fill.Shader = null;
        _fill.Color = phosphor.WithAlpha(230);
        canvas.DrawRect(SKRect.Create(x, top, size * 0.55f, size * 0.9f), _fill);
    }

    /// <summary>
    /// A monospaced face, because a terminal that was not monospaced would be a
    /// word processor.
    /// </summary>
    private static SKPaint Mono(float size, bool medium) => new()
    {
        IsAntialias = true,
        TextSize = size,
        TextAlign = SKTextAlign.Left,
        Typeface = SKTypeface.FromFamilyName(
                       "Consolas",
                       medium ? SKFontStyleWeight.Medium : SKFontStyleWeight.Normal,
                       SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                   ?? SKTypeface.FromFamilyName(
                       "Courier New",
                       medium ? SKFontStyleWeight.Medium : SKFontStyleWeight.Normal,
                       SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                   ?? SKTypeface.Default,
    };

    /// <summary>
    /// The three overlays, rebuilt only when the screen or its scaling changes.
    /// </summary>
    private void Rebuild(float width, float height, float scale)
    {
        // The glow takes its colour from the phosphor, so switching between
        // amber and green has to rebuild it too. Settings are re-read every
        // three seconds while the saver runs.
        if (_glow is not null
            && Math.Abs(_builtFor - width) < 0.5f
            && Math.Abs(_builtHeight - height) < 0.5f
            && Math.Abs(_builtScale - scale) < 0.01f
            && _builtAmber == _data.Settings.CrtAmber)
        {
            return;
        }

        Release();

        _builtFor = width;
        _builtHeight = height;
        _builtScale = scale;
        _builtAmber = _data.Settings.CrtAmber;

        var centre = new SKPoint(width / 2f, height / 2f);
        var shortest = Math.Min(width, height);
        var longest = Math.Max(width, height);
        var phosphor = Phosphor;

        // The faint wash of an energised tube, brightest in the middle.
        _glow = SKShader.CreateRadialGradient(
            centre, shortest * 0.8f,
            new[] { phosphor.WithAlpha(13), phosphor.WithAlpha(0) },
            SKShaderTileMode.Clamp);

        // Clear in the middle and closing in to more than half black at the
        // corners. Concentric circles, not a point falloff, so the inner radius
        // maps to the first stop rather than to the centre.
        var inner = shortest * 0.42f;
        var outer = longest * 0.78f;

        _vignette = SKShader.CreateRadialGradient(
            centre, outer,
            new[] { SKColors.Black.WithAlpha(0), SKColors.Black.WithAlpha(140) },
            new[] { inner / outer, 1f },
            SKShaderTileMode.Clamp);

        _scanlines = BuildScanlines(scale);

        // Built here rather than per frame. Resolving a typeface by family name
        // thirty times a second is the same mistake that made Drifting Float
        // stutter, and it costs far more than the paint around it.
        _big = Mono(Math.Max(11f, height * 0.026f), medium: true);
        _small = Mono(Math.Max(9f, height * 0.020f), medium: false);
    }

    /// <summary>
    /// The scanlines, as a repeating tile rather than a thousand rectangles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A bar one and a half pixels high every four pixels, in device pixels, so
    /// the lines stay fine as the screen grows rather than thickening with it.
    /// On a 4K display drawing them one at a time is over a thousand rectangles
    /// a frame; as a tiled shader it is one.
    /// </para>
    /// <para>
    /// The tile is eight rows for a four pixel period, which is the trick that
    /// lets a bar of one and a half pixels be expressed in whole rows: three
    /// rows out of eight is exactly 1.5 out of 4.
    /// </para>
    /// </remarks>
    private SKShader BuildScanlines(float scale)
    {
        const int rows = 8;
        const int lit = 3;

        _scanTile = new SKBitmap(new SKImageInfo(1, rows, SKColorType.Bgra8888, SKAlphaType.Premul));

        var dark = SKColors.Black.WithAlpha((byte)Math.Round(CrtTerminal.ScanAlpha * 255f));

        for (var row = 0; row < rows; row++)
        {
            _scanTile.SetPixel(0, row, row < lit ? dark : SKColors.Transparent);
        }

        // One period of the tile has to come out as four device pixels, which in
        // the canvas's own units is four divided by the scaling.
        var period = CrtTerminal.ScanPeriodPixels / scale;
        var matrix = SKMatrix.CreateScale(period, period / rows);

        return SKShader.CreateBitmap(
            _scanTile, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, matrix);
    }

    private void Release()
    {
        _glow?.Dispose();
        _vignette?.Dispose();
        _scanlines?.Dispose();
        _scanTile?.Dispose();
        _big?.Dispose();
        _small?.Dispose();

        _glow = null;
        _vignette = null;
        _scanlines = null;
        _scanTile = null;
        _big = null;
        _small = null;
    }

    public void Dispose()
    {
        Release();
        _fill.Dispose();
        _blit.Dispose();
        _portrait.Dispose();
    }
}

using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// Cover Flow. A carousel of records turning past the front, each reflected in
/// the surface below.
/// </summary>
/// <remarks>
/// <para>
/// The projection is in <see cref="CoverFlowMath"/>, where it can be tested.
/// This file is the ring of albums, the turning, and the drawing.
/// </para>
/// <para>
/// The one behaviour worth knowing: when something starts playing, that album is
/// written into the slot immediately <em>after</em> the centre, so the carousel
/// arrives at it within one turn rather than ignoring it or jumping to it. The
/// motion never breaks, and the record you just put on comes round to the front
/// on its own.
/// </para>
/// </remarks>
internal sealed class CoverFlowRenderer : IStyleRenderer, IDisposable
{
    private readonly SaverData _data;
    private readonly AlbumPicker _picker;
    private readonly PaletteStore _palettes;
    private readonly bool _isPreview;

    private readonly List<int> _ring = [];
    private readonly List<(int Slot, FlowCard Card, float Distance)> _order = [];
    private readonly SKPaint _fill = new() { IsAntialias = true };

    private double _position;

    public CoverFlowRenderer(SaverData data, AlbumPicker picker, PaletteStore palettes, bool isPreview)
    {
        _data = data;
        _picker = picker;
        _palettes = palettes;
        _isPreview = isPreview;
    }

    private int Wanted => CoverFlowMath.Count(_data.Settings.FlowCount);

    public void BuildLayout(float width, float height)
    {
        _ring.Clear();
        _position = 0;

        var albums = _data.Albums.Count;
        if (albums == 0) return;

        var used = new HashSet<int>();
        for (var i = 0; i < Wanted; i++)
        {
            var index = _picker.PickAvoiding(albums, _data.Settings.RecencyBias, used);
            used.Add(index);
            _ring.Add(index);
        }

        // Whatever is playing starts at the front, so the style opens on it
        // rather than on a random record.
        if (_data.LiveAlbumIndex is { } live && _ring.Count > 0) _ring[0] = live;

        Log.Write($"cover flow layout: {_ring.Count} covers across {width:0}x{height:0} points");
    }

    private int CentreSlot =>
        _ring.Count == 0 ? 0 : (int)Math.Round(_position, MidpointRounding.AwayFromZero) % _ring.Count;

    public void Advance(double phase, float width, float height)
    {
        if (_data.Albums.Count == 0) return;
        if (_ring.Count != Wanted) BuildLayout(width, height);
        if (_ring.Count == 0) return;

        var centre = CentreSlot;

        // Steered rather than jumped to: the playing record is placed one slot
        // ahead so the carousel arrives at it.
        if (_data.LiveAlbumIndex is { } live && _ring[centre] != live)
        {
            _ring[(centre + 1) % _ring.Count] = live;
        }

        _position += TileField.FrameInterval / CoverFlowMath.Dwell(_data.Settings.FeatureSeconds);
        if (_position >= _ring.Count) _position -= _ring.Count;
    }

    public void Draw(SKCanvas canvas, float width, float height, double phase)
    {
        if (_ring.Count == 0) return;

        var centre = CentreSlot;
        var featured = _ring[Math.Clamp(centre, 0, _ring.Count - 1)];
        var albums = _data.Albums;
        if (featured < 0 || featured >= albums.Count) return;

        var palette = _palettes.For(albums[featured].Id, _data.ImageFor(featured));

        DrawBackground(canvas, width, height, palette);

        var side = Math.Min(height * 0.40f, width * 0.28f);
        var centreY = height * 0.44f;

        // How far the carousel has slid past the nearest whole slot.
        var slide = (float)(_position - Math.Round(_position, MidpointRounding.AwayFromZero));

        _order.Clear();
        for (var d = -CoverFlowMath.Visible; d <= CoverFlowMath.Visible; d++)
        {
            var slot = (((centre + d) % _ring.Count) + _ring.Count) % _ring.Count;
            var offset = d - slide;
            _order.Add((slot, CoverFlowMath.Project(offset), Math.Abs(offset)));
        }

        // Painter's algorithm, furthest first: there is no depth buffer, so the
        // draw order is the only thing deciding which card is in front.
        _order.Sort(static (a, b) => b.Distance.CompareTo(a.Distance));

        foreach (var (slot, card, _) in _order)
        {
            DrawCard(canvas, width, centreY, side, slot, card, palette);
        }

        if (_data.Settings.ShowTrackLabel && !_isPreview)
        {
            DrawText(canvas, width, height, centreY, side, featured, palette);
        }
    }

    private void DrawBackground(SKCanvas canvas, float width, float height, ArtPalette palette)
    {
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, height),
            new[]
            {
                palette.Console.WithBrightness(0.22f).ToSkia(),
                palette.Deep.WithBrightness(0.04f).ToSkia(),
            },
            SKShaderTileMode.Clamp);

        _fill.Shader = shader;
        _fill.Color = SKColors.White;
        canvas.DrawRect(SKRect.Create(0, 0, width, height), _fill);
        _fill.Shader = null;
    }

    /// <summary>
    /// One card, drawn inside its own turned coordinate space.
    /// </summary>
    /// <remarks>
    /// Everything is drawn under the same transform, the reflection and the
    /// shadow included. That matters: a shadow that stayed square under a
    /// turned card looks immediately wrong, in a way nobody can name but
    /// everybody sees.
    /// </remarks>
    private void DrawCard(
        SKCanvas canvas, float width, float centreY, float side,
        int slot, FlowCard card, ArtPalette palette)
    {
        if (card.Alpha <= 0.01f) return;

        var image = _data.ImageFor(_ring[slot]);
        if (image is null) return;

        var x = (width * 0.5f) + (card.OffsetX * side);
        var s = side * card.Scale;

        canvas.Save();
        canvas.Translate(x, centreY);

        // The squash first, then the shear on top of it. Skia's matrix is
        // (scaleX, skewX, transX / skewY, scaleY, transY), so the shear goes in
        // the skewY slot: y' = y + shear * x.
        //
        // Held in a local because Concat takes it by reference.
        canvas.Scale(card.Squash, 1f);
        var shear = new SKMatrix(1f, 0f, 0f, card.Shear, 1f, 0f, 0f, 0f, 1f);
        canvas.Concat(ref shear);

        var rect = SKRect.Create(-s / 2f, -s / 2f, s, s);

        Drawing.DrawCover(canvas, image, rect, card.Alpha, radius: 3f, shadowAlpha: 0.6f);

        if (card.Haze > 0f)
        {
            _fill.Color = palette.Deep.ToSkia(card.Haze);
            canvas.DrawRect(rect, _fill);
        }

        DrawReflection(canvas, image, rect, s, card.Alpha, palette);

        canvas.Restore();
    }

    /// <summary>
    /// The card mirrored in the floor, with its own bottom edge against the
    /// card's.
    /// </summary>
    /// <remarks>
    /// That adjacency is the whole point. A gap, or a mirror about the wrong
    /// line, and it stops reading as a reflection and starts reading as a
    /// second smaller card sitting underneath the first.
    /// </remarks>
    private void DrawReflection(
        SKCanvas canvas, SKBitmap image, SKRect rect, float s, float alpha, ArtPalette palette)
    {
        var bandTop = rect.Bottom + 3f;
        var band = SKRect.Create(rect.Left, bandTop, s, s * 0.52f);

        canvas.Save();
        canvas.ClipRect(band);

        canvas.Translate(0, bandTop);
        canvas.Scale(1f, -1f);
        canvas.Translate(0, -bandTop);

        Drawing.DrawCover(
            canvas, image, SKRect.Create(rect.Left, bandTop - s, s, s),
            alpha * 0.28f, radius: 3f, shadowAlpha: 0f);

        canvas.Restore();

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, band.Top),
            new SKPoint(0, band.Bottom),
            new[] { palette.Deep.ToSkia(0.15f), palette.Deep.ToSkia(1f) },
            SKShaderTileMode.Clamp);

        _fill.Shader = shader;
        _fill.Color = SKColors.White;
        canvas.DrawRect(band, _fill);
        _fill.Shader = null;
    }

    private void DrawText(
        SKCanvas canvas, float width, float height, float centreY, float side,
        int featured, ArtPalette palette)
    {
        var (title, artist, _) = FeaturedText.For(_data, featured);

        var columnWidth = width * 0.62f;
        var left = (width * 0.5f) - (columnWidth / 2f);

        // Measured from the unscaled side, so the block sits at a fixed height
        // and does not bob as the centre card's own scale moves.
        var top = centreY + (side * 0.5f) + (side * 0.56f) + (height * 0.03f);

        using var shadow = SKImageFilter.CreateDropShadow(
            0f, 2f, 7f, 7f, SKColors.Black.WithAlpha(191));

        var titleSize = TextLayout.Fitted(
            title, columnWidth, height * 0.10f, Math.Max(18f, height * 0.040f), bold: true);

        using var titlePaint = TextLayout.Paint(titleSize, true, palette.Text.ToSkia());
        titlePaint.ImageFilter = shadow;
        top += TextLayout.Draw(canvas, title, left, top, columnWidth, titlePaint, centred: true);
        top += height * 0.012f;

        using var artistPaint = TextLayout.Paint(
            Math.Max(12f, height * 0.025f), false, palette.Accent.ToSkia());
        artistPaint.ImageFilter = shadow;
        TextLayout.Draw(canvas, artist, left, top, columnWidth, artistPaint, centred: true);
    }

    public void Dispose() => _fill.Dispose();
}

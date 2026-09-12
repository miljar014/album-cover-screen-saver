using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// Zoetrope. Covers standing inside a turning drum, seen through the slits in
/// its front wall.
/// </summary>
/// <remarks>
/// <para>
/// The drum's contents and its motion have nothing to do with playback. It shows
/// the newest covers in the archive in order and turns at its own steady pace,
/// and what is playing appears on it only because the archive is newest-first.
/// Nothing points at it.
/// </para>
/// <para>
/// What playback does change is the colour, since the drum floor, the rim and
/// the artist line all come from the featured album's palette, and the caption.
/// So the drum keeps turning and slowly changes its tint as you listen.
/// </para>
/// </remarks>
internal sealed class ZoetropeRenderer : IStyleRenderer, IDisposable
{
    private readonly SaverData _data;
    private readonly AlbumPicker _picker;
    private readonly PaletteStore _palettes;
    private readonly bool _isPreview;

    private readonly Featured _featured;
    private readonly List<(int Card, float Depth)> _order = [];
    private readonly SKPaint _fill = new() { IsAntialias = true };

    public ZoetropeRenderer(SaverData data, AlbumPicker picker, PaletteStore palettes, bool isPreview)
    {
        _data = data;
        _picker = picker;
        _palettes = palettes;
        _isPreview = isPreview;
        _featured = new Featured(picker, data.Settings.RecencyBias);
    }

    public void BuildLayout(float width, float height)
    {
        if (_data.Albums.Count == 0) return;

        _featured.Reset(0, _picker.Pick(_data.Albums.Count, _data.Settings.RecencyBias));
        Log.Write($"zoetrope layout: {ZoetropeDrum.CardCount(_data.Settings.ZoetropeCount)} cards");
    }

    public void Advance(double phase, float width, float height) =>
        _featured.Advance(phase, _data.LiveAlbumIndex, _data.Settings.FeatureSeconds, _data.Albums.Count);

    public void Draw(SKCanvas canvas, float width, float height, double phase)
    {
        var albums = _data.Albums;
        if (albums.Count == 0) return;

        var featured = Math.Clamp(_featured.Index, 0, albums.Count - 1);
        var palette = _palettes.For(albums[featured].Id, _data.ImageFor(featured));

        var shortest = Math.Min(width, height);
        var centreX = width * 0.5f;
        var centreY = height * 0.44f;
        var radius = Math.Min(shortest * 0.36f, width * 0.34f);
        var cardHeight = radius * 0.62f;

        var count = ZoetropeDrum.CardCount(_data.Settings.ZoetropeCount);
        var spin = ZoetropeDrum.Spin(phase);

        DrawBackground(canvas, width, height, shortest, palette);
        DrawFloor(canvas, centreX, centreY, radius, cardHeight, palette);

        // Far cards first: the only depth sorting the drum has.
        _order.Clear();
        for (var k = 0; k < count; k++)
        {
            _order.Add((k, ZoetropeDrum.DepthOf(ZoetropeDrum.AngleOf(k, count, spin))));
        }
        _order.Sort(static (a, b) => b.Depth.CompareTo(a.Depth));

        foreach (var (card, depth) in _order)
        {
            DrawCard(canvas, card, count, spin, depth, centreX, centreY, radius, cardHeight);
        }

        DrawSlits(canvas, count, spin, centreX, centreY, radius, cardHeight);
        DrawRim(canvas, centreX, centreY, radius, cardHeight, palette);

        if (_data.Settings.ShowTrackLabel && !_isPreview)
        {
            DrawText(canvas, width, height, centreY, radius, cardHeight, featured, palette);
        }
    }

    private void DrawBackground(
        SKCanvas canvas, float width, float height, float shortest, ArtPalette palette)
    {
        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(width * 0.5f, height * 0.38f), shortest,
            new[] { palette.Console.WithBrightness(0.20f).ToSkia(), SKColors.Black },
            SKShaderTileMode.Clamp);

        _fill.Shader = shader;
        _fill.Color = SKColors.White;
        canvas.DrawRect(SKRect.Create(0, 0, width, height), _fill);
        _fill.Shader = null;
    }

    /// <summary>
    /// The floor of the drum, dropped below the card baseline so the cards look
    /// like they are standing on it rather than floating over it.
    /// </summary>
    private void DrawFloor(
        SKCanvas canvas, float centreX, float centreY, float radius, float cardHeight, ArtPalette palette)
    {
        var top = centreY - (radius * ZoetropeDrum.Squash) + (cardHeight * 0.55f);
        var box = SKRect.Create(
            centreX - radius, top, radius * 2f, radius * 2f * ZoetropeDrum.Squash);

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, box.Top),
            new SKPoint(0, box.Bottom),
            new[]
            {
                palette.Console.WithBrightness(0.30f).ToSkia(),
                palette.Console.WithBrightness(0.10f).ToSkia(),
            },
            SKShaderTileMode.Clamp);

        _fill.Shader = shader;
        _fill.Color = SKColors.White;
        canvas.DrawOval(box, _fill);
        _fill.Shader = null;
    }

    private void DrawCard(
        SKCanvas canvas, int card, int count, double spin, float depth,
        float centreX, float centreY, float radius, float cardHeight)
    {
        var angle = ZoetropeDrum.AngleOf(card, count, spin);

        var x = centreX + (float)(Math.Cos(angle) * radius);
        var bottom = centreY - (float)(Math.Sin(angle) * radius * ZoetropeDrum.Squash);
        var w = ZoetropeDrum.CardWidth(cardHeight, depth);

        // The card stands on the ellipse rather than being centred on it, which
        // is the difference between records in a drum and records orbiting one.
        var rect = SKRect.Create(x - (w / 2f), bottom - w, w, w);

        var index = CardAlbum(card);
        Drawing.DrawCover(canvas, _data.ImageFor(index), rect, 1f, radius: 1f, shadowAlpha: 0.4f);

        _fill.Shader = null;
        _fill.Color = SKColors.Black.WithAlpha(
            (byte)Math.Clamp(ZoetropeDrum.ShadeOf(depth) * 255f, 0f, 255f));
        canvas.DrawRect(rect, _fill);
    }

    /// <summary>
    /// Which album is on a card.
    /// </summary>
    /// <remarks>
    /// By position in the archive, not by anything to do with the featured
    /// album. The drum is the newest records in order, and it never turns to put
    /// a particular one at the front.
    /// </remarks>
    private int CardAlbum(int card)
    {
        var albums = _data.Albums.Count;
        if (albums == 0) return 0;
        return card < albums ? card : card % albums;
    }

    private void DrawSlits(
        SKCanvas canvas, int count, double spin,
        float centreX, float centreY, float radius, float cardHeight)
    {
        _fill.Shader = null;
        _fill.Color = new SKColor(23, 18, 15, 245);

        for (var k = 0; k < count; k++)
        {
            var angle = ZoetropeDrum.SlitAngleOf(k, count, spin);
            if (!ZoetropeDrum.IsNearWall(angle)) continue;

            // Two per cent outside the cards, so the wall is in front of them.
            var x = centreX + (float)(Math.Cos(angle) * radius * 1.02);
            var y = centreY - (float)(Math.Sin(angle) * radius * ZoetropeDrum.Squash);
            var w = ZoetropeDrum.SlitWidth(radius, angle);

            canvas.DrawRect(
                SKRect.Create(x - (w / 2f), y - (cardHeight * 1.12f), w, cardHeight * 1.28f), _fill);
        }
    }

    /// <summary>The lit top edge of the drum, sitting just above the tallest cards.</summary>
    private void DrawRim(
        SKCanvas canvas, float centreX, float centreY, float radius, float cardHeight, ArtPalette palette)
    {
        var box = SKRect.Create(
            centreX - (radius * 1.03f),
            centreY - (radius * ZoetropeDrum.Squash * 1.03f) - (cardHeight * 1.06f),
            radius * 2.06f,
            radius * 2.06f * ZoetropeDrum.Squash);

        _fill.Shader = null;
        _fill.Color = palette.Accent.ToSkia(0.7f);
        _fill.Style = SKPaintStyle.Stroke;
        _fill.StrokeWidth = Math.Max(2f, radius * 0.020f);

        canvas.DrawOval(box, _fill);

        _fill.Style = SKPaintStyle.Fill;
    }

    private void DrawText(
        SKCanvas canvas, float width, float height, float centreY, float radius, float cardHeight,
        int featured, ArtPalette palette)
    {
        var (title, artist, _) = FeaturedText.For(_data, featured);

        var columnWidth = width * 0.7f;
        var left = (width * 0.5f) - (columnWidth / 2f);
        var top = centreY + (radius * ZoetropeDrum.Squash) + (cardHeight * 0.7f);

        using var shadow = SKImageFilter.CreateDropShadow(
            0f, 2f, 8f, 8f, SKColors.Black.WithAlpha(204));

        var size = TextLayout.Fitted(
            title, columnWidth, height * 0.10f, Math.Max(18f, height * 0.042f), bold: true);

        using var titlePaint = TextLayout.Paint(size, true, palette.Text.ToSkia());
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

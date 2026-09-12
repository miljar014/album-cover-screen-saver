using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// Subway Platform. A tiled station with album posters along the wall, the one
/// that is playing lit in a backlit box, and a train through every twenty six
/// seconds.
/// </summary>
/// <remarks>
/// <para>
/// Almost nothing here takes the album's colour: the tiles, the plate, the
/// frames and the train are all fixed. The only exception is the outline around
/// the lit poster, which means the album's colour appears exactly where you are
/// meant to look.
/// </para>
/// <para>
/// All the type is drawn as signs: clipped and truncated rather than wrapped,
/// because a station sign that wrapped onto a second line would immediately stop
/// being a station sign.
/// </para>
/// <para>
/// The train is drawn last and is not clipped to anything, so it passes in front
/// of the posters and the platform edge. That is the intended read: you are
/// standing on the platform, not looking at a diagram of one.
/// </para>
/// </remarks>
internal sealed class SubwayRenderer : IStyleRenderer, IDisposable
{
    private static readonly SKColor Tunnel = new(26, 26, 28);
    private static readonly SKColor Grout = new(140, 138, 128);
    private static readonly SKColor Floor = new(51, 51, 54);
    private static readonly SKColor Safety = new(217, 194, 51, 230);
    private static readonly SKColor Plate = new(15, 41, 107);
    private static readonly SKColor FrameDark = new(33, 33, 36);
    private static readonly SKColor TrainTop = new(77, 82, 89);
    private static readonly SKColor TrainBottom = new(33, 36, 41);
    private static readonly SKColor Window = new(242, 230, 179, 217);

    private readonly SaverData _data;
    private readonly AlbumPicker _picker;
    private readonly PaletteStore _palettes;
    private readonly bool _isPreview;

    private readonly Featured _featured;
    private readonly SKPaint _fill = new() { IsAntialias = true };

    public SubwayRenderer(SaverData data, AlbumPicker picker, PaletteStore palettes, bool isPreview)
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
        Log.Write($"subway layout: {width:0}x{height:0} points");
    }

    public void Advance(double phase, float width, float height) =>
        _featured.Advance(phase, _data.LiveAlbumIndex, _data.Settings.FeatureSeconds, _data.Albums.Count);

    public void Draw(SKCanvas canvas, float width, float height, double phase)
    {
        var albums = _data.Albums;
        if (albums.Count == 0) return;

        var featured = Math.Clamp(_featured.Index, 0, albums.Count - 1);
        var palette = _palettes.For(albums[featured].Id, _data.ImageFor(featured));

        var platform = height * (1f - SubwayPlatform.PlatformFraction);

        _fill.Shader = null;
        _fill.Color = Tunnel;
        canvas.DrawRect(SKRect.Create(0, 0, width, height), _fill);

        DrawTiles(canvas, width, platform, height);

        _fill.Shader = null;
        _fill.Color = Floor;
        canvas.DrawRect(SKRect.Create(0, platform, width, height - platform), _fill);

        _fill.Color = Safety;
        canvas.DrawRect(SKRect.Create(0, platform, width, height * 0.012f), _fill);

        DrawStationPlate(canvas, width, height, featured);
        DrawPosters(canvas, width, height, platform, featured, palette);

        if (SubwayPlatform.TrainIsVisible(phase)) DrawTrain(canvas, width, height, platform, phase);
    }

    /// <summary>
    /// The tiled wall, laid in a running bond with every tile a slightly
    /// different warm off-white.
    /// </summary>
    private void DrawTiles(SKCanvas canvas, float width, float platform, float height)
    {
        _fill.Shader = null;
        _fill.Color = Grout;
        canvas.DrawRect(SKRect.Create(0, 0, width, platform), _fill);

        var tile = height * 0.075f;
        var row = 0;

        for (var top = platform - tile; top + tile > 0; top -= tile, row++)
        {
            // Every other course is offset by half a tile, which is what makes
            // it a wall rather than a grid.
            var start = row % 2 == 0 ? 0f : -tile / 2f;

            for (var x = start; x < width; x += tile)
            {
                var shade = SubwayPlatform.TileShade(row, x);

                _fill.Color = new SKColor(
                    (byte)(shade * 255f), (byte)(shade * 0.99f * 255f), (byte)(shade * 0.94f * 255f));

                // The inset on all four sides is the grout showing through.
                canvas.DrawRoundRect(
                    SKRect.Create(x + 1.5f, top + 1.5f, tile - 3f, tile - 3f), 2f, 2f, _fill);
            }
        }
    }

    /// <summary>The station name plate, where the artist is the station.</summary>
    private void DrawStationPlate(SKCanvas canvas, float width, float height, int featured)
    {
        var (_, artist, _) = FeaturedText.For(_data, featured);
        if (string.IsNullOrEmpty(artist)) return;

        var plate = SKRect.Create(width * 0.06f, height * 0.09f, width * 0.40f, height * 0.11f);

        _fill.Shader = null;
        _fill.Color = Plate;
        canvas.DrawRoundRect(plate, 4f, 4f, _fill);

        var keyline = plate;
        keyline.Inflate(-5f, -5f);

        _fill.Color = SKColors.White;
        _fill.Style = SKPaintStyle.Stroke;
        _fill.StrokeWidth = 2f;
        canvas.DrawRoundRect(keyline, 3f, 3f, _fill);
        _fill.Style = SKPaintStyle.Fill;

        using var paint = TextLayout.Paint(
            Math.Max(12f, plate.Height * 0.34f), bold: true, SKColors.White);
        paint.TextAlign = SKTextAlign.Center;

        var box = plate;
        box.Inflate(-14f, -plate.Height * 0.28f);

        Sign(canvas, artist.ToUpperInvariant(), box, paint, tracking: 2f);
    }

    /// <summary>
    /// The posters along the wall, with the one that is playing lit.
    /// </summary>
    /// <remarks>
    /// Posters are four by five, so a square cover is cropped left and right.
    /// That is deliberate: posters are not square, and a square one on a station
    /// wall looks like a mistake.
    /// </remarks>
    private void DrawPosters(
        SKCanvas canvas, float width, float height, float platform, int featured, ArtPalette palette)
    {
        var posterWidth = Math.Min(width * 0.19f, height * 0.30f);
        var slots = SubwayPlatform.PosterSlots(width, posterWidth);
        var lit = SubwayPlatform.LitSlot(slots);

        for (var slot = 0; slot < slots; slot++)
        {
            var isLit = slot == lit;
            var w = isLit ? posterWidth * 1.22f : posterWidth;
            var centreX = SubwayPlatform.PosterCentre(width, slot, slots);

            var poster = SKRect.Create(
                centreX - (w / 2f),
                platform - (height * 0.12f) - (w * 1.25f),
                w, w * 1.25f);

            var frame = poster;
            frame.Inflate(w * 0.035f, w * 0.035f);

            _fill.Shader = null;
            _fill.Color = FrameDark;
            canvas.DrawRect(frame, _fill);

            var album = isLit ? featured : NeighbourAlbum(slot);
            var art = poster;
            art.Inflate(-w * 0.03f, -w * 0.03f);

            Drawing.DrawImage(canvas, _data.ImageFor(album), art, 1f, radius: 0f, shadow: false);

            if (!isLit)
            {
                _fill.Color = SKColors.Black.WithAlpha(115);
                canvas.DrawRect(poster, _fill);
                continue;
            }

            DrawLitPoster(canvas, width, height, poster, frame, w, featured, palette);
        }
    }

    private int NeighbourAlbum(int slot)
    {
        var albums = _data.Albums.Count;
        if (albums == 0) return 0;
        return slot + 1 < albums ? slot + 1 : slot % albums;
    }

    /// <summary>The backlit box: the only bright thing on the platform.</summary>
    private void DrawLitPoster(
        SKCanvas canvas, float width, float height, SKRect poster, SKRect frame, float w,
        int featured, ArtPalette palette)
    {
        var outer = w * 1.3f;

        using (var shader = SKShader.CreateRadialGradient(
                   new SKPoint(poster.MidX, poster.MidY), outer,
                   new[] { SKColors.White.WithAlpha(41), SKColors.White.WithAlpha(0) },
                   new[] { w * 0.4f / outer, 1f },
                   SKShaderTileMode.Clamp))
        {
            _fill.Shader = shader;
            _fill.Color = SKColors.White;
            canvas.DrawCircle(poster.MidX, poster.MidY, outer, _fill);
            _fill.Shader = null;
        }

        // The one place the album's colour appears, and it is exactly where the
        // eye is meant to go.
        _fill.Color = palette.Accent.ToSkia();
        _fill.Style = SKPaintStyle.Stroke;
        _fill.StrokeWidth = 2f;
        canvas.DrawRect(frame, _fill);
        _fill.Style = SKPaintStyle.Fill;

        var (title, _, _) = FeaturedText.For(_data, featured);
        if (string.IsNullOrEmpty(title)) return;

        using var paint = TextLayout.Paint(
            Math.Max(10f, height * 0.021f), bold: true, SKColors.White);
        paint.TextAlign = SKTextAlign.Center;

        Sign(
            canvas, title,
            SKRect.Create(poster.Left, poster.Bottom + (height * 0.005f), poster.Width, height * 0.04f),
            paint);
    }

    /// <summary>
    /// The train, drawn last and clipped to nothing.
    /// </summary>
    /// <remarks>
    /// It starts below the bottom edge and reaches above the platform edge, so
    /// it cuts across the floor and the posters' lower corners. That overlap is
    /// what puts the viewer on the platform rather than in front of a picture of
    /// one.
    /// </remarks>
    private void DrawTrain(SKCanvas canvas, float width, float height, float platform, double phase)
    {
        var left = SubwayPlatform.TrainLeft(phase) * width;
        var bodyHeight = (height - platform) * 1.06f;

        var body = SKRect.Create(
            left, height + (height * 0.02f) - bodyHeight, width * 1.05f, bodyHeight);

        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(0, body.Top),
                   new SKPoint(0, body.Bottom),
                   new[] { TrainTop, TrainBottom },
                   SKShaderTileMode.Clamp))
        {
            _fill.Shader = shader;
            _fill.Color = SKColors.White;
            canvas.DrawRoundRect(body, 10f, 10f, _fill);
            _fill.Shader = null;
        }

        _fill.Color = Window;
        for (var x = body.Left + (width * 0.05f); x < body.Right - (width * 0.05f); x += width * 0.11f)
        {
            canvas.DrawRoundRect(
                SKRect.Create(
                    x, body.Top + (bodyHeight * 0.22f), width * 0.075f, bodyHeight * 0.36f),
                3f, 3f, _fill);
        }
    }

    /// <summary>
    /// One line of sign lettering: clipped to its box, never wrapped.
    /// </summary>
    /// <remarks>
    /// A station sign that ran onto a second line would stop reading as a
    /// station sign, so a long name is simply cut off, which is what happens on
    /// a real platform too.
    /// </remarks>
    private static void Sign(SKCanvas canvas, string text, SKRect box, SKPaint paint, float tracking = 0f)
    {
        canvas.Save();
        canvas.ClipRect(box);

        var baseline = box.MidY - ((paint.FontMetrics.Ascent + paint.FontMetrics.Descent) / 2f);

        if (tracking == 0f)
        {
            canvas.DrawText(text, box.MidX, baseline, paint);
        }
        else
        {
            // Tracked text has to be laid out from the left, so the centring is
            // done by measuring the whole run first.
            var run = paint.MeasureText(text) + (tracking * Math.Max(0, text.Length - 1));
            var x = box.MidX - (run / 2f);

            using var left = paint.Clone();
            left.TextAlign = SKTextAlign.Left;

            foreach (var character in text)
            {
                var glyph = character.ToString();
                canvas.DrawText(glyph, x, baseline, left);
                x += left.MeasureText(glyph) + tracking;
            }
        }

        canvas.Restore();
    }

    public void Dispose() => _fill.Dispose();
}

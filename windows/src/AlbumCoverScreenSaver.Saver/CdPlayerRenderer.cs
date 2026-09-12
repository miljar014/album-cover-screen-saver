using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// CD Player. A portable player from the nineties seen from straight above, lid
/// open, jewel cases scattered around it.
/// </summary>
/// <remarks>
/// <para>
/// The disc turns on a clock of its own that owes nothing to the music. That is
/// deliberate and it is the opposite of the Cassette Deck, whose reels are wound
/// by the position within the track: a disc that stopped when the music did
/// would look like the saver had hung.
/// </para>
/// <para>
/// The jewel cases are scenery. They are laid out once when the style starts and
/// then never move, and unlike the Record Player's table they are not a record
/// of anything you played.
/// </para>
/// </remarks>
internal sealed class CdPlayerRenderer : IStyleRenderer, IDisposable
{
    private static readonly SKColor Lcd = new(112, 140, 107);

    private readonly SaverData _data;
    private readonly AlbumPicker _picker;
    private readonly PaletteStore _palettes;
    private readonly ScatterLayout _scatter;
    private readonly bool _isPreview;

    private readonly Featured _featured;
    private readonly SKPaint _fill = new() { IsAntialias = true };
    private readonly SKPaint _arc = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
    };

    private readonly SKColor[] _hues = new SKColor[CdPlayer.Arcs];

    // The disc's face never changes. It only turns, so it is drawn once and
    // blitted at an angle. Sixty four arcs stroked at nearly half a radius is
    // tens of millions of antialiased pixels a frame on a large screen, and
    // every one of those frames was producing an identical picture.
    private readonly Layer _disc = new();
    private readonly ShadowSprite _shadows = new();
    private readonly SKPaint _sprite = new() { IsAntialias = true, FilterQuality = SKFilterQuality.Low };
    private readonly SKPaint _blit = new() { IsAntialias = true, FilterQuality = SKFilterQuality.Medium };

    private List<Sleeve> _cases = [];

    // Resolved once per layout. Looking a typeface up by family name every frame
    // costs more than everything else on the panel put together.
    private SKPaint? _lcdFont;
    private float _lcdFontFor;

    public CdPlayerRenderer(
        SaverData data, AlbumPicker picker, PaletteStore palettes,
        Random random, bool isPreview)
    {
        _data = data;
        _picker = picker;
        _palettes = palettes;
        _scatter = new ScatterLayout(random);
        _isPreview = isPreview;
        _featured = new Featured(picker, data.Settings.RecencyBias);

        // The wheel never changes, so it is built once rather than sixty four
        // colour conversions a frame.
        for (var i = 0; i < CdPlayer.Arcs; i++)
        {
            _hues[i] = SKColor.FromHsv(CdPlayer.ArcHue(i) * 360f, 75f, 100f)
                .WithAlpha((byte)Math.Round(CdPlayer.ArcAlpha * 255f));
        }
    }

    public void BuildLayout(float width, float height)
    {
        if (_data.Albums.Count == 0) return;

        _featured.Reset(0, _picker.Pick(_data.Albums.Count, _data.Settings.RecencyBias));

        var (keepX, keepY, keepRadius) = CdPlayer.KeepOut(width, height);

        _cases = _scatter.BuildScatter(
            width, height,
            _data.Settings.CdCaseCount, _data.Albums.Count,
            _data.Settings.RecencyBias, _picker,
            CdPlayer.CaseSmallest, CdPlayer.CaseLargest,
            keepX, keepY, keepRadius, _isPreview);

        Log.Write($"cd layout: {_cases.Count} cases across {width:0}x{height:0} points");
    }

    public void Advance(double phase, float width, float height) =>
        _featured.Advance(phase, _data.LiveAlbumIndex, _data.Settings.FeatureSeconds, _data.Albums.Count);

    public void Draw(SKCanvas canvas, float width, float height, double phase)
    {
        var albums = _data.Albums;
        if (albums.Count == 0) return;

        var index = Math.Clamp(_featured.Index, 0, albums.Count - 1);
        var previous = Math.Clamp(_featured.PreviousIndex, 0, albums.Count - 1);
        var palette = _palettes.For(albums[index].Id, _data.ImageFor(index));

        // Taken before anything rotates the canvas, because a rotated matrix no
        // longer reports the display's scaling in its vertical term.
        var scale = Layer.ScaleOf(canvas);

        DrawRoom(canvas, width, height, palette);
        DrawCases(canvas, palette);

        var radius = CdPlayer.Radius(width, height);
        var (cx, cy) = CdPlayer.Centre(width, height);

        var side = CdPlayer.BodySide(radius);
        var body = SKRect.Create(cx - (side / 2f), cy - (side / 2f), side, side);

        DrawBody(canvas, body, radius, palette);

        // The well the disc sits in, which is what stops it reading as a disc
        // lying loose on a slab.
        _fill.Shader = null;
        _fill.Color = new SKColor(13, 13, 13, 230);
        canvas.DrawCircle(cx, cy, CdPlayer.DiscWell(radius), _fill);

        DrawDisc(canvas, cx, cy, radius, index, previous, phase, scale);

        // Outside the rotation, so it is the rim of the disc and not a mark on
        // its surface going round with it.
        _fill.Color = SKColors.White.WithAlpha(64);
        _fill.Style = SKPaintStyle.Stroke;
        _fill.StrokeWidth = 1f;
        canvas.DrawCircle(cx, cy, radius, _fill);

        _fill.Color = palette.Metal.ToSkia(0.70f);
        _fill.StrokeWidth = CdPlayer.RimWidth(radius);
        canvas.DrawCircle(cx, cy, CdPlayer.LidRim(radius), _fill);
        _fill.Style = SKPaintStyle.Fill;

        DrawPanel(canvas, body, index, palette, phase);

        if (_data.Settings.ShowTrackLabel && !_isPreview)
        {
            DrawCredits(canvas, width, height, body, index, palette);
        }
    }

    private void DrawRoom(SKCanvas canvas, float width, float height, ArtPalette palette)
    {
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, height),
            new[] { palette.Deep.ToSkia(0.16f), palette.Deep.ToSkia(0.05f) },
            SKShaderTileMode.Clamp);

        _fill.Shader = null;
        _fill.Color = SKColors.Black;
        canvas.DrawRect(SKRect.Create(0, 0, width, height), _fill);

        _fill.Shader = shader;
        _fill.Color = SKColors.White;
        canvas.DrawRect(SKRect.Create(0, 0, width, height), _fill);
        _fill.Shader = null;
    }

    private void DrawBody(SKCanvas canvas, SKRect body, float radius, ArtPalette palette)
    {
        var corner = CdPlayer.BodyCorner(radius);

        // A stretched sprite, not a filter. The body is nearly the height of the
        // screen and its shadow blurs by a third of a radius, so asking Skia for
        // it every frame means blurring a four megapixel layer thirty times a
        // second for a picture that never changes.
        _sprite.Color = SKColors.White.WithAlpha(191);

        canvas.DrawBitmap(
            _shadows.Get(corner / body.Width, blurFraction: radius * 0.30f / body.Width),
            ShadowSprite.Placement(body, dropFraction: radius * 0.10f / body.Width),
            _sprite);

        // The gradient runs down and a little to the right, which is the
        // specification's minus seventy degrees converted out of y-up.
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(body.Left, body.Top),
            new SKPoint(body.Left + (body.Width * 0.34f), body.Top + (body.Height * 0.94f)),
            new[] { palette.Metal.ToSkia(0.58f), palette.Metal.ToSkia(0.24f) },
            SKShaderTileMode.Clamp);

        _fill.Shader = shader;
        _fill.Color = SKColors.White;
        canvas.DrawRoundRect(body, corner, corner, _fill);
        _fill.Shader = null;
    }

    /// <summary>
    /// The disc itself, all of it inside one rotation.
    /// </summary>
    private void DrawDisc(
        SKCanvas canvas, float cx, float cy, float radius,
        int index, int previous, double phase, float scale)
    {
        var side = radius * 2f;

        // Silver and rainbow together, drawn once into their own image. The
        // label goes on top because it changes with the album, and the rings at
        // the middle are concentric so it makes no difference whether they turn.
        var face = _disc.Get(side, side, scale, $"cd-face|{radius:0.#}", surface =>
        {
            using var silver = SKShader.CreateLinearGradient(
                new SKPoint(radius - (radius * 0.5f), radius - (radius * 0.87f)),
                new SKPoint(radius + (radius * 0.5f), radius + (radius * 0.87f)),
                new[] { new SKColor(219, 219, 219), new SKColor(140, 140, 140) },
                SKShaderTileMode.Clamp);

            using var paint = new SKPaint { IsAntialias = true, Shader = silver };
            surface.DrawCircle(radius, radius, radius, paint);

            DrawDiffraction(surface, radius, radius, radius);
        });

        canvas.Save();
        canvas.RotateRadians(CdPlayer.Spin(phase), cx, cy);

        if (face is not null)
        {
            canvas.DrawImage(face, SKRect.Create(cx - radius, cy - radius, side, side), _blit);
        }

        DrawLabel(canvas, cx, cy, radius, index, previous, phase);

        canvas.Restore();

        DrawMiddle(canvas, cx, cy, radius);
    }

    /// <summary>
    /// The rainbow, as sixty four overlapping bands.
    /// </summary>
    /// <remarks>
    /// Each is a thin arc stroked at nearly half a radius, which is what makes
    /// it a band rather than a line. Drawn as arcs rather than as filled wedges,
    /// because a wedge has a straight edge where a stroked arc has a round one
    /// and at this alpha the difference shows.
    /// </remarks>
    private void DrawDiffraction(SKCanvas canvas, float cx, float cy, float radius)
    {
        var ring = CdPlayer.ArcRadius(radius);
        var oval = SKRect.Create(cx - ring, cy - ring, ring * 2f, ring * 2f);

        _arc.StrokeWidth = CdPlayer.ArcWidth(radius);

        for (var i = 0; i < CdPlayer.Arcs; i++)
        {
            _arc.Color = _hues[i];

            // Negated, because the specification's angles are measured
            // anticlockwise in y-up and Skia measures them the other way.
            canvas.DrawArc(oval, -CdPlayer.ArcStart(i), -CdPlayer.ArcSweep, false, _arc);
        }
    }

    /// <summary>
    /// The printed label, washed out because it is ink on plastic rather than a
    /// paper sleeve.
    /// </summary>
    private void DrawLabel(
        SKCanvas canvas, float cx, float cy, float radius, int index, int previous, double phase)
    {
        var labelRadius = CdPlayer.LabelRadius(radius);
        var label = SKRect.Create(
            cx - labelRadius, cy - labelRadius, labelRadius * 2f, labelRadius * 2f);

        canvas.Save();

        using (var clip = new SKPath())
        {
            clip.AddCircle(cx, cy, labelRadius);
            canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);
        }

        Drawing.DrawFeaturedCover(
            canvas, _data.ImageFor(index), _data.ImageFor(previous), label,
            _featured.FadeRaw(phase), radius: 0f, shadowAlpha: 0f);

        _fill.Shader = null;
        _fill.Color = SKColors.White.WithAlpha(26);
        canvas.DrawCircle(cx, cy, labelRadius, _fill);

        canvas.Restore();
    }

    private void DrawMiddle(SKCanvas canvas, float cx, float cy, float radius)
    {
        var (clear, hub, hole) = CdPlayer.Middle(radius);

        _fill.Shader = null;

        _fill.Color = new SKColor(204, 204, 204, 230);
        canvas.DrawCircle(cx, cy, clear, _fill);

        _fill.Color = new SKColor(184, 184, 184);
        canvas.DrawCircle(cx, cy, hub, _fill);

        _fill.Color = new SKColor(13, 13, 13);
        canvas.DrawCircle(cx, cy, hole, _fill);
    }

    /// <summary>The segment display and the three buttons beside it.</summary>
    private void DrawPanel(
        SKCanvas canvas, SKRect body, int index, ArtPalette palette, double phase)
    {
        var lcd = SKRect.Create(
            body.Left + (body.Width * 0.12f),
            body.Top + (body.Height * 0.055f),
            body.Width * 0.50f,
            body.Height * 0.115f);

        _fill.Shader = null;
        _fill.Color = Lcd;
        canvas.DrawRoundRect(lcd, 3f, 3f, _fill);

        var (title, _, _) = FeaturedText.For(_data, index);
        var size = Math.Max(8f, lcd.Height * 0.44f);

        if (_lcdFont is null || Math.Abs(_lcdFontFor - size) > 0.01f)
        {
            _lcdFont?.Dispose();
            _lcdFont = Mono(size);
            _lcdFontFor = size;
        }

        _lcdFont.Color = new SKColor(26, 26, 26, 217);

        var inset = lcd.Width * 0.04f;
        var room = lcd.Width - (inset * 2f);

        TextLayout.DrawLine(
            canvas, Ellipsised("▶ " + title.ToUpperInvariant(), _lcdFont, room),
            lcd.Left + inset,
            lcd.Top + (lcd.Height * 0.24f) - _lcdFont.FontMetrics.Ascent,
            _lcdFont);

        // The one thing on the whole object that follows the track rather than
        // the album.
        var live = _data.LiveAlbumIndex == index
            ? _data.NowPlaying.ProgressFraction
            : (double?)null;

        var progress = _featured.Progress(phase, _data.Settings.FeatureSeconds, live);

        _fill.Color = new SKColor(26, 26, 26, 128);
        canvas.DrawRect(
            SKRect.Create(lcd.Left + 3f, lcd.Top + 2f, (lcd.Width - 6f) * progress, 2f), _fill);

        DrawButtons(canvas, body, lcd, palette);
    }

    private void DrawButtons(SKCanvas canvas, SKRect body, SKRect lcd, ArtPalette palette)
    {
        var radius = body.Width * 0.035f;

        for (var i = 0; i < 3; i++)
        {
            var cx = body.Right - (body.Width * (0.16f + (i * 0.11f)));

            using var shader = SKShader.CreateLinearGradient(
                new SKPoint(cx - radius, lcd.MidY - radius),
                new SKPoint(cx + (radius * 0.34f), lcd.MidY + (radius * 0.94f)),
                new[] { palette.Metal.ToSkia(0.75f), palette.Metal.ToSkia(0.35f) },
                SKShaderTileMode.Clamp);

            _fill.Shader = shader;
            _fill.Color = SKColors.White;
            canvas.DrawCircle(cx, lcd.MidY, radius, _fill);
            _fill.Shader = null;
        }
    }

    /// <summary>
    /// Shortens a line to fit, with an ellipsis at the end.
    /// </summary>
    /// <remarks>
    /// The display is one line and does not wrap, so a long title has to be cut.
    /// Cut at the tail rather than the middle, which is what a real player does.
    /// </remarks>
    private static string Ellipsised(string text, SKPaint paint, float room)
    {
        if (room <= 0f || paint.MeasureText(text) <= room) return text;

        for (var length = text.Length - 1; length > 0; length--)
        {
            var candidate = text[..length] + "…";
            if (paint.MeasureText(candidate) <= room) return candidate;
        }

        return "…";
    }

    private void DrawCases(SKCanvas canvas, ArtPalette palette)
    {
        foreach (var sleeve in _cases)
        {
            canvas.Save();
            canvas.Translate(sleeve.CentreX, sleeve.CentreY);
            canvas.RotateRadians(sleeve.Angle);

            var half = sleeve.Side / 2f;
            var square = SKRect.Create(-half, -half, sleeve.Side, sleeve.Side);

            // Nine of these a frame, each a blurred offscreen layer, was the
            // other half of why this style ran at three frames a second.
            _sprite.Color = SKColors.White.WithAlpha(153);

            canvas.DrawBitmap(
                _shadows.Get(0f, blurFraction: 0.13f),
                ShadowSprite.Placement(square, dropFraction: 0.035f),
                _sprite);

            // The black tray you can see through the plastic.
            _fill.Color = new SKColor(26, 26, 26, 242);
            canvas.DrawRect(square, _fill);

            var inlay = square;
            inlay.Inflate(-sleeve.Side * 0.035f, -sleeve.Side * 0.035f);
            Drawing.DrawCover(canvas, _data.ImageFor(sleeve.Index), inlay, 1f, radius: 0f, shadowAlpha: 0f);

            // The same push-back the record sleeves get, so the scenery does not
            // compete with the disc.
            _fill.Color = palette.Deep.ToSkia(0.42f);
            canvas.DrawRect(square, _fill);

            _fill.Color = SKColors.White.WithAlpha(41);
            canvas.DrawRect(
                SKRect.Create(square.Left, square.Top, sleeve.Side * 0.085f, sleeve.Side), _fill);

            DrawGlare(canvas, square, sleeve.Side);

            _fill.Color = SKColors.White.WithAlpha(26);
            _fill.Style = SKPaintStyle.Stroke;
            _fill.StrokeWidth = 1f;
            canvas.DrawRect(square, _fill);
            _fill.Style = SKPaintStyle.Fill;

            canvas.Restore();
        }
    }

    /// <summary>
    /// The band of reflection across the plastic, wider at the bottom left.
    /// </summary>
    /// <remarks>
    /// The specification gives its four corners in y-up. Flipped here rather
    /// than mirrored, so the light still comes from the same side of the room
    /// as every other highlight in the style.
    /// </remarks>
    private void DrawGlare(SKCanvas canvas, SKRect square, float side)
    {
        using var path = new SKPath();

        path.MoveTo(square.Left, square.Bottom - (side * 0.30f));
        path.LineTo(square.Left + (side * 0.55f), square.Top);
        path.LineTo(square.Left + (side * 0.78f), square.Top);
        path.LineTo(square.Left, square.Bottom - (side * 0.07f));
        path.Close();

        _fill.Shader = null;
        _fill.Color = SKColors.White.WithAlpha(23);
        canvas.DrawPath(path, _fill);
    }

    private void DrawCredits(
        SKCanvas canvas, float width, float height, SKRect body, int index, ArtPalette palette)
    {
        var (title, artist, _) = FeaturedText.For(_data, index);

        var columnWidth = width * 0.86f;
        var left = (width * 0.5f) - (columnWidth / 2f);

        using var shadow = SKImageFilter.CreateDropShadow(
            0f, 2f, 14f, 14f, SKColors.Black.WithAlpha(191));

        var titleSize = TextLayout.Fitted(
            title, columnWidth, height * 0.09f, Math.Max(17f, height * 0.038f), bold: true);

        var artistSize = Math.Max(12f, height * 0.024f);

        // Measured before anything is drawn, because where the block starts
        // depends on how tall it turns out to be.
        var block = TextLayout.Measure(title, titleSize, true, columnWidth)
                    + (height * 0.010f)
                    + TextLayout.Measure(artist, artistSize, false, columnWidth);

        var top = CdPlayer.CreditsTop(height, body.Bottom, block);

        using var titlePaint = TextLayout.Paint(titleSize, true, palette.Text.ToSkia());
        titlePaint.ImageFilter = shadow;

        top += TextLayout.Draw(canvas, title, left, top, columnWidth, titlePaint, centred: true);
        top += height * 0.010f;

        using var artistPaint = TextLayout.Paint(artistSize, false, palette.Accent.ToSkia());
        artistPaint.ImageFilter = shadow;

        TextLayout.Draw(canvas, artist, left, top, columnWidth, artistPaint, centred: true);
    }

    private static SKPaint Mono(float size) => new()
    {
        IsAntialias = true,
        TextSize = size,
        TextAlign = SKTextAlign.Left,
        Typeface = SKTypeface.FromFamilyName(
                       "Consolas", SKFontStyleWeight.Bold,
                       SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                   ?? SKTypeface.FromFamilyName(
                       "Courier New", SKFontStyleWeight.Bold,
                       SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                   ?? SKTypeface.Default,
    };

    public void Dispose()
    {
        _fill.Dispose();
        _arc.Dispose();
        _lcdFont?.Dispose();
        _disc.Dispose();
        _shadows.Dispose();
        _sprite.Dispose();
        _blit.Dispose();
    }
}

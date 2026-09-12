using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// Cassette Deck. A brushed metal face, a smoked window, and a cassette whose
/// reels wind in step with the track.
/// </summary>
/// <remarks>
/// <para>
/// The reels are the style. They are wound by the position within the song, not
/// by a clock, so when the music stops they stop and when you skip they jump.
/// A clock-driven spin would keep turning through a pause and there would be
/// nothing left of the illusion.
/// </para>
/// <para>
/// The tape between them is a real tangent of each wound pack, so the exposed
/// span shifts by itself as one reel empties into the other.
/// </para>
/// </remarks>
internal sealed class CassetteRenderer : IStyleRenderer, IDisposable
{
    private static readonly SKColor TapeBrown = new(56, 36, 26);

    private readonly SaverData _data;
    private readonly AlbumPicker _picker;
    private readonly PaletteStore _palettes;
    private readonly bool _isPreview;

    private readonly Featured _featured;
    private readonly SKPaint _fill = new() { IsAntialias = true };
    private readonly SKPaint _stroke = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round,
    };

    private readonly Layer _furniture = new();
    private readonly SKPaint _blit = new() { IsAntialias = true, FilterQuality = SKFilterQuality.Medium };

    public CassetteRenderer(
        SaverData data, AlbumPicker picker, PaletteStore palettes, bool isPreview)
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
        Log.Write($"cassette layout: {width:0}x{height:0} points");
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

        var deck = SKRect.Create(width * 0.09f, height * 0.28f, width * 0.82f, height * 0.44f);

        var window = SKRect.Create(
            deck.Left + (deck.Width * 0.045f),
            deck.Top + (deck.Height * 0.20f),
            deck.Width * 0.60f,
            deck.Height * 0.66f);

        // The room, the face, its seventy hairlines of brushed grain and the
        // smoked window are one unchanging picture. Only the cassette inside it
        // moves, so all of that is drawn once and blitted.
        var furniture = _furniture.Get(
            width, height, Layer.ScaleOf(canvas), $"deck|{palette.Metal}|{palette.Deep}",
            surface =>
            {
                DrawRoom(surface, width, height, palette);
                DrawDeck(surface, deck, palette);

                var glass = new SKPaint { IsAntialias = true, Color = new SKColor(13, 13, 13, 235) };
                surface.DrawRoundRect(window, 6f, 6f, glass);
                glass.Dispose();
            });

        if (furniture is not null)
        {
            canvas.DrawImage(furniture, SKRect.Create(0, 0, width, height), _blit);
        }
        else
        {
            DrawRoom(canvas, width, height, palette);
            DrawDeck(canvas, deck, palette);

            _fill.Shader = null;
            _fill.Color = new SKColor(13, 13, 13, 235);
            canvas.DrawRoundRect(window, 6f, 6f, _fill);
        }

        var shell = window;
        shell.Inflate(-window.Width * 0.035f, -window.Height * 0.07f);

        Drawing.DrawFeaturedCover(
            canvas, _data.ImageFor(index), _data.ImageFor(previous), shell,
            _featured.FadeRaw(phase), radius: 4f, shadowAlpha: 0f);

        // Printed on the plastic rather than pasted on the front of it.
        _fill.Color = palette.Deep.ToSkia(0.30f);
        canvas.DrawRoundRect(shell, 4f, 4f, _fill);

        DrawTransport(canvas, shell, palette, phase);

        _stroke.StrokeWidth = 2f;
        _stroke.Color = palette.Metal.ToSkia(0.5f);
        canvas.DrawRoundRect(shell, 4f, 4f, _stroke);

        DrawGlare(canvas, window);
        DrawKeys(canvas, deck, window, palette);

        if (_data.Settings.ShowTrackLabel && !_isPreview)
        {
            DrawCredits(canvas, width, height, deck, index, palette);
        }
    }

    private void DrawRoom(SKCanvas canvas, float width, float height, ArtPalette palette)
    {
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, height),
            new[]
            {
                palette.Deep.WithBrightness(0.14f).ToSkia(),
                palette.Deep.WithBrightness(0.04f).ToSkia(),
            },
            SKShaderTileMode.Clamp);

        _fill.Shader = shader;
        _fill.Color = SKColors.White;
        canvas.DrawRect(SKRect.Create(0, 0, width, height), _fill);
        _fill.Shader = null;
    }

    /// <summary>The face of the deck, and the brushed grain across it.</summary>
    private void DrawDeck(SKCanvas canvas, SKRect deck, ArtPalette palette)
    {
        var corner = deck.Height * 0.05f;

        using (var shadow = SKImageFilter.CreateDropShadowOnly(
                   0f, deck.Height * 0.03f, deck.Height * 0.08f, deck.Height * 0.08f,
                   SKColors.Black.WithAlpha(166)))
        {
            _fill.Shader = null;
            _fill.Color = SKColors.Black;
            _fill.ImageFilter = shadow;
            canvas.DrawRoundRect(deck, corner, corner, _fill);
            _fill.ImageFilter = null;
        }

        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(deck.Left, deck.Top),
                   new SKPoint(deck.Left + (deck.Width * 0.17f), deck.Bottom),
                   new[] { palette.Metal.ToSkia(0.62f), palette.Metal.ToSkia(0.32f) },
                   SKShaderTileMode.Clamp))
        {
            _fill.Shader = shader;
            _fill.Color = SKColors.White;
            canvas.DrawRoundRect(deck, corner, corner, _fill);
            _fill.Shader = null;
        }

        canvas.Save();

        using (var clip = new SKPath())
        {
            clip.AddRoundRect(deck, corner, corner);
            canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);

            // Seventy hairlines, alternating light and dark at a barely visible
            // alpha. Individually invisible, together they are brushed metal.
            for (var i = 0; i < 70; i++)
            {
                _fill.Color = (i % 2 == 0 ? SKColors.White : SKColors.Black).WithAlpha(9);

                canvas.DrawRect(
                    SKRect.Create(deck.Left, deck.Top + (deck.Height * i / 70f), deck.Width, 1f),
                    _fill);
            }
        }

        canvas.Restore();
    }

    /// <summary>
    /// The tape and the two reels that carry it.
    /// </summary>
    /// <remarks>
    /// The tape path is drawn before the reels, so each pack prints over the end
    /// of its own span and the tape appears to go under the wound edge rather
    /// than to stop at it.
    /// </remarks>
    private void DrawTransport(SKCanvas canvas, SKRect shell, ArtPalette palette, double phase)
    {
        var live = _data.NowPlaying.IsLive;
        var duration = _data.NowPlaying.DurationMs / 1000.0;

        var (progress, span) = TapeDeck.Clock(
            live, duration, _data.NowPlaying.ProgressFraction, phase);

        var maxRadius = TapeDeck.MaxRadius(shell.Height);
        var hubRadius = TapeDeck.HubRadius(maxRadius);

        var reels = TapeDeck.Wind(maxRadius, hubRadius, progress, span);

        var hubY = shell.MidY - (shell.Height * 0.04f);
        var supplyX = shell.Left + (shell.Width * 0.29f);
        var takeupX = shell.Left + (shell.Width * 0.71f);

        var guideRadius = shell.Height * 0.032f;
        var guideY = shell.Bottom - (shell.Height * 0.150f);
        var leftGuideX = shell.Left + (shell.Width * 0.130f);
        var rightGuideX = shell.Right - (shell.Width * 0.130f);

        DrawHead(canvas, shell, guideY, palette);

        DrawTape(
            canvas, shell,
            supplyX, takeupX, hubY, reels,
            leftGuideX, rightGuideX, guideY, guideRadius);

        DrawReel(canvas, supplyX, hubY, reels.SupplyRadius, hubRadius, reels.SupplyAngle, palette);
        DrawReel(canvas, takeupX, hubY, reels.TakeupRadius, hubRadius, reels.TakeupAngle, palette);

        DrawRoller(canvas, leftGuideX, guideY, guideRadius, palette);
        DrawRoller(canvas, rightGuideX, guideY, guideRadius, palette);
    }

    private void DrawTape(
        SKCanvas canvas, SKRect shell,
        float supplyX, float takeupX, float hubY, Reels reels,
        float leftGuideX, float rightGuideX, float guideY, float guideRadius)
    {
        var (tlx, tly) = TapeDeck.Tangent(
            leftGuideX, guideY, supplyX, hubY, reels.SupplyRadius, takeLeft: true);

        var (trx, trY) = TapeDeck.Tangent(
            rightGuideX, guideY, takeupX, hubY, reels.TakeupRadius, takeLeft: false);

        using var path = new SKPath();

        path.MoveTo(tlx, tly);
        path.LineTo(leftGuideX - guideRadius, guideY);

        // Round the underside of each roller. The Mac states these as
        // anticlockwise arcs in its own space; in a y-down one the same quarter
        // turns run the other way round the clock.
        path.ArcTo(
            SKRect.Create(
                leftGuideX - guideRadius, guideY - guideRadius, guideRadius * 2f, guideRadius * 2f),
            180f, -90f, false);

        path.LineTo(rightGuideX, guideY + guideRadius);

        path.ArcTo(
            SKRect.Create(
                rightGuideX - guideRadius, guideY - guideRadius, guideRadius * 2f, guideRadius * 2f),
            90f, -90f, false);

        path.LineTo(trx, trY);

        var width = Math.Max(1.5f, shell.Height * 0.020f);

        _stroke.StrokeWidth = width * 1.9f;
        _stroke.Color = SKColors.Black.WithAlpha(89);
        canvas.DrawPath(path, _stroke);

        _stroke.StrokeWidth = width;
        _stroke.Color = TapeBrown;
        canvas.DrawPath(path, _stroke);

        _stroke.StrokeWidth = width * 0.28f;
        _stroke.Color = SKColors.White.WithAlpha(33);
        canvas.DrawPath(path, _stroke);
    }

    /// <summary>
    /// One reel.
    /// </summary>
    /// <remarks>
    /// The wound tape is drawn outside the rotation and only the hub turns
    /// inside it. Rotating the pack as well would make it shimmer, because a
    /// gradient on a turning circle has no business turning with it.
    /// </remarks>
    private void DrawReel(
        SKCanvas canvas, float cx, float cy, float tape, float hub, float angle, ArtPalette palette)
    {
        _fill.Shader = null;
        _fill.Color = new SKColor(10, 10, 10, 242);
        canvas.DrawCircle(cx, cy, tape * 1.06f, _fill);

        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(cx - (tape * 0.5f), cy - (tape * 0.87f)),
                   new SKPoint(cx + (tape * 0.5f), cy + (tape * 0.87f)),
                   new[] { new SKColor(77, 48, 33), new SKColor(41, 26, 18) },
                   SKShaderTileMode.Clamp))
        {
            _fill.Shader = shader;
            _fill.Color = SKColors.White;
            canvas.DrawCircle(cx, cy, tape, _fill);
            _fill.Shader = null;
        }

        canvas.Save();
        canvas.RotateRadians(angle, cx, cy);

        _fill.Color = palette.Metal.ToSkia(0.85f);
        canvas.DrawCircle(cx, cy, hub, _fill);

        // Six spokes, which are the only thing making the rotation legible at
        // all: a plain disc turning looks exactly like a plain disc at rest.
        _stroke.StrokeWidth = hub * 0.30f;
        _stroke.Color = new SKColor(15, 15, 15);

        for (var i = 0; i < 6; i++)
        {
            var a = i * MathF.PI / 3f;

            canvas.DrawLine(
                cx, cy,
                cx + (MathF.Cos(a) * hub * 0.92f),
                cy + (MathF.Sin(a) * hub * 0.92f),
                _stroke);
        }

        _fill.Color = new SKColor(15, 15, 15);
        canvas.DrawCircle(cx, cy, hub * 0.30f, _fill);

        canvas.Restore();
    }

    private void DrawRoller(SKCanvas canvas, float cx, float cy, float radius, ArtPalette palette)
    {
        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(cx - radius, cy - radius),
                   new SKPoint(cx + (radius * 0.34f), cy + (radius * 0.94f)),
                   new[] { palette.Metal.ToSkia(0.78f), palette.Metal.ToSkia(0.30f) },
                   SKShaderTileMode.Clamp))
        {
            _fill.Shader = shader;
            _fill.Color = SKColors.White;
            canvas.DrawCircle(cx, cy, radius, _fill);
            _fill.Shader = null;
        }

        _fill.Color = new SKColor(10, 10, 10, 230);
        canvas.DrawCircle(cx, cy, radius - (radius * 0.62f), _fill);
    }

    private void DrawHead(SKCanvas canvas, SKRect shell, float guideY, ArtPalette palette)
    {
        var headWidth = shell.Width * 0.16f;
        var headHeight = shell.Height * 0.14f;

        var opening = SKRect.Create(
            shell.MidX - (headWidth / 2f),
            guideY + (headHeight * 0.55f) - headHeight,
            headWidth, headHeight);

        _fill.Shader = null;
        _fill.Color = new SKColor(13, 13, 13, 217);
        canvas.DrawRoundRect(opening, 2f, 2f, _fill);

        var capstanWidth = headWidth * 0.24f;
        var capstanHeight = headHeight * 0.62f;

        _fill.Color = palette.Metal.ToSkia(0.55f);
        canvas.DrawRect(
            SKRect.Create(
                opening.MidX - (capstanWidth / 2f), opening.Bottom - capstanHeight,
                capstanWidth, capstanHeight),
            _fill);
    }

    /// <summary>A sheen across the top half of the smoked window.</summary>
    private void DrawGlare(SKCanvas canvas, SKRect window)
    {
        canvas.Save();

        using (var clip = new SKPath())
        {
            clip.AddRoundRect(window, 6f, 6f);
            canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);

            var top = SKRect.Create(window.Left, window.Top, window.Width, window.Height / 2f);

            using var shader = SKShader.CreateLinearGradient(
                new SKPoint(0, top.Top),
                new SKPoint(0, top.Bottom),
                new[] { SKColors.White.WithAlpha(26), SKColors.White.WithAlpha(0) },
                SKShaderTileMode.Clamp);

            _fill.Shader = shader;
            _fill.Color = SKColors.White;
            canvas.DrawRect(top, _fill);
            _fill.Shader = null;
        }

        canvas.Restore();
    }

    /// <summary>
    /// The transport keys, in the piano style of the era.
    /// </summary>
    /// <remarks>
    /// Only the play key rides down, and only while something is actually
    /// playing. That single twelve per cent of a key's height is the whole of
    /// what makes the panel read as responding to the music instead of being a
    /// printed decal. The keys do nothing; there is nothing to click.
    /// </remarks>
    private void DrawKeys(SKCanvas canvas, SKRect deck, SKRect window, ArtPalette palette)
    {
        var box = SKRect.Create(
            window.Right + (deck.Width * 0.035f),
            window.Top,
            deck.Right - window.Right - (deck.Width * 0.075f),
            window.Height);

        if (box.Width <= 0f) return;

        var plate = box;
        plate.Inflate(0f, -box.Height * 0.20f);

        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(0, plate.Top),
                   new SKPoint(0, plate.Bottom),
                   new[] { palette.Metal.ToSkia(0.18f), palette.Metal.ToSkia(0.34f) },
                   SKShaderTileMode.Clamp))
        {
            // Dark at the top, which is what makes it read as recessed into the
            // face rather than sitting on it.
            _fill.Shader = shader;
            _fill.Color = SKColors.White;
            canvas.DrawRoundRect(plate, plate.Height * 0.14f, plate.Height * 0.14f, _fill);
            _fill.Shader = null;
        }

        _stroke.StrokeWidth = 1f;
        _stroke.Color = SKColors.Black.WithAlpha(115);
        canvas.DrawRoundRect(plate, plate.Height * 0.14f, plate.Height * 0.14f, _stroke);

        var running = _data.NowPlaying.IsLive;

        var gap = plate.Width * 0.055f;
        var keyWidth = (plate.Width - (gap * 4f)) / 3f;
        var keyHeight = plate.Height * 0.62f;
        var baseTop = plate.Top + (plate.Height * 0.20f);

        for (var i = 0; i < 3; i++)
        {
            var pressed = TapeDeck.IsPressed(i, running);

            var key = SKRect.Create(
                plate.Left + gap + ((keyWidth + gap) * i),
                baseTop + TapeDeck.KeyDrop(i, running, keyHeight),
                keyWidth, keyHeight);

            var corner = keyWidth * 0.16f;

            if (!pressed)
            {
                // The block under a raised key, which is what gives it a side.
                _fill.Shader = null;
                _fill.Color = new SKColor(10, 10, 10, 191);

                var skirt = key;
                skirt.Offset(0f, keyHeight * 0.10f);
                canvas.DrawRoundRect(skirt, corner, corner, _fill);
            }

            using (var shader = SKShader.CreateLinearGradient(
                       new SKPoint(0, key.Top),
                       new SKPoint(0, key.Bottom),
                       new[]
                       {
                           palette.Metal.ToSkia(pressed ? 0.34f : 0.72f),
                           palette.Metal.ToSkia(pressed ? 0.20f : 0.40f),
                       },
                       SKShaderTileMode.Clamp))
            {
                _fill.Shader = shader;
                _fill.Color = SKColors.White;
                canvas.DrawRoundRect(key, corner, corner, _fill);
                _fill.Shader = null;
            }

            _stroke.StrokeWidth = 1f;
            _stroke.Color = SKColors.White.WithAlpha((byte)(pressed ? 26 : 56));
            canvas.DrawRoundRect(key, corner, corner, _stroke);

            var icon = key;
            icon.Inflate(-keyWidth * 0.26f, -keyHeight * 0.30f);

            // A pressed key's legend goes light, because the key under it has
            // gone dark.
            _fill.Shader = null;
            _fill.Color = pressed ? new SKColor(219, 219, 219) : new SKColor(26, 26, 26);

            DrawKeyIcon(canvas, icon, i, running);
        }

        DrawLamp(canvas, plate, running);
    }

    private void DrawKeyIcon(SKCanvas canvas, SKRect r, int key, bool running)
    {
        switch (key)
        {
            case 0:
                Triangle(canvas, SKRect.Create(r.Left, r.Top, r.Width * 0.54f, r.Height), false);
                Triangle(canvas, SKRect.Create(
                    r.Right - (r.Width * 0.54f), r.Top, r.Width * 0.54f, r.Height), false);
                break;

            case 1 when running:
                Triangle(canvas, SKRect.Create(
                    r.Left + (r.Width * 0.12f), r.Top, r.Width * 0.80f, r.Height), true);
                break;

            case 1:
                canvas.DrawRect(SKRect.Create(r.Left, r.Top, r.Width * 0.30f, r.Height), _fill);
                canvas.DrawRect(SKRect.Create(
                    r.Right - (r.Width * 0.30f), r.Top, r.Width * 0.30f, r.Height), _fill);
                break;

            default:
                Triangle(canvas, SKRect.Create(r.Left, r.Top, r.Width * 0.54f, r.Height), true);
                Triangle(canvas, SKRect.Create(
                    r.Right - (r.Width * 0.54f), r.Top, r.Width * 0.54f, r.Height), true);
                break;
        }
    }

    private void Triangle(SKCanvas canvas, SKRect r, bool pointsRight)
    {
        using var path = new SKPath();

        if (pointsRight)
        {
            path.MoveTo(r.Left, r.Top);
            path.LineTo(r.Left, r.Bottom);
            path.LineTo(r.Right, r.MidY);
        }
        else
        {
            path.MoveTo(r.Right, r.Top);
            path.LineTo(r.Right, r.Bottom);
            path.LineTo(r.Left, r.MidY);
        }

        path.Close();
        canvas.DrawPath(path, _fill);
    }

    private void DrawLamp(SKCanvas canvas, SKRect plate, bool running)
    {
        var radius = plate.Height * 0.055f;
        var cx = plate.MidX;
        var cy = plate.Bottom - (plate.Height * 0.14f);

        var colour = running ? new SKColor(242, 61, 51) : new SKColor(77, 23, 20);

        _fill.Shader = null;

        if (running)
        {
            _fill.Color = colour.WithAlpha(71);
            canvas.DrawCircle(cx, cy, radius + (radius * 1.4f), _fill);
        }

        _fill.Color = colour;
        canvas.DrawCircle(cx, cy, radius, _fill);
    }

    private void DrawCredits(
        SKCanvas canvas, float width, float height, SKRect deck, int index, ArtPalette palette)
    {
        var (title, artist, sub) = FeaturedText.For(_data, index);

        var columnWidth = width * 0.82f;
        var top = deck.Bottom + (height * 0.035f);

        using var shadow = SKImageFilter.CreateDropShadow(
            0f, 2f, 14f, 14f, SKColors.Black.WithAlpha(179));

        var titleSize = TextLayout.Fitted(
            title, columnWidth, height * 0.10f, Math.Max(18f, height * 0.042f), bold: true);

        using var titlePaint = TextLayout.Paint(titleSize, true, palette.Text.ToSkia());
        titlePaint.ImageFilter = shadow;

        top += TextLayout.Draw(canvas, title, deck.Left, top, columnWidth, titlePaint);
        top += height * 0.012f;

        var second = string.IsNullOrEmpty(sub) ? artist : $"{artist}  ·  {sub}";

        using var secondPaint = TextLayout.Paint(
            Math.Max(12f, height * 0.024f), false, palette.Accent.ToSkia());
        secondPaint.ImageFilter = shadow;

        TextLayout.Draw(canvas, second, deck.Left, top, columnWidth, secondPaint);
    }

    public void Dispose()
    {
        _fill.Dispose();
        _stroke.Dispose();
        _blit.Dispose();
        _furniture.Dispose();
    }
}

using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// Neon Jukebox. An arched cabinet ringed in neon, bubble tubes down each
/// shoulder, the album in the display window and five selection strips below it.
/// </summary>
/// <remarks>
/// Nothing in this style is driven by how far through a track you are, so it
/// degrades perfectly: with the music off the neon still breathes, the bubbles
/// still rise, and the only thing that changes is that the top strip loses its
/// tab and shows an album name instead of a track.
/// </remarks>
internal sealed class JukeboxRenderer : IStyleRenderer, IDisposable
{
    private static readonly SKColor Card = new(242, 235, 214);

    private readonly SaverData _data;
    private readonly AlbumPicker _picker;
    private readonly PaletteStore _palettes;
    private readonly Random _random;
    private readonly bool _isPreview;

    private readonly Featured _featured;
    private readonly Bubble[] _bubbles = new Bubble[Jukebox.Bubbles];

    private readonly SKPaint _fill = new() { IsAntialias = true };
    private readonly SKPaint _stroke = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeCap = SKStrokeCap.Round,
    };

    private SKPaint? _stripFont;
    private float _stripFontFor;

    // One small glow per colour, stretched under each bubble. Building a radial
    // gradient shader per bubble per frame is forty four shaders a frame for six
    // distinct pictures.
    private readonly Dictionary<uint, SKBitmap> _glows = new();
    private readonly Layer _cabinet = new();
    private readonly SKPaint _blit = new() { IsAntialias = true, FilterQuality = SKFilterQuality.Low };

    public JukeboxRenderer(
        SaverData data, AlbumPicker picker, PaletteStore palettes, Random random, bool isPreview)
    {
        _data = data;
        _picker = picker;
        _palettes = palettes;
        _random = random;
        _isPreview = isPreview;
        _featured = new Featured(picker, data.Settings.RecencyBias);
    }

    public void BuildLayout(float width, float height)
    {
        if (_data.Albums.Count == 0) return;

        _featured.Reset(0, _picker.Pick(_data.Albums.Count, _data.Settings.RecencyBias));

        for (var i = 0; i < _bubbles.Length; i++)
        {
            _bubbles[i] = Jukebox.Spawn(_random, _random.Next(0, 2), (float)_random.NextDouble());
        }

        Log.Write($"jukebox layout: {width:0}x{height:0} points");
    }

    public void Advance(double phase, float width, float height)
    {
        _featured.Advance(phase, _data.LiveAlbumIndex, _data.Settings.FeatureSeconds, _data.Albums.Count);

        for (var i = 0; i < _bubbles.Length; i++)
        {
            _bubbles[i] = Jukebox.Step(_bubbles[i], i, phase, TileField.FrameInterval, _random);
        }
    }

    public void Draw(SKCanvas canvas, float width, float height, double phase)
    {
        var albums = _data.Albums;
        if (albums.Count == 0) return;

        var index = Math.Clamp(_featured.Index, 0, albums.Count - 1);
        var previous = Math.Clamp(_featured.PreviousIndex, 0, albums.Count - 1);
        var palette = _palettes.For(albums[index].Id, _data.ImageFor(index));

        var pulse = Jukebox.Pulse(phase);

        var cabinetWidth = Jukebox.CabinetWidth(width, height);
        var cabinetLeft = (width * 0.5f) - (cabinetWidth / 2f);
        var archRadius = cabinetWidth / 2f;
        var shoulder = Jukebox.ShoulderY(height);

        // The room and the cabinet never change between albums, and the
        // cabinet's shadow is a blur the width of the machine. The neon over it
        // does change, every frame, because it is breathing.
        var furniture = _cabinet.Get(
            width, height, Layer.ScaleOf(canvas), $"cabinet|{palette.Console}|{palette.Deep}",
            surface =>
            {
                DrawRoom(surface, width, height, palette);
                DrawCabinet(
                    surface, width, height, cabinetLeft, cabinetWidth, archRadius, shoulder, palette);
            });

        if (furniture is not null)
        {
            canvas.DrawImage(furniture, SKRect.Create(0, 0, width, height), _blit);
        }
        else
        {
            DrawRoom(canvas, width, height, palette);
            DrawCabinet(
                canvas, width, height, cabinetLeft, cabinetWidth, archRadius, shoulder, palette);
        }

        DrawArch(canvas, width, archRadius, shoulder, cabinetWidth, palette, pulse);
        DrawTubes(canvas, height, cabinetLeft, cabinetWidth, archRadius, shoulder, palette);

        var side = Jukebox.DisplaySide(cabinetWidth, height);
        var cy = shoulder + (archRadius * 0.18f);

        var display = SKRect.Create((width * 0.5f) - (side / 2f), cy - (side / 2f), side, side);
        var well = display;
        well.Inflate(side * 0.09f, side * 0.09f);

        DrawDisplay(canvas, display, well, index, previous, palette, pulse, phase);

        if (_data.Settings.ShowTrackLabel && !_isPreview)
        {
            DrawStrips(canvas, width, height, cabinetWidth, well, index, palette);
        }
    }

    private void DrawRoom(SKCanvas canvas, float width, float height, ArtPalette palette)
    {
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, height),
            new[] { palette.Deep.WithBrightness(0.12f).ToSkia(), SKColors.Black },
            SKShaderTileMode.Clamp);

        _fill.Shader = shader;
        _fill.Color = SKColors.White;
        canvas.DrawRect(SKRect.Create(0, 0, width, height), _fill);
        _fill.Shader = null;
    }

    /// <summary>
    /// The cabinet: straight sides capped with a semicircle.
    /// </summary>
    private void DrawCabinet(
        SKCanvas canvas, float width, float height, float left, float cabinetWidth,
        float archRadius, float shoulder, ArtPalette palette)
    {
        var foot = height * 0.94f;

        using var body = new SKPath();
        body.MoveTo(left, foot);
        body.LineTo(left, shoulder);

        // Over the top. In the Mac's y-up space this is stated as 180 to 0
        // clockwise; here the same half turn passes through straight up, which
        // is 270 rather than 90.
        body.ArcTo(
            SKRect.Create(
                (width * 0.5f) - archRadius, shoulder - archRadius, archRadius * 2f, archRadius * 2f),
            180f, 180f, false);

        body.LineTo(left + cabinetWidth, foot);
        body.Close();

        using (var shadow = SKImageFilter.CreateDropShadowOnly(
                   0f, 0f, cabinetWidth * 0.10f, cabinetWidth * 0.10f,
                   SKColors.Black.WithAlpha(204)))
        {
            _fill.Shader = null;
            _fill.Color = SKColors.Black;
            _fill.ImageFilter = shadow;
            canvas.DrawPath(body, _fill);
            _fill.ImageFilter = null;
        }

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(left, shoulder - archRadius),
            new SKPoint(left + (cabinetWidth * 0.26f), foot),
            new[]
            {
                palette.Console.WithBrightness(0.34f).ToSkia(),
                palette.Console.WithBrightness(0.10f).ToSkia(),
            },
            SKShaderTileMode.Clamp);

        _fill.Shader = shader;
        _fill.Color = SKColors.White;
        canvas.DrawPath(body, _fill);
        _fill.Shader = null;
    }

    /// <summary>
    /// Two arcs of tubing over the arch, breathing against each other.
    /// </summary>
    /// <remarks>
    /// The inner one runs in counter-phase and is allowed past full brightness,
    /// so as the outer tube fades the inner one comes up. Both on the same pulse
    /// would give one throbbing blob instead of an arch.
    /// </remarks>
    private void DrawArch(
        SKCanvas canvas, float width, float archRadius, float shoulder,
        float cabinetWidth, ArtPalette palette, float pulse)
    {
        var centreX = width * 0.5f;

        using (var outer = Tube(centreX, shoulder, archRadius * 0.93f))
        {
            Neon(canvas, outer, palette.Accent.ToSkia(), Math.Max(2f, cabinetWidth * 0.010f), pulse);
        }

        using var inner = Tube(centreX, shoulder, archRadius * 0.80f);

        Neon(
            canvas, inner,
            palette.Metal.WithBrightness(0.85f, saturationMultiplier: 2f).ToSkia(),
            Math.Max(1.5f, cabinetWidth * 0.007f),
            Jukebox.CounterPulse(pulse));
    }

    private static SKPath Tube(float centreX, float centreY, float radius)
    {
        var path = new SKPath();

        path.AddArc(
            SKRect.Create(centreX - radius, centreY - radius, radius * 2f, radius * 2f),
            182f, 176f);

        return path;
    }

    /// <summary>
    /// Neon, as five strokes of the same path.
    /// </summary>
    /// <remarks>
    /// Wide and faint first, then narrower and brighter, and last a thin white
    /// core. The white is the trick: a hot core inside a coloured halo is what
    /// the eye reads as a gas tube, and the same five strokes in the tube's own
    /// colour give a fat coloured line instead.
    /// </remarks>
    private void Neon(SKCanvas canvas, SKPath path, SKColor colour, float width, float pulse)
    {
        foreach (var pass in Jukebox.Passes)
        {
            var alpha = (byte)Math.Clamp(pass.Alpha * pulse * 255f, 0f, 255f);

            _stroke.StrokeWidth = pass.Core ? Math.Max(1f, width * pass.Width) : width * pass.Width;
            _stroke.Color = pass.Core ? SKColors.White.WithAlpha(alpha) : colour.WithAlpha(alpha);

            canvas.DrawPath(path, _stroke);
        }
    }

    private void DrawTubes(
        SKCanvas canvas, float height, float cabinetLeft, float cabinetWidth,
        float archRadius, float shoulder, ArtPalette palette)
    {
        var tubeWidth = Jukebox.TubeWidth(cabinetWidth);
        var top = height * 0.09f;
        var bottom = shoulder + (archRadius * 0.10f);

        for (var tube = 0; tube < 2; tube++)
        {
            var centreX = Jukebox.TubeCentre(cabinetLeft, cabinetWidth, tube);
            var glass = SKRect.Create(centreX - (tubeWidth / 2f), top, tubeWidth, bottom - top);
            var corner = tubeWidth / 2f;

            _fill.Shader = null;
            _fill.Color = new SKColor(5, 5, 5, 217);
            canvas.DrawRoundRect(glass, corner, corner, _fill);

            canvas.Save();

            using (var clip = new SKPath())
            {
                clip.AddRoundRect(glass, corner, corner);
                canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);
                DrawBubbles(canvas, tube, centreX, tubeWidth, top, bottom, palette);
            }

            canvas.Restore();

            using var outline = new SKPath();
            outline.AddRoundRect(glass, corner, corner);

            Neon(canvas, outline, palette.Accent.ToSkia(), Math.Max(1f, cabinetWidth * 0.004f), 0.5f);
        }
    }

    /// <summary>
    /// The bubbles, each drawn three times so a filled circle reads as lit glass.
    /// </summary>
    private void DrawBubbles(
        SKCanvas canvas, int tube, float centreX, float tubeWidth,
        float top, float bottom, ArtPalette palette)
    {
        var swatches = palette.Swatches;

        foreach (var bubble in _bubbles)
        {
            if (bubble.Tube != tube) continue;

            var radius = tubeWidth * bubble.Size;
            var x = centreX + (bubble.Across * tubeWidth * 0.30f);

            // The tube's own space runs up the screen, so a bubble at nought is
            // at the bottom of it.
            var y = bottom - (bubble.Up * (bottom - top));

            var colour = swatches.Count == 0
                ? palette.Accent.ToSkia()
                : swatches[bubble.Tint % swatches.Count].ToSkia();

            var outer = radius * 2.1f;

            canvas.DrawBitmap(
                Glow(colour),
                SKRect.Create(x - outer, y - outer, outer * 2f, outer * 2f),
                _blit);

            _fill.Shader = null;
            _fill.Color = colour.WithAlpha(204);
            canvas.DrawCircle(x, y, radius, _fill);

            // Up and to the left of centre, which is where a highlight lands if
            // the room's light is where every other highlight in this style
            // says it is.
            _fill.Color = SKColors.White.WithAlpha(153);
            canvas.DrawOval(
                SKRect.Create(x - (radius * 0.30f), y - (radius * 0.65f), radius * 0.55f, radius * 0.55f),
                _fill);
        }
    }

    /// <summary>
    /// The halo round a bubble, made once per colour and stretched.
    /// </summary>
    /// <remarks>
    /// The gradient's stops are fractions of the sprite, so stretching it to any
    /// bubble's size gives the same falloff the shader would have: the solid
    /// core out to two fifths of the radius, fading to nothing at the edge.
    /// </remarks>
    private SKBitmap Glow(SKColor colour)
    {
        if (_glows.TryGetValue((uint)colour, out var cached)) return cached;

        const int side = 64;
        const float centre = side / 2f;

        var bitmap = new SKBitmap(new SKImageInfo(side, side, SKColorType.Bgra8888, SKAlphaType.Premul));

        using (var surface = new SKCanvas(bitmap))
        {
            surface.Clear(SKColors.Transparent);

            using var shader = SKShader.CreateRadialGradient(
                new SKPoint(centre, centre), centre,
                new[] { colour.WithAlpha(140), colour.WithAlpha(0) },
                new[] { 0.4f / 2.1f, 1f },
                SKShaderTileMode.Clamp);

            using var paint = new SKPaint { IsAntialias = true, Shader = shader };
            surface.DrawCircle(centre, centre, centre, paint);
        }

        _glows[(uint)colour] = bitmap;
        return bitmap;
    }

    private void DrawDisplay(
        SKCanvas canvas, SKRect display, SKRect well, int index, int previous,
        ArtPalette palette, float pulse, double phase)
    {
        _fill.Shader = null;
        _fill.Color = new SKColor(8, 8, 8, 242);
        canvas.DrawRoundRect(well, 8f, 8f, _fill);

        Drawing.DrawFeaturedCover(
            canvas, _data.ImageFor(index), _data.ImageFor(previous), display,
            _featured.FadeRaw(phase), radius: 4f, shadowAlpha: 0f);

        // The glass rim breathes with the arch, which is what ties the window to
        // the machine around it.
        _stroke.StrokeWidth = 2f;
        _stroke.Color = palette.Accent.ToSkia(0.8f * pulse);
        canvas.DrawRoundRect(well, 8f, 8f, _stroke);
    }

    /// <summary>
    /// Five paper strips, newest first, the way the titles are listed on a real
    /// machine.
    /// </summary>
    private void DrawStrips(
        SKCanvas canvas, float width, float height, float cabinetWidth,
        SKRect well, int featured, ArtPalette palette)
    {
        var albums = _data.Albums;

        var stripWidth = cabinetWidth * 0.74f;
        var left = (width * 0.5f) - (stripWidth / 2f);
        var cursor = well.Bottom + (height * 0.045f);

        var (rows, rowHeight) = Jukebox.StripRows(height, cursor);
        if (rows <= 0) return;

        var size = Math.Max(9f, rowHeight * 0.42f);

        if (_stripFont is null || Math.Abs(_stripFontFor - size) > 0.01f)
        {
            _stripFont?.Dispose();
            _stripFont = TextLayout.Paint(size, false, SKColors.Black);
            _stripFontFor = size;
        }

        for (var i = 0; i < rows && i < albums.Count; i++)
        {
            var now = Jukebox.IsNowPlaying(i, _data.LiveAlbumIndex, featured);

            var row = SKRect.Create(left, cursor + (rowHeight * 0.14f), stripWidth, rowHeight * 0.86f);

            _fill.Shader = null;
            _fill.Color = Card.WithAlpha((byte)(now ? 255 : 209));
            canvas.DrawRoundRect(row, 2f, 2f, _fill);

            if (now)
            {
                _fill.Color = palette.Accent.ToSkia();
                canvas.DrawRect(
                    SKRect.Create(row.Left, row.Top, row.Width * 0.012f, row.Height), _fill);
            }

            var label = now
                ? Live(featured)
                : $"{albums[i].Name} — {albums[i].Artist}";

            _stripFont.Typeface = TextLayout.Face(now);
            _stripFont.Color = new SKColor(36, 36, 36);

            var inset = row.Width * 0.03f;

            TextLayout.DrawLine(
                canvas,
                Ellipsised(label, _stripFont, row.Width - (inset * 2f)),
                row.Left + inset,
                row.Top + (row.Height * 0.22f) - _stripFont.FontMetrics.Ascent,
                _stripFont);

            cursor += rowHeight;
        }
    }

    private string Live(int index)
    {
        var (title, artist, _) = FeaturedText.For(_data, index);
        return $"{title} — {artist}";
    }

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

    public void Dispose()
    {
        _fill.Dispose();
        _stroke.Dispose();
        _stripFont?.Dispose();
        _blit.Dispose();
        _cabinet.Dispose();

        foreach (var glow in _glows.Values) glow.Dispose();
        _glows.Clear();
    }
}

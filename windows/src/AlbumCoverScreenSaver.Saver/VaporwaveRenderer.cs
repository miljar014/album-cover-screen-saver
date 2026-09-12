using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// Vaporwave Grid. A sun setting over a neon grid, with the cover floating above
/// the horizon.
/// </summary>
/// <remarks>
/// <para>
/// <b>The one style that does not take its colours from the album.</b> Every
/// other style built so far reads its scheme out of the cover; this one is
/// hard-coded magenta, cyan and violet, because the genre simply requires those
/// colours and a record that happens to be brown does not get a brown vaporwave.
/// The album recolours exactly one line of type, the artist, and nothing else.
/// </para>
/// <para>
/// It also has no idle state to speak of. Nothing about the scene changes when
/// the music stops except the words, so it looks the same running unattended at
/// three in the morning as it does while you are listening.
/// </para>
/// </remarks>
internal sealed class VaporwaveRenderer : IStyleRenderer, IDisposable
{
    private static readonly SKColor Magenta = new(250, 51, 158);
    private static readonly SKColor Cyan = new(64, 240, 250);
    private static readonly SKColor Violet = new(61, 20, 92);
    private static readonly SKColor SkyTop = new(13, 5, 36);
    private static readonly SKColor GroundNear = new(41, 5, 66);
    private static readonly SKColor GroundFar = new(15, 3, 31);
    private static readonly SKColor SunCore = new(255, 230, 89);
    private static readonly SKColor SunSlot = new(26, 8, 51, 230);

    private readonly SaverData _data;
    private readonly AlbumPicker _picker;
    private readonly PaletteStore _palettes;
    private readonly bool _isPreview;

    private readonly Featured _featured;
    private readonly SKPaint _fill = new() { IsAntialias = true };
    private readonly SKPaint _line = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1.4f,
    };

    public VaporwaveRenderer(SaverData data, AlbumPicker picker, PaletteStore palettes, bool isPreview)
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
        Log.Write($"vaporwave layout: {width:0}x{height:0} points");
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

        var horizon = height * (1f - VaporwaveGrid.HorizonFraction);
        var sunRadius = Math.Min(width, height) * 0.20f;

        DrawSky(canvas, width, horizon);
        DrawSun(canvas, width, horizon, sunRadius);
        DrawGround(canvas, width, height, horizon);
        DrawRays(canvas, width, height, horizon);
        DrawRungs(canvas, width, height, horizon, phase);

        _fill.Shader = null;
        _fill.Color = Magenta.WithAlpha(230);
        canvas.DrawRect(SKRect.Create(0, horizon - 1.5f, width, 3f), _fill);

        var side = Math.Min(height * 0.34f, width * 0.26f);
        var bob = MathF.Sin((float)phase * 0.7f) * height * 0.008f;

        // Floating above the horizon, not standing on it.
        var rect = SKRect.Create(
            (width * 0.5f) - (side / 2f),
            horizon - (height * 0.10f) - bob - side,
            side, side);

        DrawGlow(canvas, rect, side);

        Drawing.DrawFeaturedCover(
            canvas, _data.ImageFor(index), _data.ImageFor(previous), rect,
            _featured.FadeRaw(phase), radius: 2f, shadowAlpha: 0.7f);

        DrawNeonFrame(canvas, rect);

        if (_data.Settings.ShowTrackLabel && !_isPreview)
        {
            DrawText(canvas, width, height, rect, index, palette);
        }
    }

    private void DrawSky(SKCanvas canvas, float width, float horizon)
    {
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, horizon),
            new[] { SkyTop, Violet, Magenta.WithAlpha(191) },
            SKShaderTileMode.Clamp);

        _fill.Shader = shader;
        _fill.Color = SKColors.White;
        canvas.DrawRect(SKRect.Create(0, 0, width, horizon), _fill);
        _fill.Shader = null;
    }

    /// <summary>
    /// The sun, cut across by bands that widen as they go down.
    /// </summary>
    /// <remarks>
    /// The widening is the signature of the genre. Bands of a fixed width give
    /// you a striped circle; bands that grow give you a sun dissolving into the
    /// horizon, which is the whole image.
    /// </remarks>
    private void DrawSun(SKCanvas canvas, float width, float horizon, float radius)
    {
        var centre = new SKPoint(width * 0.5f, horizon - (radius * 0.62f));

        canvas.Save();
        canvas.ClipRect(SKRect.Create(
            centre.X - radius, centre.Y - radius, radius * 2f, radius * 2f));
        canvas.ClipPath(CirclePath(centre, radius), SKClipOperation.Intersect, antialias: true);

        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(0, centre.Y - radius),
                   new SKPoint(0, centre.Y + radius),
                   new[] { SunCore, Magenta },
                   SKShaderTileMode.Clamp))
        {
            _fill.Shader = shader;
            _fill.Color = SKColors.White;
            canvas.DrawCircle(centre, radius, _fill);
            _fill.Shader = null;
        }

        _fill.Color = SunSlot;
        foreach (var (down, band) in VaporwaveGrid.SunSlots(radius))
        {
            canvas.DrawRect(
                SKRect.Create(centre.X - radius, centre.Y + down, radius * 2f, band), _fill);
        }

        canvas.Restore();
    }

    private static SKPath CirclePath(SKPoint centre, float radius)
    {
        var path = new SKPath();
        path.AddCircle(centre.X, centre.Y, radius);
        return path;
    }

    private void DrawGround(SKCanvas canvas, float width, float height, float horizon)
    {
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, horizon),
            new SKPoint(0, height),
            new[] { GroundFar, GroundNear },
            SKShaderTileMode.Clamp);

        _fill.Shader = shader;
        _fill.Color = SKColors.White;
        canvas.DrawRect(SKRect.Create(0, horizon, width, height - horizon), _fill);
        _fill.Shader = null;
    }

    /// <summary>The lines running away to the vanishing point.</summary>
    private void DrawRays(SKCanvas canvas, float width, float height, float horizon)
    {
        _line.Color = Cyan.WithAlpha(191);

        for (var i = -VaporwaveGrid.Rays; i <= VaporwaveGrid.Rays; i++)
        {
            var (atHorizon, atBottom) = VaporwaveGrid.Ray(i);

            canvas.DrawLine(
                (width * 0.5f) + (atHorizon * width), horizon,
                (width * 0.5f) + (atBottom * width), height,
                _line);
        }
    }

    /// <summary>
    /// The rungs rushing toward the viewer, at exactly one per second.
    /// </summary>
    /// <remarks>
    /// Driven off the fractional part of the clock rather than an accumulating
    /// total, so the wrap is exact. Integrate a delta instead and it drifts,
    /// which shows as a hitch once a second for as long as the saver runs.
    /// </remarks>
    private void DrawRungs(SKCanvas canvas, float width, float height, float horizon, double phase)
    {
        var cycle = VaporwaveGrid.Cycle(phase);
        var span = height - horizon;

        for (var k = 0; k < VaporwaveGrid.Rungs; k++)
        {
            var rung = VaporwaveGrid.RungAt(k, cycle);
            if (rung.Alpha <= 0.004f) continue;

            _line.Color = Cyan.WithAlpha((byte)Math.Clamp(rung.Alpha * 255f, 0f, 255f));

            var y = horizon + (rung.DownFromHorizon * span);
            canvas.DrawLine(0, y, width, y, _line);
        }
    }

    private void DrawGlow(SKCanvas canvas, SKRect rect, float side)
    {
        var outer = side * 1.15f;

        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(rect.MidX, rect.MidY), outer,
            new[] { Cyan.WithAlpha(128), Cyan.WithAlpha(0) },
            new[] { side * 0.5f / outer, 1f },
            SKShaderTileMode.Clamp);

        _fill.Shader = shader;
        _fill.Color = SKColors.White;
        canvas.DrawCircle(rect.MidX, rect.MidY, outer, _fill);
        _fill.Shader = null;
    }

    /// <summary>
    /// A bright thin core inside a wider saturated stroke, which is the cheap
    /// way to draw neon in two dimensions.
    /// </summary>
    private void DrawNeonFrame(SKCanvas canvas, SKRect rect)
    {
        _line.StrokeWidth = 3f;
        _line.Color = Magenta;
        canvas.DrawRect(rect, _line);

        _line.StrokeWidth = 1f;
        _line.Color = Cyan;
        canvas.DrawRect(rect, _line);

        _line.StrokeWidth = 1.4f;
    }

    /// <summary>
    /// The title, drawn three times in three colours slightly apart.
    /// </summary>
    /// <remarks>
    /// A fake of the colour fringing you get from a misaligned screen. Cyan
    /// pushed one way, magenta the other, white in the middle. It costs two
    /// extra text draws a frame and it is most of what makes the style read as
    /// the era it is imitating.
    /// </remarks>
    private void DrawText(
        SKCanvas canvas, float width, float height, SKRect rect, int index, ArtPalette palette)
    {
        var (title, artist, _) = FeaturedText.For(_data, index);

        var columnWidth = width * 0.8f;
        var left = (width * 0.5f) - (columnWidth / 2f);
        var top = rect.Bottom + (height * 0.03f);
        var offset = Math.Max(2f, height * 0.004f);

        var shout = title.ToUpperInvariant();
        var size = TextLayout.Fitted(
            shout, columnWidth, height * 0.11f, Math.Max(20f, height * 0.052f), bold: true);

        using var fringe = TextLayout.Paint(size, true, Cyan.WithAlpha(230));
        TextLayout.Draw(canvas, shout, left - offset, top, columnWidth, fringe, centred: true, tracking: 4f);

        fringe.Color = Magenta.WithAlpha(230);
        TextLayout.Draw(canvas, shout, left + offset, top, columnWidth, fringe, centred: true, tracking: 4f);

        fringe.Color = SKColors.White;
        var used = TextLayout.Draw(canvas, shout, left, top, columnWidth, fringe, centred: true, tracking: 4f);

        // The one line the album is allowed to colour.
        using var artistPaint = TextLayout.Paint(
            Math.Max(12f, height * 0.024f), false, palette.Accent.ToSkia());

        TextLayout.Draw(
            canvas, artist.ToUpperInvariant(), left, top + used + (height * 0.014f),
            columnWidth, artistPaint, centred: true, tracking: 6f);
    }

    public void Dispose()
    {
        _fill.Dispose();
        _line.Dispose();
    }
}

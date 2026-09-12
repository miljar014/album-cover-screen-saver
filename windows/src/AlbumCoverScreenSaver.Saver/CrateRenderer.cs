using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// Crate Digging. A row of sleeves leaning back into a box, sliding past one at
/// a time, with the front one named in a column beside it.
/// </summary>
/// <remarks>
/// <para>
/// This style does not use the shared featured-album machinery at all. The crate
/// has its own motion and the sleeve at the front simply <em>is</em> the featured
/// one, so there is no crossfade and nothing to keep in step.
/// </para>
/// <para>
/// When something starts playing it is pushed in at the front immediately, with
/// the crate offset set a full sleeve back so it slides in from behind the
/// current one rather than appearing on top of it. And while music is playing
/// the crate stops recycling: it only ever receives what you actually put on.
/// </para>
/// </remarks>
internal sealed class CrateRenderer : IStyleRenderer, IDisposable
{
    private readonly SaverData _data;
    private readonly AlbumPicker _picker;
    private readonly PaletteStore _palettes;
    private readonly bool _isPreview;

    private readonly List<int> _sleeves = [];
    private readonly SKPaint _fill = new() { IsAntialias = true };
    private readonly HashSet<int> _onScreen = [];

    private float _offset;

    public CrateRenderer(SaverData data, AlbumPicker picker, PaletteStore palettes, bool isPreview)
    {
        _data = data;
        _picker = picker;
        _palettes = palettes;
        _isPreview = isPreview;
    }

    private int Front => _sleeves.Count > 0 ? _sleeves[0] : 0;

    public void BuildLayout(float width, float height)
    {
        _sleeves.Clear();
        _offset = 0;

        var albums = _data.Albums.Count;
        if (albums == 0) return;

        var used = new HashSet<int>();

        // Whatever is playing is at the front from the start.
        var first = _data.LiveAlbumIndex ?? _picker.Pick(albums, _data.Settings.RecencyBias);
        _sleeves.Add(first);
        used.Add(first);

        for (var i = 1; i < CrateMath.Want(_data.Settings.CrateCount); i++)
        {
            var index = _picker.PickAvoiding(albums, _data.Settings.RecencyBias, used);
            used.Add(index);
            _sleeves.Add(index);
        }

        Log.Write($"crate layout: {_sleeves.Count} sleeves across {width:0}x{height:0} points");
    }

    public void Advance(double phase, float width, float height)
    {
        var albums = _data.Albums.Count;
        if (albums == 0 || _sleeves.Count == 0) return;

        var spacing = CrateMath.Spacing(width, height);

        // Pushed in a full sleeve back, so it slides in from behind the one at
        // the front rather than popping into place on top of it.
        if (_data.LiveAlbumIndex is { } live && _sleeves[0] != live)
        {
            _sleeves.Insert(0, live);
            _offset = spacing;

            var cap = CrateMath.RetentionCap(_data.Settings.CrateCount);
            while (_sleeves.Count > cap) _sleeves.RemoveAt(_sleeves.Count - 1);
        }

        _offset += (float)(TileField.FrameInterval * spacing / CrateMath.Dwell(_data.Settings.FeatureSeconds));

        if (_offset < spacing) return;

        _offset -= spacing;

        // Only when nothing is playing. A crate that kept recycling under live
        // playback would throw away the record you just put on.
        if (_data.LiveAlbumIndex is not null) return;

        _sleeves.RemoveAt(0);

        _onScreen.Clear();
        foreach (var sleeve in _sleeves) _onScreen.Add(sleeve);
        _sleeves.Add(_picker.PickAvoiding(albums, _data.Settings.RecencyBias, _onScreen));
    }

    public void Draw(SKCanvas canvas, float width, float height, double phase)
    {
        var albums = _data.Albums;
        if (albums.Count == 0 || _sleeves.Count == 0) return;

        var front = Math.Clamp(Front, 0, albums.Count - 1);
        var palette = _palettes.For(albums[front].Id, _data.ImageFor(front));

        DrawBackground(canvas, width, height, palette);

        var side = CrateMath.Side(width, height);
        var spacing = CrateMath.Spacing(width, height);
        var baseBottom = height - (height * 0.34f);
        var frontX = width * 0.13f;

        // Back to front, so nearer sleeves paint over the ones behind them.
        for (var i = _sleeves.Count - 1; i >= 0; i--)
        {
            var x = frontX + (i * spacing) - _offset;
            if (x >= width * 1.05f) continue;

            var depth = CrateMath.DepthAt(i);
            var s = side * CrateMath.ScaleOf(depth);

            // Deeper sleeves sit higher, which is the second depth cue after
            // size and the one people cannot name.
            var bottom = baseBottom - (CrateMath.RiseOf(depth) * height);
            var rect = SKRect.Create(x, bottom - s, s, s);

            canvas.Save();
            canvas.RotateRadians(CrateMath.LeanOf(depth), rect.MidX, rect.MidY);

            Drawing.DrawCover(canvas, _data.ImageFor(_sleeves[i]), rect, 1f, radius: 2f, shadowAlpha: 0.55f);

            _fill.Shader = null;
            _fill.Color = palette.Deep.ToSkia(CrateMath.HazeOf(depth));
            canvas.DrawRect(rect, _fill);

            // The light catching the edge where the next sleeve would be.
            _fill.Color = palette.Accent.ToSkia(0.20f);
            canvas.DrawRect(SKRect.Create(rect.Left, rect.Top, 2f, rect.Height), _fill);

            canvas.Restore();
        }

        DrawCrateFront(canvas, width, height, side, palette);

        if (_data.Settings.ShowTrackLabel && !_isPreview)
        {
            DrawColumn(canvas, width, height, front, palette);
        }
    }

    private void DrawBackground(SKCanvas canvas, float width, float height, ArtPalette palette)
    {
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, height),
            new[]
            {
                palette.Deep.WithBrightness(0.15f).ToSkia(),
                palette.Deep.WithBrightness(0.04f).ToSkia(),
            },
            SKShaderTileMode.Clamp);

        _fill.Shader = shader;
        _fill.Color = SKColors.White;
        canvas.DrawRect(SKRect.Create(0, 0, width, height), _fill);
        _fill.Shader = null;
    }

    /// <summary>
    /// The front board of the crate, which is what the sleeves stand in.
    /// </summary>
    /// <remarks>
    /// It hides the point where the sleeves would otherwise meet the floor, and
    /// without it the whole thing reads as records floating in a gradient.
    /// </remarks>
    private void DrawCrateFront(
        SKCanvas canvas, float width, float height, float side, ArtPalette palette)
    {
        var top = height - (height * 0.34f) - (side * 0.30f);
        var crate = SKRect.Create(0, top, width, height - top);

        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(0, crate.Top),
                   new SKPoint(0, crate.Bottom),
                   new[]
                   {
                       palette.ConsoleLight.ToSkia(),
                       palette.Console.WithBrightness(0.14f).ToSkia(),
                   },
                   SKShaderTileMode.Clamp))
        {
            _fill.Shader = shader;
            _fill.Color = SKColors.White;
            canvas.DrawRect(crate, _fill);
            _fill.Shader = null;
        }

        _fill.Color = palette.Accent.ToSkia(0.45f);
        canvas.DrawRect(SKRect.Create(0, crate.Top, width, 3f), _fill);
    }

    /// <summary>
    /// The column beside the crate: what it is, and whether you are listening to
    /// it or only looking at it.
    /// </summary>
    private void DrawColumn(SKCanvas canvas, float width, float height, int front, ArtPalette palette)
    {
        var columnWidth = width * 0.42f;
        var left = width * 0.54f;
        var top = height - (height * 0.78f);

        using var shadow = SKImageFilter.CreateDropShadow(
            0f, 2f, 8f, 8f, SKColors.Black.WithAlpha(191));

        var live = _data.LiveAlbumIndex == front;
        var (title, artist, sub) = FeaturedText.For(_data, front);

        using var kicker = TextLayout.Paint(
            Math.Max(10f, height * 0.017f), true, palette.Accent.ToSkia());
        kicker.ImageFilter = shadow;

        TextLayout.Draw(
            canvas, live ? "NOW PLAYING" : "IN THE CRATE", left, top, columnWidth,
            kicker, centred: false, tracking: 3f);

        // A fixed step rather than the measured height, so the title always
        // starts in the same place whatever the kicker did.
        top += height * 0.048f;

        var titleSize = TextLayout.Fitted(
            title, columnWidth, height * 0.16f, Math.Max(20f, height * 0.048f), bold: true);

        using var titlePaint = TextLayout.Paint(titleSize, true, palette.Text.ToSkia());
        titlePaint.ImageFilter = shadow;
        top += TextLayout.Draw(canvas, title, left, top, columnWidth, titlePaint);
        top += height * 0.014f;

        using var artistPaint = TextLayout.Paint(
            Math.Max(13f, height * 0.028f), false, palette.Accent.ToSkia());
        artistPaint.ImageFilter = shadow;
        top += TextLayout.Draw(canvas, artist, left, top, columnWidth, artistPaint);

        if (string.IsNullOrEmpty(sub)) return;

        top += height * 0.010f;

        using var subPaint = TextLayout.Paint(
            Math.Max(11f, height * 0.021f), false, palette.TextMuted.ToSkia());
        subPaint.ImageFilter = shadow;
        TextLayout.Draw(canvas, sub, left, top, columnWidth, subPaint);
    }

    public void Dispose() => _fill.Dispose();
}

using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>One cover on its own orbit.</summary>
internal sealed class OrbitingCover
{
    public int Index { get; init; }
    public float Radius { get; init; }
    public float Size { get; init; }
    public double Speed { get; init; }
    public double Angle { get; set; }
}

/// <summary>
/// Starfield Orbit. One record as a sun, the rest circling it, against a sky
/// that is the colour of whatever is playing.
/// </summary>
/// <remarks>
/// <para>
/// Along with Ambient Field, this is the style where almost nothing is a fixed
/// colour. Space itself is the album's average colour held very dark, and the
/// glow around the sun is its most characterful one. Put a red record on and you
/// are looking into a faintly red void.
/// </para>
/// <para>
/// The only things that stay their own colour are the stars, which are white,
/// and the shading on the covers, which is black. Everything else changes hue
/// the moment the track does.
/// </para>
/// <para>
/// There is no depth sorting to speak of: the covers on the far side of the
/// orbit are drawn, then the sun, then the covers on the near side. That is
/// enough, because the sun is the only thing they can ever pass in front of.
/// </para>
/// </remarks>
internal sealed class StarfieldRenderer : IStyleRenderer, IDisposable
{
    private readonly SaverData _data;
    private readonly AlbumPicker _picker;
    private readonly PaletteStore _palettes;
    private readonly Random _random;
    private readonly bool _isPreview;

    private readonly Featured _featured;
    private readonly List<OrbitingCover> _orbiters = [];
    private readonly SKPaint _fill = new() { IsAntialias = true };

    public StarfieldRenderer(
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
        _orbiters.Clear();

        var albums = _data.Albums.Count;
        if (albums == 0) return;

        _featured.Reset(0, _picker.Pick(albums, _data.Settings.RecencyBias));

        var wanted = Starfield.OrbiterCount(_data.Settings.OrbitCount, _isPreview);
        var used = new HashSet<int>();

        for (var i = 0; i < wanted; i++)
        {
            var index = _picker.PickAvoiding(albums, _data.Settings.RecencyBias, used);
            used.Add(index);

            var radius = (float)(0.20 + (_random.NextDouble() * 0.32));

            _orbiters.Add(new OrbitingCover
            {
                Index = index,
                Radius = radius,
                Size = (float)(0.075 + (_random.NextDouble() * 0.060)),
                Angle = _random.NextDouble() * Math.PI * 2,
                Speed = Starfield.SpeedAt(radius, slower: _random.Next(2) == 0),
            });
        }

        Log.Write($"starfield layout: {_orbiters.Count} orbiters across {width:0}x{height:0} points");
    }

    public void Advance(double phase, float width, float height)
    {
        _featured.Advance(phase, _data.LiveAlbumIndex, _data.Settings.FeatureSeconds, _data.Albums.Count);

        // The orbiters are a fixed set chosen at layout time and are not
        // affected by playback at all. Only the sun follows the music.
        var step = TileField.FrameInterval * Math.Max(0.05, _data.Settings.OrbitSpeed);
        foreach (var orbiter in _orbiters) orbiter.Angle += orbiter.Speed * step;
    }

    public void Draw(SKCanvas canvas, float width, float height, double phase)
    {
        var albums = _data.Albums;
        if (albums.Count == 0) return;

        var index = Math.Clamp(_featured.Index, 0, albums.Count - 1);
        var previous = Math.Clamp(_featured.PreviousIndex, 0, albums.Count - 1);
        var palette = _palettes.For(albums[index].Id, _data.ImageFor(index));

        var shortest = Math.Min(width, height);
        var centreX = width * 0.5f;

        // The sun sits slightly below centre in the specification's y-up space,
        // which is slightly above it here.
        var centreY = height * 0.46f;
        var sunSide = Math.Min(height * 0.30f, width * 0.22f);

        DrawVoid(canvas, width, height, shortest, palette);
        DrawStars(canvas, width, height, phase);

        foreach (var orbiter in _orbiters)
        {
            if (Starfield.IsBehind(orbiter.Angle)) DrawOrbiter(canvas, orbiter, centreX, centreY, shortest);
        }

        DrawCorona(canvas, centreX, centreY, sunSide, palette);

        Drawing.DrawFeaturedCover(
            canvas, _data.ImageFor(index), _data.ImageFor(previous),
            SKRect.Create(centreX - (sunSide / 2f), centreY - (sunSide / 2f), sunSide, sunSide),
            _featured.FadeRaw(phase), radius: 6f, shadowAlpha: 0.5f);

        foreach (var orbiter in _orbiters)
        {
            if (!Starfield.IsBehind(orbiter.Angle)) DrawOrbiter(canvas, orbiter, centreX, centreY, shortest);
        }

        if (_data.Settings.ShowTrackLabel && !_isPreview)
        {
            DrawText(canvas, width, height, centreY, sunSide, index, palette);
        }
    }

    private void DrawVoid(SKCanvas canvas, float width, float height, float shortest, ArtPalette palette)
    {
        var radius = shortest * 0.95f;

        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(width * 0.5f, height * 0.5f), radius,
            new[] { palette.Deep.WithBrightness(0.13f).ToSkia(), SKColors.Black },
            SKShaderTileMode.Clamp);

        _fill.Shader = shader;
        _fill.Color = SKColors.White;
        canvas.DrawRect(SKRect.Create(0, 0, width, height), _fill);
        _fill.Shader = null;
    }

    /// <summary>
    /// Two hundred and twenty stars, computed rather than stored.
    /// </summary>
    /// <remarks>
    /// Each one drifts sideways at a rate set by its own depth and wraps round
    /// the screen, so near stars visibly outrun far ones. That parallax is the
    /// only thing making the sky look like it has any depth to it, and it is
    /// also what keeps the stars from reading as part of the same flat plane as
    /// the covers.
    /// </remarks>
    private void DrawStars(SKCanvas canvas, float width, float height, double phase)
    {
        _fill.Shader = null;

        for (var i = 0; i < Starfield.Count; i++)
        {
            var star = Starfield.At(i);

            var x = (float)(((star.X01 * width) + Starfield.DriftOf(phase, star.Depth)) % width);
            var y = (float)(star.Y01 * height);
            var size = Starfield.SizeOf(star.Depth);

            _fill.Color = SKColors.White.WithAlpha(
                (byte)Math.Clamp(Starfield.AlphaOf(star.Depth) * 255f, 0f, 255f));

            // The specification draws these from a corner rather than a centre.
            // A sub-pixel difference, and preserved only because it costs
            // nothing to preserve.
            canvas.DrawOval(SKRect.Create(x, y, size, size), _fill);
        }
    }

    private void DrawCorona(SKCanvas canvas, float centreX, float centreY, float sunSide, ArtPalette palette)
    {
        var outer = sunSide * 1.5f;

        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(centreX, centreY), outer,
            new[] { palette.Accent.ToSkia(0.42f), palette.Accent.ToSkia(0f) },
            new[] { sunSide * 0.45f / outer, 1f },
            SKShaderTileMode.Clamp);

        _fill.Shader = shader;
        _fill.Color = SKColors.White;
        canvas.DrawCircle(centreX, centreY, outer, _fill);
        _fill.Shader = null;
    }

    private void DrawOrbiter(
        SKCanvas canvas, OrbitingCover orbiter, float centreX, float centreY, float shortest)
    {
        var placed = Starfield.PlaceOrbiter(orbiter.Angle, shortest, orbiter.Radius, orbiter.Size);

        var x = centreX + placed.OffsetX;

        // OffsetUp is away from the viewer in the specification's y-up space.
        var y = centreY - placed.OffsetUp;

        var rect = SKRect.Create(x - (placed.Size / 2f), y - (placed.Size / 2f), placed.Size, placed.Size);

        Drawing.DrawCover(canvas, _data.ImageFor(orbiter.Index), rect, 1f, radius: 2f, shadowAlpha: 0.5f);

        // Shaded by how far away it is, so a cover behind the sun sits back into
        // the dark rather than being merely smaller.
        _fill.Shader = null;
        _fill.Color = SKColors.Black.WithAlpha((byte)Math.Clamp(placed.Depth * 0.45f * 255f, 0f, 255f));
        canvas.DrawRect(rect, _fill);
    }

    private void DrawText(
        SKCanvas canvas, float width, float height, float centreY, float sunSide,
        int index, ArtPalette palette)
    {
        var (title, artist, _) = FeaturedText.For(_data, index);

        var columnWidth = width * 0.6f;
        var left = (width * 0.5f) - (columnWidth / 2f);
        var top = centreY + (sunSide * 0.5f) + (height * 0.045f);

        using var shadow = SKImageFilter.CreateDropShadow(
            0f, 2f, 9f, 9f, SKColors.Black.WithAlpha(217));

        var titleSize = TextLayout.Fitted(
            title, columnWidth, height * 0.11f, Math.Max(18f, height * 0.042f), bold: true);

        using var titlePaint = TextLayout.Paint(titleSize, true, palette.Text.ToSkia());
        titlePaint.ImageFilter = shadow;
        top += TextLayout.Draw(canvas, title, left, top, columnWidth, titlePaint, centred: true);
        top += height * 0.012f;

        using var artistPaint = TextLayout.Paint(
            Math.Max(12f, height * 0.026f), false, palette.Accent.ToSkia());
        artistPaint.ImageFilter = shadow;
        TextLayout.Draw(canvas, artist, left, top, columnWidth, artistPaint, centred: true);
    }

    public void Dispose() => _fill.Dispose();
}

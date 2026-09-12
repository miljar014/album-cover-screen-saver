using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// Record Player. A mid-century turntable seen from above, on a wood plinth, in
/// a room lit by the cover itself, with every record played this session
/// scattered around it.
/// </summary>
/// <remarks>
/// <para>
/// The arm crosses the record in step with the actual song, which is the reason
/// the style is worth building at all.
/// </para>
/// <para>
/// The table is the other reason, and it is the part most easily got wrong. It
/// is a record of the evening's listening, not decoration: sleeves accumulate,
/// keep their positions, and are identified by album id rather than by index,
/// because the archive is rebuilt newest-first every time a song is recorded and
/// an index would quietly repaint an existing sleeve with somebody else's cover.
/// </para>
/// </remarks>
internal sealed class VinylRenderer : IStyleRenderer, IDisposable
{
    private readonly SaverData _data;
    private readonly AlbumPicker _picker;
    private readonly PaletteStore _palettes;
    private readonly BlurStore _blur;
    private readonly ScatterLayout _table;
    private readonly bool _isPreview;

    private readonly SKPaint _fill = new() { IsAntialias = true };
    private readonly SKPaint _stroke = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeCap = SKStrokeCap.Round,
    };

    // Up to twenty six sleeves, each wanting two blurred shadows, was fifty two
    // offscreen layers a frame. That is more than Drifting Float had when it was
    // reported as jumpy.
    private readonly ShadowSprite _shadows = new();
    private readonly SKPaint _sprite = new() { IsAntialias = true, FilterQuality = SKFilterQuality.Low };
    private readonly SKPaint _blit = new() { IsAntialias = true, FilterQuality = SKFilterQuality.Medium };

    // The plinth and its grain are sixty four wandering polylines that are
    // identical every frame.
    private readonly Layer _plinth = new();

    private int _index;
    private int _previousIndex;
    private double _startedAt;
    private double _changeStart = -1;
    private double _angle;
    private bool _seeded;

    public VinylRenderer(
        SaverData data, AlbumPicker picker, PaletteStore palettes,
        BlurStore blur, Random random, bool isPreview)
    {
        _data = data;
        _picker = picker;
        _palettes = palettes;
        _blur = blur;
        _table = new ScatterLayout(random);
        _isPreview = isPreview;
    }

    private float CentreX(float width) => width * (_isPreview ? 0.42f : 0.33f);

    private static float Radius(float width, float height) =>
        Math.Min(height * 0.34f, width * 0.25f);

    public void BuildLayout(float width, float height)
    {
        if (_data.Albums.Count == 0) return;

        _index = _data.LiveAlbumIndex ?? _picker.Pick(_data.Albums.Count, _data.Settings.RecencyBias);
        _previousIndex = _index;
        _startedAt = 0;
        _changeStart = -1;

        // Claiming only clears the board when a different style had it. A
        // rebuild here on every archive reload is the bug this guards against:
        // it wiped and re-scattered the entire pile each time a new song was
        // recorded.
        _table.Claim(CollageMode.Vinyl);

        var cap = Turntable.SleeveCap(_data.Settings.VinylSleeveCount, _isPreview);

        if (!_seeded)
        {
            _seeded = true;

            _table.Seed(
                _data.Albums.Select(album => album.Id).ToArray(), cap, 0,
                () => FindSpot(width, height));
        }

        _table.TrimTo(cap);

        Log.Write($"vinyl layout: {_table.Table.Count} sleeves across {width:0}x{height:0} points");
    }

    private TableSpot? FindSpot(float width, float height) =>
        _table.FindSpot(
            width, height,
            minSizeFraction: 0.15f, maxSizeFraction: 0.25f,
            scale: (float)Math.Max(0.4, _data.Settings.VinylSleeveScale),
            keepOutCentreX: CentreX(width),
            keepOutCentreY: height * 0.50f,
            keepOutRadius: Radius(width, height) * 1.40f,
            keepOutRect: (width * 0.56f, height * 0.18f, width * 0.42f, height * 0.64f));

    public void Advance(double phase, float width, float height)
    {
        var albums = _data.Albums.Count;
        if (albums == 0) return;

        if (_index >= albums) _index = 0;
        if (_previousIndex >= albums) _previousIndex = _index;

        _angle += Turntable.SpinStep(_data.Settings.VinylRpm, TileField.FrameInterval);

        if (_changeStart >= 0)
        {
            if (phase - _changeStart < Turntable.ChangeDuration) return;

            _changeStart = -1;
            _startedAt = phase;
            return;
        }

        if (_data.LiveAlbumIndex is { } live)
        {
            if (live != _index) Begin(phase, live);
            else _startedAt = phase;
        }
        else if (phase - _startedAt >=
                 Math.Max(Turntable.MinimumSecondsPerRecord, _data.Settings.VinylSecondsPerRecord))
        {
            Begin(phase, PickDifferent(albums));
        }

        Sync(phase, width, height);
    }

    private int PickDifferent(int albums)
    {
        if (albums <= 1) return _index;
        return _picker.PickAvoiding(albums, _data.Settings.RecencyBias, new HashSet<int> { _index });
    }

    private void Begin(double phase, int next)
    {
        _previousIndex = _index;
        _index = next;
        _changeStart = phase;
    }

    /// <summary>
    /// Adds whatever is on the platter to the table, then trims the oldest away.
    /// </summary>
    /// <remarks>
    /// Append and cap, never rebuild. Records must appear to pile up over an
    /// evening, which they cannot do if the pile is thrown away every time the
    /// archive is reloaded.
    /// </remarks>
    private void Sync(double phase, float width, float height)
    {
        var albums = _data.Albums;
        if (_index < 0 || _index >= albums.Count) return;

        var cap = Turntable.SleeveCap(_data.Settings.VinylSleeveCount, _isPreview);

        if (cap > 0) _table.Pin(albums[_index].Id, FindSpot(width, height), phase);

        _table.TrimTo(cap);
    }

    public void Draw(SKCanvas canvas, float width, float height, double phase)
    {
        var albums = _data.Albums;
        if (albums.Count == 0) return;

        var index = Math.Clamp(_index, 0, albums.Count - 1);
        var previous = Math.Clamp(_previousIndex, 0, albums.Count - 1);
        var palette = _palettes.For(albums[index].Id, _data.ImageFor(index));

        var t = _changeStart < 0 ? double.MaxValue : phase - _changeStart;
        var lift = _changeStart < 0 ? 0f : Turntable.ArmLift(t);
        var fade = _changeStart < 0 ? 1f : Turntable.Fade(t);

        var radius = Radius(width, height);
        var cx = CentreX(width);
        var cy = height * 0.50f;

        var scale = Layer.ScaleOf(canvas);

        DrawBackdrop(canvas, width, height, index, palette);
        DrawPlinth(canvas, width, height, scale, palette);
        DrawTable(canvas, width, height, phase, palette);
        DrawPlatter(canvas, cx, cy, radius, palette);

        if (fade < 1f) DrawRecord(canvas, cx, cy, radius, previous, palette, 1f - fade);
        if (fade > 0f) DrawRecord(canvas, cx, cy, radius, index, palette, fade);

        DrawArm(canvas, cx, cy, radius, palette, lift, phase);

        if (!_isPreview && _data.Settings.ShowTrackLabel)
        {
            DrawColumn(canvas, width, height, index, palette);
        }
    }

    /// <summary>
    /// The cover blurred into a wash, which is the room's only light.
    /// </summary>
    /// <remarks>
    /// Drawn into a square larger than the screen so no edge of the blur is
    /// visible, then knocked back with a flat wash. <b>No vignette.</b> An
    /// earlier version had one and it blacked out the plinth margin, which is
    /// the only place the backdrop shows at all. It should read as a dim lit
    /// room, not as a dark border.
    /// </remarks>
    private void DrawBackdrop(
        SKCanvas canvas, float width, float height, int index, ArtPalette palette)
    {
        var blurred = _blur.Blurred(_data.Albums[index].Id, _data.ImageFor(index), 140, 18);

        _fill.Shader = null;

        if (blurred is null)
        {
            using var shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(0, height),
                new[]
                {
                    palette.Deep.WithBrightness(0.16f).ToSkia(),
                    palette.Deep.WithBrightness(0.045f).ToSkia(),
                },
                SKShaderTileMode.Clamp);

            _fill.Shader = shader;
            _fill.Color = SKColors.White;
            canvas.DrawRect(SKRect.Create(0, 0, width, height), _fill);
            _fill.Shader = null;
            return;
        }

        var side = Math.Max(width, height) * 1.15f;

        Drawing.DrawImage(
            canvas, blurred,
            SKRect.Create((width - side) / 2f, (height - side) / 2f, side, side),
            1f, radius: 0f, shadow: false);

        _fill.Color = palette.Deep.ToSkia(0.32f);
        canvas.DrawRect(SKRect.Create(0, 0, width, height), _fill);
    }

    /// <summary>The wood the deck stands on, with its grain and its trim.</summary>
    private void DrawPlinth(
        SKCanvas canvas, float width, float height, float scale, ArtPalette palette)
    {
        // Wood, grain and trim are the same picture every frame, and the palette
        // is the only thing that ever changes them.
        var cached = _plinth.Get(
            width, height, scale, $"plinth|{palette.Console}|{palette.ConsoleLight}|{palette.Accent}|{palette.Deep}",
            surface => PaintPlinth(surface, width, height, palette));

        if (cached is not null)
        {
            canvas.DrawImage(cached, SKRect.Create(0, 0, width, height), _blit);
            return;
        }

        PaintPlinth(canvas, width, height, palette);
    }

    private void PaintPlinth(SKCanvas canvas, float width, float height, ArtPalette palette)
    {
        var plinth = SKRect.Create(
            width * 0.085f, height * 0.095f, width * 0.83f, height * 0.81f);

        var corner = plinth.Height * 0.05f;

        _sprite.Color = SKColors.White.WithAlpha(166);

        canvas.DrawBitmap(
            _shadows.Get(corner / plinth.Width, blurFraction: plinth.Height * 0.05f / plinth.Width),
            ShadowSprite.Placement(plinth, dropFraction: plinth.Height * 0.012f / plinth.Width),
            _sprite);

        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(plinth.Left, plinth.Top),
                   new SKPoint(plinth.Left + (plinth.Width * 0.34f), plinth.Bottom),
                   new[] { palette.ConsoleLight.ToSkia(), palette.Console.ToSkia() },
                   SKShaderTileMode.Clamp))
        {
            _fill.Shader = shader;
            _fill.Color = SKColors.White;
            canvas.DrawRoundRect(plinth, corner, corner, _fill);
            _fill.Shader = null;
        }

        canvas.Save();

        using (var clip = new SKPath())
        {
            clip.AddRoundRect(plinth, corner, corner);
            canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);
            DrawGrain(canvas, plinth, palette);
        }

        canvas.Restore();

        // The one detail tying the furniture to the record.
        _fill.Shader = null;
        _fill.Color = palette.Accent.ToSkia(0.5f);
        canvas.DrawRect(
            SKRect.Create(
                plinth.Left, plinth.Bottom - (plinth.Height * 0.055f),
                plinth.Width, Math.Max(1f, plinth.Height * 0.006f)),
            _fill);

        _stroke.StrokeWidth = 2f;
        _stroke.Color = palette.Deep.ToSkia(0.7f);
        canvas.DrawRoundRect(plinth, corner, corner, _stroke);
    }

    /// <summary>
    /// Sixty four wandering lines of grain.
    /// </summary>
    /// <remarks>
    /// Seeded from the line's own index and nothing else, so the wood does not
    /// crawl between frames. Any per-frame randomness here and the plinth
    /// shimmers like water.
    /// </remarks>
    private void DrawGrain(SKCanvas canvas, SKRect plinth, ArtPalette palette)
    {
        var step = plinth.Width / 12f;

        for (var i = 0; i < 64; i++)
        {
            var f = (float)Noise.Hash01(i);
            var g = (float)Noise.Fraction(Math.Sin(i * 78.233) * 12345.6789);

            var y = plinth.Bottom - (plinth.Height * ((i / 64f) + (f * 0.012f)));

            using var path = new SKPath();

            for (var x = plinth.Left; x <= plinth.Right; x += step)
            {
                var wobble = MathF.Sin((x * 0.004f) + (f * 6.28f)) * plinth.Height * 0.006f;

                if (x <= plinth.Left) path.MoveTo(x, y + wobble);
                else path.LineTo(x, y + wobble);
            }

            _stroke.StrokeWidth = 1f + (g * 2f);
            _stroke.Color = palette.Console.WithBrightness(0.14f).ToSkia(0.06f + (f * 0.08f));

            canvas.DrawPath(path, _stroke);
        }
    }

    private void DrawPlatter(SKCanvas canvas, float cx, float cy, float radius, ArtPalette palette)
    {
        var outer = radius * 1.16f;
        var disc = SKRect.Create(cx - outer, cy - outer, outer * 2f, outer * 2f);

        // A sprite with a fully rounded corner is a circle, so the platter's
        // shadow costs the same as a cover's.
        _sprite.Color = SKColors.White.WithAlpha(191);

        canvas.DrawBitmap(
            _shadows.Get(0.5f, blurFraction: radius * 0.16f / disc.Width),
            ShadowSprite.Placement(disc, dropFraction: radius * 0.04f / disc.Width),
            _sprite);

        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(cx - (outer * 0.5f), cy - (outer * 0.87f)),
                   new SKPoint(cx + (outer * 0.5f), cy + (outer * 0.87f)),
                   new[] { palette.Metal.ToSkia(), palette.Metal.WithBrightness(0.38f).ToSkia() },
                   SKShaderTileMode.Clamp))
        {
            _fill.Shader = shader;
            _fill.Color = SKColors.White;
            canvas.DrawCircle(cx, cy, outer, _fill);
            _fill.Shader = null;
        }

        _fill.Color = palette.Deep.WithBrightness(0.10f).ToSkia();
        canvas.DrawCircle(cx, cy, radius * 1.07f, _fill);
    }

    /// <summary>
    /// The record, which during a change is drawn twice.
    /// </summary>
    /// <remarks>
    /// The grooves are rotationally symmetric, so the two sweeps of sheen and
    /// the label are the only things carrying the spin. Leave them out and the
    /// record looks like a photograph of one.
    /// </remarks>
    private void DrawRecord(
        SKCanvas canvas, float cx, float cy, float radius, int index, ArtPalette palette, float alpha)
    {
        _fill.Shader = null;
        _fill.Color = new SKColor(11, 11, 11, Byte(alpha));
        canvas.DrawCircle(cx, cy, radius, _fill);

        var i = 0;

        for (var r = Turntable.GrooveStart(radius);
             r > Turntable.GrooveEnd(radius);
             r -= Turntable.GrooveStep(radius), i++)
        {
            var bright = Turntable.IsBrightGroove(i);

            _stroke.StrokeWidth = bright ? 0.9f : 1.6f;
            _stroke.Color = (bright ? new SKColor(56, 56, 56) : new SKColor(26, 26, 26))
                .WithAlpha(Byte(alpha * 0.5f));

            canvas.DrawCircle(cx, cy, r, _stroke);
        }

        canvas.Save();
        canvas.RotateRadians((float)_angle, cx, cy);

        var sweep = radius * 0.72f;
        var oval = SKRect.Create(cx - sweep, cy - sweep, sweep * 2f, sweep * 2f);

        _stroke.StrokeWidth = radius * 0.52f;
        _stroke.Color = palette.Accent.ToSkia(alpha * 0.05f);

        canvas.DrawArc(oval, -22f, 44f, false, _stroke);
        canvas.DrawArc(oval, 158f, 44f, false, _stroke);

        var labelRadius = radius * 0.345f;
        var label = SKRect.Create(
            cx - labelRadius, cy - labelRadius, labelRadius * 2f, labelRadius * 2f);

        canvas.Save();

        using (var clip = new SKPath())
        {
            clip.AddCircle(cx, cy, labelRadius);
            canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);

            var cover = _data.ImageFor(index);

            if (cover is null)
            {
                _fill.Shader = null;
                _fill.Color = palette.Console.ToSkia(alpha);
                canvas.DrawCircle(cx, cy, labelRadius, _fill);
            }
            else
            {
                Drawing.DrawImage(canvas, cover, label, alpha, radius: 0f, shadow: false);
            }
        }

        canvas.Restore();

        _stroke.StrokeWidth = radius * 0.016f;
        _stroke.Color = palette.Accent.ToSkia(alpha * 0.85f);
        canvas.DrawCircle(cx, cy, labelRadius, _stroke);

        _fill.Shader = null;
        _fill.Color = palette.Metal.ToSkia(alpha);
        canvas.DrawCircle(cx, cy, radius * 0.030f, _fill);

        _fill.Color = new SKColor(5, 5, 5, Byte(alpha));
        canvas.DrawCircle(cx, cy, radius * 0.020f, _fill);

        canvas.Restore();

        _stroke.StrokeWidth = 1.5f;
        _stroke.Color = new SKColor(89, 89, 89, Byte(alpha * 0.5f));
        canvas.DrawCircle(cx, cy, radius, _stroke);
    }

    /// <summary>
    /// The tonearm, placed by where the song has got to.
    /// </summary>
    private void DrawArm(
        SKCanvas canvas, float cx, float cy, float radius,
        ArtPalette palette, float lift, double phase)
    {
        var following = _data.Settings.VinylFollowNowPlaying
                        && _data.LiveAlbumIndex is not null
                        && _data.NowPlaying.DurationMs > 0;

        var progress = Turntable.PlayProgress(
            following, _data.NowPlaying.ProgressFraction,
            phase, _startedAt, _data.Settings.VinylSecondsPerRecord);

        var pivotX = cx + (radius * 1.45f);
        var pivotY = cy + (radius * 0.75f);
        var armLength = radius * 1.35f;

        var angle = Turntable.ArmAngle(
            pivotX, pivotY, cx, cy, armLength, Turntable.TipRadius(radius, progress, lift));

        var tipX = pivotX + (float)(Math.Cos(angle) * armLength);
        var tipY = pivotY + (float)(Math.Sin(angle) * armLength);

        var drop = lift * radius * 0.05f;

        _stroke.StrokeWidth = radius * 0.045f;
        _stroke.Color = SKColors.Black.WithAlpha(Byte(0.35f - (lift * 0.15f)));
        canvas.DrawLine(pivotX, pivotY + drop + 3f, tipX, tipY + drop + 3f, _stroke);

        _stroke.StrokeWidth = radius * 0.115f;
        _stroke.Color = palette.Metal.WithBrightness(0.45f).ToSkia();
        canvas.DrawLine(
            pivotX, pivotY,
            pivotX - (float)(Math.Cos(angle) * radius * 0.34f),
            pivotY - (float)(Math.Sin(angle) * radius * 0.34f),
            _stroke);

        _stroke.StrokeWidth = radius * 0.040f;
        canvas.DrawLine(pivotX, pivotY, tipX, tipY, _stroke);

        _stroke.StrokeWidth = radius * 0.018f;
        _stroke.Color = palette.Metal.ToSkia();
        canvas.DrawLine(pivotX, pivotY, tipX, tipY, _stroke);

        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(pivotX - (radius * 0.1f), pivotY - (radius * 0.17f)),
                   new SKPoint(pivotX + (radius * 0.1f), pivotY + (radius * 0.17f)),
                   new[] { palette.Metal.ToSkia(), palette.Metal.WithBrightness(0.35f).ToSkia() },
                   SKShaderTileMode.Clamp))
        {
            _fill.Shader = shader;
            _fill.Color = SKColors.White;
            canvas.DrawCircle(pivotX, pivotY, radius * 0.10f, _fill);
            _fill.Shader = null;
        }

        _stroke.StrokeWidth = 1.5f;
        _stroke.Color = palette.Deep.ToSkia(0.7f);
        canvas.DrawCircle(pivotX, pivotY, radius * 0.10f, _stroke);

        canvas.Save();
        canvas.RotateRadians((float)angle, tipX, tipY);

        var head = SKRect.Create(
            tipX - (radius * 0.10f), tipY - (radius * 0.055f), radius * 0.17f, radius * 0.11f);

        _fill.Shader = null;
        _fill.Color = palette.Deep.WithBrightness(0.08f).ToSkia();
        canvas.DrawRoundRect(head, radius * 0.02f, radius * 0.02f, _fill);

        _stroke.StrokeWidth = 1.4f;
        _stroke.Color = palette.Accent.ToSkia();
        canvas.DrawRoundRect(head, radius * 0.02f, radius * 0.02f, _stroke);

        canvas.Restore();
    }

    /// <summary>
    /// Every record played this session, lying where it was put down.
    /// </summary>
    private void DrawTable(
        SKCanvas canvas, float width, float height, double phase, ArtPalette palette)
    {
        foreach (var sleeve in _table.Table)
        {
            if (!_data.AlbumIndexById.TryGetValue(sleeve.AlbumId, out var index)) continue;

            var side = sleeve.SizeFraction * height;

            var age = phase - sleeve.AddedAt;
            var young = age < 0.7;

            var k = young ? Ease.Out(Ease.Clamp((float)(age / 0.7))) : 1f;
            var scale = young ? 1.3f - (0.3f * k) : 1f;
            var lift = young ? (1f - k) * height * 0.03f : 0f;
            var alpha = young ? Ease.Out(Ease.Clamp((float)(age / 0.45))) : 1f;

            canvas.Save();
            canvas.Translate(sleeve.Nx * width, (sleeve.Ny * height) + lift);
            canvas.RotateRadians(sleeve.Angle);

            var drawn = side * scale;
            var half = drawn / 2f;
            var square = SKRect.Create(-half, -half, drawn, drawn);

            if (sleeve.DiscPeek > 0f)
            {
                DrawPeekingDisc(canvas, drawn, half, sleeve, index, palette, alpha);
            }

            _sprite.Color = SKColors.White.WithAlpha(Byte(0.6f * alpha));

            canvas.DrawBitmap(
                _shadows.Get(0f, blurFraction: 0.14f),
                ShadowSprite.Placement(square, dropFraction: 0.04f),
                _sprite);

            _fill.Color = new SKColor(15, 15, 15, Byte(alpha));
            canvas.DrawRect(square, _fill);

            Drawing.DrawImage(
                canvas, _data.ImageFor(index), square, alpha, radius: 0f, shadow: false);

            // Pushes the pile back so the deck stays the subject.
            _fill.Color = palette.Deep.ToSkia(0.46f * alpha);
            canvas.DrawRect(square, _fill);

            _stroke.StrokeWidth = 1f;
            _stroke.Color = SKColors.White.WithAlpha(Byte(0.05f * alpha));
            canvas.DrawRect(square, _stroke);

            canvas.Restore();
        }
    }

    /// <summary>
    /// A disc sliding out of its sleeve.
    /// </summary>
    /// <remarks>
    /// Drawn first, so the sleeve prints over it and it reads as being inside
    /// the sleeve rather than lying on top of it.
    /// </remarks>
    private void DrawPeekingDisc(
        SKCanvas canvas, float side, float half, TableSleeve sleeve,
        int index, ArtPalette palette, float alpha)
    {
        var dx = MathF.Cos(sleeve.DiscDirection) * side * sleeve.DiscPeek;
        var dy = MathF.Sin(sleeve.DiscDirection) * side * sleeve.DiscPeek;
        var dr = half * 0.95f;

        var disc = SKRect.Create(dx - dr, dy - dr, dr * 2f, dr * 2f);

        _sprite.Color = SKColors.White.WithAlpha(Byte(0.55f * alpha));

        canvas.DrawBitmap(
            _shadows.Get(0.5f, blurFraction: side * 0.10f / disc.Width),
            ShadowSprite.Placement(disc, dropFraction: side * 0.02f / disc.Width),
            _sprite);

        _fill.Color = new SKColor(13, 13, 13, Byte(alpha));
        canvas.DrawCircle(dx, dy, dr, _fill);

        _stroke.StrokeWidth = 1f;
        _stroke.Color = new SKColor(77, 77, 77, Byte(0.16f * alpha));

        for (var rr = dr * 0.90f; rr > dr * 0.42f; rr -= dr * 0.07f)
        {
            canvas.DrawCircle(dx, dy, rr, _stroke);
        }

        var labelRadius = dr * 0.35f;

        canvas.Save();

        using (var clip = new SKPath())
        {
            clip.AddCircle(dx, dy, labelRadius);
            canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);

            Drawing.DrawImage(
                canvas, _data.ImageFor(index),
                SKRect.Create(dx - labelRadius, dy - labelRadius, labelRadius * 2f, labelRadius * 2f),
                0.9f * alpha, radius: 0f, shadow: false);
        }

        canvas.Restore();

        _stroke.StrokeWidth = 1.2f;
        _stroke.Color = palette.Accent.ToSkia(0.5f * alpha);
        canvas.DrawCircle(dx, dy, labelRadius, _stroke);
    }

    /// <summary>
    /// The propped sleeve and the credits beside the deck.
    /// </summary>
    /// <remarks>
    /// The block is measured before any of it is drawn and then centred. Laying
    /// it out downward from a fixed line is what lets a long title run off the
    /// bottom of the screen.
    /// </remarks>
    private void DrawColumn(
        SKCanvas canvas, float width, float height, int index, ArtPalette palette)
    {
        var x = width * 0.585f;
        var columnWidth = width * 0.355f;

        var sleeveSide = Math.Min(height * 0.62f, width * 0.42f);

        var propped = SKRect.Create(
            x + (columnWidth * 0.5f) - (sleeveSide * 0.52f),
            (height * 0.5f) - (sleeveSide / 2f),
            sleeveSide, sleeveSide);

        canvas.Save();

        // A poster propped against the wall, not a framed print. The tilt is the
        // whole point of it.
        canvas.RotateRadians(0.075f, propped.MidX, propped.MidY);
        Drawing.DrawCover(canvas, _data.ImageFor(index), propped, 1f, radius: 2f, shadowAlpha: 0.7f);
        canvas.Restore();

        // One gradient rather than the specification's two stacked fills, begun
        // a tenth of the screen earlier so the wash has somewhere to fade in
        // from. See Turntable.ScrimAlpha: as written it steps from nothing to
        // thirty five per cent in no distance and leaves a hard vertical line
        // down the whole height of the screen.
        var scrim = SKRect.Create(
            width * Turntable.ScrimStart, 0, width * (1f - Turntable.ScrimStart), height);

        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(scrim.Left, 0),
                   new SKPoint(scrim.Right, 0),
                   new[]
                   {
                       palette.Deep.ToSkia(0f),
                       palette.Deep.ToSkia(Turntable.ScrimAtSeam),
                       palette.Deep.ToSkia(Turntable.ScrimAtEdge),
                   },
                   new[] { 0f, Turntable.ScrimSeamStop(), 1f },
                   SKShaderTileMode.Clamp))
        {
            _fill.Shader = shader;
            _fill.Color = SKColors.White;
            canvas.DrawRect(scrim, _fill);
            _fill.Shader = null;
        }

        var live = _data.LiveAlbumIndex == index;
        var (title, artist, sub) = FeaturedText.For(_data, index);

        var kickerSize = Math.Max(10f, height * 0.017f);
        var titleSize = TextLayout.Fitted(
            title, columnWidth, height * 0.22f, Math.Max(20f, height * 0.050f), bold: true);
        var artistSize = TextLayout.Fitted(
            artist, columnWidth, height * 0.10f, Math.Max(14f, height * 0.029f), bold: false);
        var subSize = Math.Max(11f, height * 0.020f);

        var kicker = live ? "NOW PLAYING" : "ON THE TURNTABLE";
        var gap = height * 0.022f;

        var total = TextLayout.Measure(kicker, kickerSize, true, columnWidth, tracking: 3f)
                    + (gap * 1.4f)
                    + TextLayout.Measure(title, titleSize, true, columnWidth)
                    + (gap * 0.8f)
                    + TextLayout.Measure(artist, artistSize, false, columnWidth);

        if (!string.IsNullOrEmpty(sub))
        {
            total += (gap * 0.5f) + TextLayout.Measure(sub, subSize, false, columnWidth);
        }

        var top = (height * 0.5f) - (total / 2f);

        using var shadow = SKImageFilter.CreateDropShadow(
            0f, 2f, 12f, 12f, SKColors.Black.WithAlpha(204));

        using (var paint = TextLayout.Paint(kickerSize, true, palette.Accent.ToSkia()))
        {
            paint.ImageFilter = shadow;
            top += TextLayout.Draw(canvas, kicker, x, top, columnWidth, paint, tracking: 3f);
            top += gap * 1.4f;
        }

        using (var paint = TextLayout.Paint(titleSize, true, palette.Text.ToSkia()))
        {
            paint.ImageFilter = shadow;
            top += TextLayout.Draw(canvas, title, x, top, columnWidth, paint);
            top += gap * 0.8f;
        }

        using (var paint = TextLayout.Paint(artistSize, false, palette.Accent.ToSkia()))
        {
            paint.ImageFilter = shadow;
            top += TextLayout.Draw(canvas, artist, x, top, columnWidth, paint);
        }

        if (string.IsNullOrEmpty(sub)) return;

        top += gap * 0.5f;

        using (var paint = TextLayout.Paint(subSize, false, palette.TextMuted.ToSkia()))
        {
            paint.ImageFilter = shadow;
            TextLayout.Draw(canvas, sub, x, top, columnWidth, paint);
        }
    }

    private static byte Byte(float alpha) => (byte)Math.Clamp(alpha * 255f, 0f, 255f);

    public void Dispose()
    {
        _fill.Dispose();
        _stroke.Dispose();
        _shadows.Dispose();
        _sprite.Dispose();
        _blit.Dispose();
        _plinth.Dispose();
    }
}

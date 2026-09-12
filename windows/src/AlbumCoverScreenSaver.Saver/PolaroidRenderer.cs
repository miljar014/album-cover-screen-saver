using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// Polaroid Corkboard. Instant photographs pinned to a cork board in a wooden
/// frame, one in the middle for whatever is playing.
/// </summary>
/// <remarks>
/// <para>
/// This is the style the persistent board was built for. Every record you listen
/// to gets pinned up when the next one starts, and it stays exactly where it was
/// put: not re-scattered when the archive grows, not moved when the resolution
/// changes, not wiped when a new song is recorded. Come back an hour later and
/// the wall is a record of the hour.
/// </para>
/// <para>
/// <b>The one rule that has to be right.</b> The test for "has the middle
/// changed" reads the playing album's id, never the featured index. The featured
/// index is deliberately frozen for 1.35 seconds while a change animates, so
/// skipping two tracks quickly inside that window would make the middle one
/// vanish without ever reaching the wall. Reading the id catches every record
/// regardless of what the animation is doing.
/// </para>
/// </remarks>
internal sealed class PolaroidRenderer : IStyleRenderer, IDisposable
{
    /// <summary>Seconds a new photograph takes to land.</summary>
    private const float ArrivalSeconds = 0.6f;

    private readonly SaverData _data;
    private readonly AlbumPicker _picker;
    private readonly PaletteStore _palettes;
    private readonly Random _random;
    private readonly bool _isPreview;

    private readonly Featured _featured;
    private readonly ScatterLayout _board;
    private readonly CorkTexture _cork = new();
    private readonly SKPaint _fill = new() { IsAntialias = true };
    private readonly SKPaint _shadowPaint = new() { IsAntialias = true, FilterQuality = SKFilterQuality.Low };
    private readonly ShadowSprite _shadows = new();

    private string? _lastCentreId;
    private float _width;
    private float _height;

    public PolaroidRenderer(
        SaverData data, AlbumPicker picker, PaletteStore palettes, Random random, bool isPreview)
    {
        _data = data;
        _picker = picker;
        _palettes = palettes;
        _random = random;
        _isPreview = isPreview;
        _featured = new Featured(picker, data.Settings.RecencyBias);
        _board = new ScatterLayout(random);
    }

    private int Cap => _isPreview ? 4 : Math.Clamp(_data.Settings.PolaroidCount, 0, 24);

    public void BuildLayout(float width, float height)
    {
        _width = width;
        _height = height;

        _board.Claim(CollageMode.Polaroid);

        var albums = _data.Albums;
        if (albums.Count == 0) return;

        _featured.Reset(0, _picker.Pick(albums.Count, _data.Settings.RecencyBias));

        if (Cap == 0)
        {
            _board.TrimTo(0);
            return;
        }

        // Populated once and then only added to. An archive refresh must not
        // rebuild it, which is why this returns rather than reseeding.
        if (_board.Table.Count > 0) return;

        var centre = _data.LiveAlbumIndex ?? _featured.Index;
        var centreId = centre >= 0 && centre < albums.Count ? albums[centre].Id : "";

        var seeds = albums
            .Take(Cap + 1)
            .Where(album => !string.Equals(album.Id, centreId, StringComparison.Ordinal))
            .Take(Cap)
            .Select(album => album.Id)
            .ToList();

        _board.Seed(seeds, Cap, phase: 0, FindSpot);

        Log.Write($"polaroid layout: {_board.Table.Count} photos on a board of {width:0}x{height:0} points");
    }

    /// <summary>
    /// Somewhere free on the board, keeping clear of the middle photograph.
    /// </summary>
    private TableSpot? FindSpot() => _board.FindSpot(
        _width, _height,
        minSizeFraction: 0.17f, maxSizeFraction: 0.26f, scale: 1f,
        keepOutCentreX: _width * 0.5f,
        keepOutCentreY: _height * 0.5f,
        keepOutRadius: Math.Min(_height * 0.36f, _width * 0.26f) * 0.82f);

    public void Advance(double phase, float width, float height)
    {
        _width = width;
        _height = height;

        var albums = _data.Albums;
        if (albums.Count == 0) return;

        _featured.Advance(phase, _data.LiveAlbumIndex, _data.Settings.FeatureSeconds, albums.Count);

        if (_board.Table.Count == 0 && Cap > 0) BuildLayout(width, height);

        // Read off the playback signal, not the featured index. See the note at
        // the top: the index is frozen mid-change and a quickly skipped track
        // would never reach the wall.
        string? currentId = null;

        if (_data.NowPlaying.IsLive && !string.IsNullOrEmpty(_data.NowPlaying.AlbumId))
        {
            currentId = _data.NowPlaying.AlbumId;
        }
        else if (_featured.Index >= 0 && _featured.Index < albums.Count)
        {
            currentId = albums[_featured.Index].Id;
        }

        if (currentId is null) return;

        if (_lastCentreId is not null && !string.Equals(_lastCentreId, currentId, StringComparison.Ordinal))
        {
            _board.Pin(_lastCentreId, FindSpot(), phase);
            _board.TrimTo(Cap);
        }

        _lastCentreId = currentId;
    }

    public void Draw(SKCanvas canvas, float width, float height, double phase)
    {
        var albums = _data.Albums;
        if (albums.Count == 0) return;

        var centre = Math.Clamp(_featured.Index, 0, albums.Count - 1);
        var palette = _palettes.For(albums[centre].Id, _data.ImageFor(centre));

        DrawFrame(canvas, width, height);
        DrawBoard(canvas, width, height);

        // Oldest first, so newer photographs overlap the ones already up.
        for (var i = 0; i < _board.Table.Count; i++)
        {
            var pinned = _board.Table[i];
            var index = IndexOf(pinned.AlbumId);
            if (index < 0) continue;

            var settle = (float)(phase - pinned.AddedAt);
            var landed = settle < ArrivalSeconds ? Ease.Back(settle / ArrivalSeconds) : 1f;

            // It drops the last fraction of a screen into place and shrinks from
            // 120% as it goes, which is what makes it read as being put there
            // rather than appearing.
            var centreX = pinned.Nx * width;
            var centreY = (pinned.Ny * height) - ((1f - landed) * height * 0.025f);
            var side = pinned.SizeFraction * height * (1.2f - (0.2f * landed));

            DrawPhoto(
                canvas, index, centreX, centreY, side, pinned.Angle, landed,
                i, big: false, phase, palette);
        }

        var centreSide = Math.Min(height * 0.36f, width * 0.26f);
        DrawPhoto(
            canvas, centre, width * 0.5f, height * 0.5f, centreSide, -0.03f, 1f,
            index: 999, big: true, phase, palette);
    }

    private int IndexOf(string albumId) =>
        _data.AlbumIndexById.TryGetValue(albumId, out var index) ? index : -1;

    /// <summary>The wooden surround, with its grain.</summary>
    private void DrawFrame(SKCanvas canvas, float width, float height)
    {
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(width * 0.342f, height),
            new[] { new SKColor(107, 69, 36), new SKColor(66, 41, 20) },
            SKShaderTileMode.Clamp);

        _fill.Shader = shader;
        _fill.Color = SKColors.White;
        canvas.DrawRect(SKRect.Create(0, 0, width, height), _fill);
        _fill.Shader = null;

        for (var i = 0; i < 40; i++)
        {
            _fill.Color = SKColors.Black.WithAlpha((byte)((0.05f + (i % 3 * 0.02f)) * 255f));
            canvas.DrawRect(SKRect.Create(0, i / 40f * height, width, 1.5f), _fill);
        }
    }

    /// <summary>
    /// The cork, and the shadow the frame casts onto it.
    /// </summary>
    /// <remarks>
    /// The four shadow strips are what make the board read as recessed into the
    /// frame rather than as a rectangle printed on it. They are not decoration.
    /// </remarks>
    private void DrawBoard(SKCanvas canvas, float width, float height)
    {
        var inset = Math.Min(width, height) * 0.028f;
        var board = SKRect.Create(inset, inset, width - (inset * 2f), height - (inset * 2f));

        canvas.Save();
        canvas.ClipRect(board);

        var cork = _cork.Get(board.Width, board.Height, _random);
        if (cork is not null)
        {
            _fill.Shader = null;
            _fill.Color = SKColors.White;
            canvas.DrawBitmap(cork, board, _fill);
        }

        var depth = inset * 0.9f;
        DrawEdgeShadow(canvas, SKRect.Create(board.Left, board.Top, board.Width, depth), 0, 1);
        DrawEdgeShadow(canvas, SKRect.Create(board.Left, board.Bottom - depth, board.Width, depth), 0, -1);
        DrawEdgeShadow(canvas, SKRect.Create(board.Left, board.Top, depth, board.Height), 1, 0);
        DrawEdgeShadow(canvas, SKRect.Create(board.Right - depth, board.Top, depth, board.Height), -1, 0);

        canvas.Restore();

        _fill.Shader = null;
        _fill.Color = SKColors.Black.WithAlpha(89);
        _fill.Style = SKPaintStyle.Stroke;
        _fill.StrokeWidth = 2f;
        canvas.DrawRect(board, _fill);
        _fill.Style = SKPaintStyle.Fill;
    }

    /// <summary>One strip of the recess shadow, dark at the frame and clear inward.</summary>
    private void DrawEdgeShadow(SKCanvas canvas, SKRect strip, int inwardX, int inwardY)
    {
        var from = new SKPoint(
            inwardX >= 0 ? strip.Left : strip.Right,
            inwardY >= 0 ? strip.Top : strip.Bottom);

        var to = new SKPoint(
            inwardX > 0 ? strip.Right : inwardX < 0 ? strip.Left : from.X,
            inwardY > 0 ? strip.Bottom : inwardY < 0 ? strip.Top : from.Y);

        using var shader = SKShader.CreateLinearGradient(
            from, to,
            new[] { SKColors.Black.WithAlpha(77), SKColors.Black.WithAlpha(0) },
            SKShaderTileMode.Clamp);

        _fill.Shader = shader;
        _fill.Color = SKColors.White;
        canvas.DrawRect(strip, _fill);
        _fill.Shader = null;
    }

    /// <summary>
    /// One instant photograph: card, image, caption and pin.
    /// </summary>
    /// <remarks>
    /// It rocks rather than slides, because it turns about the pin rather than
    /// about its own centre. That is the whole difference between a photograph
    /// hanging on a wall and a sticker sliding across one.
    /// </remarks>
    private void DrawPhoto(
        SKCanvas canvas, int album, float centreX, float centreY, float side,
        float angle, float alpha, int index, bool big, double phase, ArtPalette palette)
    {
        if (alpha <= 0.01f) return;

        var w = side;
        var border = w * 0.055f;
        var h = w + (border * 4.4f);

        // Every photograph sways on its own phase, so the wall breathes instead
        // of rocking in time.
        var sway = MathF.Sin(((float)phase * 0.6f) + (index * 1.7f)) * 0.012f;

        canvas.Save();
        canvas.Translate(centreX, centreY - (h * 0.42f));
        canvas.RotateRadians(angle + sway);
        canvas.Translate(0, h * 0.42f);

        var card = SKRect.Create(-(w / 2f) - border, -h / 2f, w + (border * 2f), h);

        var sprite = _shadows.Get(0.01f);
        _shadowPaint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(0.55f * alpha * 255f, 0f, 255f));
        canvas.DrawBitmap(sprite, ShadowSprite.Placement(card), _shadowPaint);

        _fill.Shader = null;
        _fill.Color = new SKColor(247, 245, 237, (byte)Math.Clamp(alpha * 255f, 0f, 255f));
        canvas.DrawRect(card, _fill);

        var photo = SKRect.Create(-w / 2f, card.Top + border, w, w);
        Drawing.DrawImage(canvas, _data.ImageFor(album), photo, alpha, radius: 0f, shadow: false);

        // Only the pinned ones are knocked back. Leaving the middle one bright
        // is what says it is the record playing.
        if (!big)
        {
            _fill.Color = palette.Deep.ToSkia(0.20f * alpha);
            canvas.DrawRect(photo, _fill);
        }

        DrawCaption(canvas, album, card, w, h, big, alpha);
        DrawPin(canvas, card, w, alpha, palette);

        canvas.Restore();
    }

    private void DrawCaption(
        SKCanvas canvas, int album, SKRect card, float w, float h, bool big, float alpha)
    {
        var albums = _data.Albums;
        if (album < 0 || album >= albums.Count) return;

        var caption = big ? FeaturedText.For(_data, album).Title : albums[album].Name;
        if (string.IsNullOrEmpty(caption)) return;

        using var paint = new SKPaint
        {
            IsAntialias = true,
            TextSize = Math.Max(9f, w * 0.10f),
            Color = new SKColor(46, 51, 82, (byte)Math.Clamp(alpha * 255f, 0f, 255f)),
            TextAlign = SKTextAlign.Center,
            // A hand on a photograph, not a typeface. The Mac uses Bradley Hand
            // and this is the nearest thing Windows ships.
            Typeface = SKTypeface.FromFamilyName(
                           "Segoe Script", SKFontStyleWeight.Normal,
                           SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                       ?? TextLayout.Face(bold: false),
        };

        // Clipped rather than wrapped, and truncated rather than shrunk: a long
        // album name written on a photograph runs out of room, which is what a
        // person's hand does too.
        var box = SKRect.Create(card.Left + 4f, card.Top + (h * 0.845f), card.Width - 8f, h * 0.11f);

        canvas.Save();
        canvas.ClipRect(box);
        canvas.DrawText(caption, box.MidX, box.Top + paint.TextSize, paint);
        canvas.Restore();
    }

    /// <summary>
    /// The drawing pin. The only part of a photograph that takes the album's
    /// colour.
    /// </summary>
    private void DrawPin(SKCanvas canvas, SKRect card, float w, float alpha, ArtPalette palette)
    {
        var radius = w * 0.045f;
        var x = 0f;
        var y = card.Top + (radius * 1.6f);

        var opacity = (byte)Math.Clamp(alpha * 255f, 0f, 255f);

        _fill.Shader = null;
        _fill.Color = SKColors.Black.WithAlpha((byte)Math.Clamp(0.30f * alpha * 255f, 0f, 255f));
        canvas.DrawCircle(x, y + (radius * 0.4f), radius * 0.8f, _fill);

        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(x - radius, y - radius),
                   new SKPoint(x + radius, y + radius),
                   new[]
                   {
                       palette.Accent.ToSkia().WithAlpha(opacity),
                       palette.Accent.WithBrightness(0.35f).ToSkia().WithAlpha(opacity),
                   },
                   SKShaderTileMode.Clamp))
        {
            _fill.Shader = shader;
            _fill.Color = SKColors.White;
            canvas.DrawCircle(x, y, radius, _fill);
            _fill.Shader = null;
        }

        _fill.Color = SKColors.White.WithAlpha((byte)Math.Clamp(0.55f * alpha * 255f, 0f, 255f));
        canvas.DrawCircle(x - (radius * 0.1f), y - (radius * 0.3f), radius * 0.25f, _fill);
    }

    public void Dispose()
    {
        _fill.Dispose();
        _shadowPaint.Dispose();
        _shadows.Dispose();
        _cork.Dispose();
    }
}

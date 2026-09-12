using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// Gallery Wall. Two rows of framed covers hung on a wall, with a spotlight
/// roaming across them and a placard under each one.
/// </summary>
/// <remarks>
/// <para>
/// The trick that makes it work is the spotlight. Sixty per cent of its
/// position is pinned to the column the featured frame is in and only forty per
/// cent roams, so the light always lingers near whatever is playing. It reads
/// as a gallery that happens to be lit, rather than as a highlight being pointed
/// at something.
/// </para>
/// <para>
/// Only the artwork is dimmed by the light, never the frame or the mat. That is
/// the right way round: paint is matte and takes the light, and the moulding is
/// the lit object. Dimming the frames as well makes the whole wall look like it
/// is behind a gauze.
/// </para>
/// <para>
/// The hero frame cuts rather than crossfades. It is the one single-album style
/// that does, and it is deliberate: a picture on a wall is replaced, not
/// dissolved.
/// </para>
/// </remarks>
internal sealed class GalleryRenderer : IStyleRenderer, IDisposable
{
    /// <summary>Radians per frame. A full sweep of the light takes about seventy seconds.</summary>
    private const float SpotlightRate = (float)(TileField.FrameInterval * 0.09);

    private const int Rows = 2;

    private readonly SaverData _data;
    private readonly AlbumPicker _picker;
    private readonly PaletteStore _palettes;
    private readonly bool _isPreview;

    private readonly Featured _featured;
    private readonly List<int> _items = [];
    private readonly SKPaint _fill = new() { IsAntialias = true };
    private readonly ShadowSprite _shadows = new();
    private readonly SKPaint _shadowPaint = new() { IsAntialias = true, FilterQuality = SKFilterQuality.Low };

    private float _spotPhase;

    public GalleryRenderer(
        SaverData data, AlbumPicker picker, PaletteStore palettes, bool isPreview)
    {
        _data = data;
        _picker = picker;
        _palettes = palettes;
        _isPreview = isPreview;
        _featured = new Featured(picker, data.Settings.RecencyBias);
    }

    private int Columns => Math.Clamp(_data.Settings.GalleryColumns, 2, 7);

    private int Slots => Columns * Rows;

    /// <summary>The first slot of the lower row, which is where the eye lands.</summary>
    private int HeroSlot => Slots / 2;

    public void BuildLayout(float width, float height)
    {
        _items.Clear();
        if (_data.Albums.Count == 0) return;

        _featured.Reset(0, _picker.Pick(_data.Albums.Count, _data.Settings.RecencyBias));
        RefillSlots();

        Log.Write($"gallery layout: {Columns} columns, {Slots} frames across {width:0}x{height:0} points");
    }

    /// <summary>
    /// Fills every frame with a distinct album.
    /// </summary>
    /// <remarks>
    /// Rebuilt only when the number of frames changes, so the covers on the wall
    /// stay put while the hero rotates. A wall that reshuffled itself every time
    /// the featured album changed would be a different style entirely.
    /// </remarks>
    private void RefillSlots()
    {
        _items.Clear();

        var albums = _data.Albums.Count;
        if (albums == 0) return;

        var used = new HashSet<int>();
        for (var slot = 0; slot < Slots; slot++)
        {
            var index = _picker.PickAvoiding(albums, _data.Settings.RecencyBias, used);
            used.Add(index);
            _items.Add(index);
        }
    }

    public void Advance(double phase, float width, float height)
    {
        if (_data.Albums.Count == 0) return;

        if (_items.Count != Slots) RefillSlots();

        _featured.Advance(
            phase, _data.LiveAlbumIndex, _data.Settings.FeatureSeconds, _data.Albums.Count);

        _spotPhase += SpotlightRate;

        // The hero slot always shows the featured album, so it is written every
        // frame rather than at the moment of a change.
        if (_items.Count > HeroSlot) _items[HeroSlot] = _featured.Index;
    }

    public void Draw(SKCanvas canvas, float width, float height, double phase)
    {
        if (_items.Count == 0) return;

        var albums = _data.Albums;
        var featured = Math.Clamp(_featured.Index, 0, albums.Count - 1);
        var palette = _palettes.For(albums[featured].Id, _data.ImageFor(featured));

        var columns = Columns;
        var marginX = width * 0.07f;
        var cellWidth = (width - (2f * marginX)) / columns;
        var cellHeight = height * 0.36f;

        // The specification is in AppKit's y-up space, where topY is measured up
        // from the bottom and row 0 is the upper row. Converted here, once.
        var railY = height - (height * 0.60f) - (cellHeight * 0.06f);
        var firstRowCentre = height - (height * 0.60f) + (cellHeight * 0.42f);

        DrawWall(canvas, width, height, palette);

        var reach = Math.Min(width, height) * 0.55f;
        var roam = (MathF.Sin(_spotPhase) + 1f) / 2f;
        var heroColumn = HeroSlot % columns;
        var heroX = marginX + ((heroColumn + 0.5f) * cellWidth);

        // Sixty per cent pinned to the hero's column, forty roaming. The light
        // wanders, but it never wanders far from what is playing.
        var spotX = (heroX * 0.6f) + (roam * width * 0.4f);
        var spotY = height * 0.45f;

        DrawSpotlight(canvas, spotX, spotY, reach);

        _fill.Color = palette.Accent.ToSkia(0.30f);
        canvas.DrawRect(SKRect.Create(0, railY, width, 2f), _fill);

        var art = Math.Min(cellWidth * 0.62f, cellHeight * 0.52f);
        var mat = art * 1.26f;
        var sprite = _shadows.Get(0.02f);

        for (var slot = 0; slot < _items.Count; slot++)
        {
            var row = slot / columns;
            var column = slot % columns;

            var centreX = marginX + ((column + 0.5f) * cellWidth);
            var centreY = firstRowCentre + (row * cellHeight);

            var distance = MathF.Sqrt(
                ((centreX - spotX) * (centreX - spotX)) + ((centreY - spotY) * (centreY - spotY))) / reach;

            // One number drives the frame's brightness and its placard's text.
            var lit = Math.Max(0.30f, 1f - (distance * 0.85f));

            DrawFrame(canvas, slot, centreX, centreY, mat, art, lit, palette, sprite);

            if (_data.Settings.ShowTrackLabel && !_isPreview)
            {
                DrawPlacard(
                    canvas, slot, centreX, centreY - (mat / 2f) - (cellHeight * 0.045f),
                    cellWidth * 0.86f, height, lit, palette);
            }
        }
    }

    private void DrawWall(SKCanvas canvas, float width, float height, ArtPalette palette)
    {
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, height),
            new[]
            {
                palette.Console.WithBrightness(0.26f).ToSkia(),
                palette.Deep.WithBrightness(0.07f).ToSkia(),
            },
            SKShaderTileMode.Clamp);

        _fill.Shader = shader;
        _fill.Color = SKColors.White;
        canvas.DrawRect(SKRect.Create(0, 0, width, height), _fill);
        _fill.Shader = null;
    }

    private void DrawSpotlight(SKCanvas canvas, float x, float y, float reach)
    {
        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(x, y), reach,
            new[] { SKColors.White.WithAlpha(22), SKColors.White.WithAlpha(0) },
            SKShaderTileMode.Clamp);

        _fill.Shader = shader;
        _fill.Color = SKColors.White;
        canvas.DrawCircle(x, y, reach, _fill);
        _fill.Shader = null;
    }

    private void DrawFrame(
        SKCanvas canvas, int slot, float centreX, float centreY,
        float mat, float art, float lit, ArtPalette palette, SKBitmap sprite)
    {
        var isHero = slot == HeroSlot;

        var frameRect = SKRect.Create(centreX - (mat / 2f), centreY - (mat / 2f), mat, mat);
        var artRect = SKRect.Create(centreX - (art / 2f), centreY - (art / 2f), art, art);

        _shadowPaint.Color = SKColors.White.WithAlpha(153);
        canvas.DrawBitmap(sprite, ShadowSprite.Placement(frameRect), _shadowPaint);

        // The hero's moulding is a warm album-coloured wood; every other frame
        // is the desaturated metal, so the featured one reads as the good frame
        // on the wall rather than as a highlighted one.
        var moulding = isHero
            ? palette.Accent.WithBrightness(0.55f)
            : palette.Metal.WithBrightness(0.40f);

        _fill.Color = moulding.ToSkia();
        canvas.DrawRect(frameRect, _fill);

        // A near-white card with the faintest tint of the album in it.
        var board = frameRect;
        board.Inflate(-mat * 0.035f, -mat * 0.035f);
        _fill.Color = palette.Deep.WithBrightness(0.88f, 0.15f).ToSkia();
        canvas.DrawRect(board, _fill);

        Drawing.DrawImage(canvas, _data.ImageFor(_items[slot]), artRect, 1f, radius: 0f, shadow: false);

        // Only the artwork takes the light. See the note at the top.
        _fill.Color = SKColors.Black.WithAlpha((byte)Math.Clamp((1f - lit) * 255f, 0f, 255f));
        canvas.DrawRect(artRect, _fill);
    }

    /// <summary>
    /// The little card under a frame: what it is, and who made it.
    /// </summary>
    /// <remarks>
    /// Both lines fade with the light rather than going out entirely, because a
    /// placard in a dim corner of a gallery is still legible if you walk up to
    /// it. The floor of 0.35 is what keeps it from disappearing.
    /// </remarks>
    private void DrawPlacard(
        SKCanvas canvas, int slot, float centreX, float top, float plaqueWidth,
        float height, float lit, ArtPalette palette)
    {
        var albums = _data.Albums;
        var index = _items[slot];
        if (index < 0 || index >= albums.Count) return;

        var album = albums[index];
        var isHero = slot == HeroSlot;

        // The hero names the track when something is playing; everything else
        // names the record, the way a gallery labels a painting.
        var title = album.Name;
        var artist = album.Artist;

        if (isHero && _data.LiveAlbumIndex == index && !string.IsNullOrEmpty(_data.NowPlaying.Track))
        {
            title = _data.NowPlaying.Track;
            artist = _data.NowPlaying.Artist;
        }

        var fade = 0.35f + (0.65f * lit);
        var left = centreX - (plaqueWidth / 2f);

        using var titlePaint = TextLayout.Paint(
            Math.Max(9f, height * 0.0165f), bold: true, palette.Text.ToSkia(fade));
        titlePaint.TextAlign = SKTextAlign.Left;

        var used = TextLayout.Draw(canvas, title, left, top, plaqueWidth, titlePaint, centred: true);

        using var artistPaint = TextLayout.Paint(
            Math.Max(8f, height * 0.014f), bold: false, palette.Accent.ToSkia(fade));
        artistPaint.TextAlign = SKTextAlign.Left;

        TextLayout.Draw(
            canvas, artist, left, top + used + (height * 0.004f), plaqueWidth, artistPaint, centred: true);
    }

    public void Dispose()
    {
        _fill.Dispose();
        _shadowPaint.Dispose();
        _shadows.Dispose();
    }
}

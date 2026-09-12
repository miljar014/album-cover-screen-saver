using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// Ambient Field. The quietest style: one cover floating over a slow wash of
/// the album's own colours, with its reflection under it.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is invented. The ground and all four drifting masses are the
/// cover's colours: its most characteristic one, and its average colour held at
/// three different brightnesses. So the whole screen changes hue when the track
/// does, and it changes at the <em>start</em> of the crossfade rather than the
/// end, which is what makes the new colour feel like the cause of the change
/// rather than an afterthought.
/// </para>
/// <para>
/// The four masses each drift on their own two periods, and for every one of
/// them the horizontal and vertical periods are mutually irrational. That is
/// the entire trick: the field never returns to an arrangement it has been in
/// before, so it never reads as a loop however long it is left running.
/// </para>
/// </remarks>
internal sealed class AmbientRenderer : IStyleRenderer, IDisposable
{
    /// <summary>Where each mass sits, and how far it reaches.</summary>
    private static readonly (float X, float Y, float Reach)[] Masses =
    [
        (0.30f, 0.62f, 0.62f),
        (0.72f, 0.40f, 0.70f),
        (0.50f, 0.20f, 0.58f),
        (0.86f, 0.78f, 0.50f),
    ];

    private readonly SaverData _data;
    private readonly AlbumPicker _picker;
    private readonly PaletteStore _palettes;
    private readonly bool _isPreview;

    private readonly Featured _featured;
    private readonly SKPaint _fill = new() { IsAntialias = true };

    public AmbientRenderer(SaverData data, AlbumPicker picker, PaletteStore palettes, bool isPreview)
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
        Log.Write($"ambient layout: {width:0}x{height:0} points");
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

        DrawField(canvas, width, height, phase, palette);

        var side = Math.Min(height * 0.42f, width * 0.32f);

        // A slow rise and fall of less than one per cent of the screen. Small
        // enough that nobody sees it move, large enough that the cover never
        // looks pinned in place.
        var bob = MathF.Sin((float)phase * 0.45f) * height * 0.007f;
        var centreY = (height * 0.415f) - bob;

        var rect = SKRect.Create(
            (width * 0.5f) - (side / 2f), centreY - (side / 2f), side, side);

        DrawReflection(canvas, rect, side, height, index, palette);

        Drawing.DrawFeaturedCover(
            canvas, _data.ImageFor(index), _data.ImageFor(previous), rect,
            _featured.FadeRaw(phase), radius: 8f, shadowAlpha: 0.6f);

        if (_data.Settings.ShowTrackLabel && !_isPreview)
        {
            DrawText(canvas, width, height, rect, index, palette);
        }
    }

    /// <summary>
    /// A dark ground with four soft masses of the album's colour drifting over
    /// it.
    /// </summary>
    private void DrawField(SKCanvas canvas, float width, float height, double phase, ArtPalette palette)
    {
        _fill.Shader = null;
        _fill.Color = palette.Deep.ToSkia();
        canvas.DrawRect(SKRect.Create(0, 0, width, height), _fill);

        var motion = (float)(phase * 0.05 * Math.Max(0.05, _data.Settings.AmbientMotion));
        var reach = Math.Min(width, height);

        for (var i = 0; i < Masses.Length; i++)
        {
            var mass = Masses[i];
            var f = i + 1;

            var colour = i switch
            {
                0 => palette.Accent,
                1 => palette.ConsoleLight,
                2 => palette.Console,
                _ => palette.Accent.WithBrightness(0.55f),
            };

            var x = width * (mass.X + (0.11f * MathF.Sin((motion * f * 0.73f) + f)));

            // The specification places these in AppKit's y-up space, so the
            // vertical fraction is measured from the bottom.
            var yUp = mass.Y + (0.11f * MathF.Cos((motion * f * 0.51f) + (f * 2f)));
            var y = height * (1f - yUp);

            var radius = reach * mass.Reach;

            using var shader = SKShader.CreateRadialGradient(
                new SKPoint(x, y), radius,
                new[] { colour.ToSkia(0.50f), colour.ToSkia(0f) },
                SKShaderTileMode.Clamp);

            _fill.Shader = shader;
            _fill.Color = SKColors.White;
            canvas.DrawCircle(x, y, radius, _fill);
            _fill.Shader = null;
        }
    }

    /// <summary>
    /// The cover mirrored in the surface it is standing on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Drawn before the cover, so the cover prints over the top edge of it and
    /// the two meet with no seam.
    /// </para>
    /// <para>
    /// The mirror is about the top edge of the band, and the square is drawn
    /// just <em>above</em> that line so it lands just below it flipped. What
    /// survives the clip is the bottom 62% of the cover with its own bottom
    /// edge at the top of the band. That adjacency is the whole thing: get it
    /// wrong and it reads as a second, smaller cover rather than as a
    /// reflection.
    /// </para>
    /// <para>
    /// Note it draws the featured cover directly rather than going through the
    /// crossfade, so during a track change the cover dissolves and its
    /// reflection cuts. That is the macOS behaviour, and nobody has ever
    /// noticed, because the reflection is at 22% under a fade to almost
    /// nothing.
    /// </para>
    /// </remarks>
    private void DrawReflection(
        SKCanvas canvas, SKRect rect, float side, float height, int index, ArtPalette palette)
    {
        var bandTop = rect.Bottom + (height * 0.012f);
        var band = SKRect.Create(rect.Left, bandTop, side, side * 0.62f);

        canvas.Save();
        canvas.ClipRect(band);

        canvas.Translate(0, bandTop);
        canvas.Scale(1f, -1f);
        canvas.Translate(0, -bandTop);

        Drawing.DrawCover(
            canvas, _data.ImageFor(index),
            SKRect.Create(rect.Left, bandTop - side, side, side),
            alpha: 0.22f, radius: 6f, shadowAlpha: 0f);

        canvas.Restore();

        // Clear where it meets the cover, almost solid at the far end.
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, band.Top),
            new SKPoint(0, band.Bottom),
            new[] { palette.Deep.ToSkia(0f), palette.Deep.ToSkia(0.95f) },
            SKShaderTileMode.Clamp);

        _fill.Shader = shader;
        _fill.Color = SKColors.White;
        canvas.DrawRect(band, _fill);
        _fill.Shader = null;
    }

    private void DrawText(
        SKCanvas canvas, float width, float height, SKRect rect, int index, ArtPalette palette)
    {
        var (title, artist, sub) = FeaturedText.For(_data, index);

        var columnWidth = width * 0.56f;
        var left = (width * 0.5f) - (columnWidth / 2f);
        var top = rect.Bottom + (height * 0.055f);

        using var shadow = SKImageFilter.CreateDropShadow(
            0f, 2f, 8f, 8f, SKColors.Black.WithAlpha(179));

        var titleSize = TextLayout.Fitted(
            title, columnWidth, height * 0.13f, Math.Max(20f, height * 0.052f), bold: true);

        using var titlePaint = TextLayout.Paint(titleSize, true, palette.Text.ToSkia());
        titlePaint.ImageFilter = shadow;
        top += TextLayout.Draw(canvas, title, left, top, columnWidth, titlePaint, centred: true);
        top += height * 0.018f;

        using var artistPaint = TextLayout.Paint(
            Math.Max(14f, height * 0.030f), false, palette.Accent.ToSkia());
        artistPaint.ImageFilter = shadow;
        top += TextLayout.Draw(canvas, artist, left, top, columnWidth, artistPaint, centred: true);

        if (string.IsNullOrEmpty(sub)) return;

        top += height * 0.010f;

        using var subPaint = TextLayout.Paint(
            Math.Max(11f, height * 0.021f), false, palette.TextMuted.ToSkia());
        subPaint.ImageFilter = shadow;
        TextLayout.Draw(canvas, sub, left, top, columnWidth, subPaint, centred: true);
    }

    public void Dispose() => _fill.Dispose();
}

/// <summary>
/// What a single-album style writes under its cover.
/// </summary>
/// <remarks>
/// The track when that album is the one playing, and the record itself
/// otherwise. The third line only exists in the first case, which is why every
/// style that uses it checks for empty rather than drawing a blank line.
/// </remarks>
internal static class FeaturedText
{
    public static (string Title, string Artist, string Sub) For(SaverData data, int index)
    {
        var albums = data.Albums;
        if (index < 0 || index >= albums.Count) return ("", "", "");

        var album = albums[index];
        var playing = data.NowPlaying;

        if (data.LiveAlbumIndex == index && !string.IsNullOrEmpty(playing.Track))
        {
            return (playing.Track, playing.Artist, playing.AlbumName);
        }

        return (album.Name, album.Artist, "");
    }
}

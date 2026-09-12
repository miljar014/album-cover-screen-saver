using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// The drawing every style shares.
/// </summary>
/// <remarks>
/// <b>Coordinates.</b> The specification is written in AppKit's y-up space:
/// rectangles give their bottom edge as Y, shadow offsets are negative for
/// downward, and the label origins are measured up from the bottom-left corner.
/// Skia is y-down. The conversion happens here and in
/// <see cref="GridBuilder"/>, and nowhere else, so nothing in the port ever
/// mixes the two conventions.
/// </remarks>
internal static class Drawing
{
    /// <summary>Spotify green, from the specification, used for the badge.</summary>
    private static readonly SKColor BadgeGreen = new(28, 214, 97);

    /// <summary>
    /// Reused by every cover blit rather than allocated per cover per frame.
    /// </summary>
    /// <remarks>
    /// Safe as a single shared instance because every window renders in turn on
    /// the one loop thread. At thirty three covers and thirty frames a second,
    /// allocating fresh paints is two thousand short-lived objects a second, and
    /// the collector pauses that causes are visible as jumpiness rather than as
    /// a lower frame rate.
    /// </remarks>
    private static readonly SKPaint CoverPaint = new()
    {
        IsAntialias = true,
        FilterQuality = SKFilterQuality.Medium,
    };

    private static readonly SKTypeface Regular = Face(SKFontStyleWeight.Normal);
    private static readonly SKTypeface Medium = Face(SKFontStyleWeight.Medium);
    private static readonly SKTypeface SemiBold = Face(SKFontStyleWeight.SemiBold);
    private static readonly SKTypeface Bold = Face(SKFontStyleWeight.Bold);

    private static SKTypeface Face(SKFontStyleWeight weight) =>
        SKTypeface.FromFamilyName("Segoe UI", weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        ?? SKTypeface.Default;

    /// <summary>
    /// The aspect-fill cover blitter.
    /// </summary>
    /// <remarks>
    /// The cover is square and the cell usually is not, so the image is drawn
    /// into a square of the rectangle's <em>larger</em> side, centred, and
    /// clipped. That crops a sliver rather than stretching. On album art a crop
    /// reads as nothing at all; a stretch is immediately obvious.
    ///
    /// The shadow is cast by filling the destination with black at the draw
    /// alpha first, so its strength tracks the cover's fade automatically.
    /// </remarks>
    public static void DrawImage(
        SKCanvas canvas, SKBitmap? image, SKRect rect, float alpha, float radius, bool shadow)
    {
        if (image is null || alpha <= 0.01f) return;

        if (shadow)
        {
            // Black at 0.55 x alpha, blur 6% of the width, offset 2% downward.
            // CoreGraphics states shadow blur as a diameter, so sigma is half.
            //
            // Fine for a style drawing one or two covers a frame. A style
            // drawing dozens must use ShadowSprite instead: see the note there
            // about what this costs at scale.
            var blur = rect.Width * 0.06f / 2f;
            using var shadowPaint = new SKPaint
            {
                IsAntialias = true,
                ImageFilter = SKImageFilter.CreateDropShadowOnly(
                    0f, rect.Width * 0.02f, blur, blur,
                    SKColors.Black.WithAlpha((byte)(0.55f * alpha * 255f))),
            };
            canvas.DrawRoundRect(rect, radius, radius, shadowPaint);
        }

        canvas.Save();

        using (var clip = new SKRoundRect(rect, radius, radius))
        {
            canvas.ClipRoundRect(clip, SKClipOperation.Intersect, antialias: true);
        }

        var side = Math.Max(rect.Width, rect.Height);
        var destination = SKRect.Create(rect.MidX - (side / 2f), rect.MidY - (side / 2f), side, side);

        CoverPaint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(alpha * 255f, 0f, 255f));
        canvas.DrawBitmap(image, destination, CoverPaint);

        canvas.Restore();
    }

    /// <summary>
    /// Draws one grid tile, including its flip.
    /// </summary>
    /// <remarks>
    /// The flip is a horizontal squash, not a 3D rotation, so there is no
    /// perspective and no back face. <c>|cos(t pi)|</c> runs 1 to 0 at the
    /// halfway point and back to 1, and the album is swapped exactly at the
    /// zero-width moment, which is what makes it read as a card turning rather
    /// than as a crossfade.
    ///
    /// The half-point inset on every side is a hairline that stops neighbouring
    /// covers sharing an antialiased edge and bleeding into one another.
    /// </remarks>
    public static void DrawTile(
        SKCanvas canvas, Tile tile, SKBitmap? image, double phase,
        float alpha = 1f, SKRect? into = null)
    {
        var squash = 1f;

        if (tile.IsFlipping)
        {
            var t = (float)((phase - tile.FlipStart) / Math.Max(0.15, tile.FlipDuration));
            squash = Math.Max(0.02f, Math.Abs(MathF.Cos(t * MathF.PI)));
        }

        // Slow-Building Wall passes a shrunken rectangle so a tile grows into
        // its cell as it fades in. Everything else draws the cell itself.
        var rect = into ?? SKRect.Create(tile.Cell.X, tile.Cell.Y, tile.Cell.Width, tile.Cell.Height);
        rect.Inflate(-0.5f, -0.5f);

        canvas.Save();

        if (squash < 1f)
        {
            var centreX = rect.MidX;
            var centreY = rect.MidY;
            canvas.Translate(centreX, centreY);
            canvas.Scale(squash, 1f);
            canvas.Translate(-centreX, -centreY);
        }

        DrawImage(canvas, image, rect, alpha, radius: 0f, shadow: false);

        canvas.Restore();
    }

    /// <summary>
    /// A cover with the softer, heavier shadow the single-album styles use.
    /// </summary>
    /// <remarks>
    /// A different profile from <see cref="DrawImage"/>: blur a tenth of the
    /// width against six per cent, and a bigger drop. Both exist on purpose. A
    /// cover in a grid is flat against the wall; a featured one is a record
    /// propped up in front of you, and the shadow is what says so.
    ///
    /// The shadow strength is multiplied by the draw alpha, so a cover fading
    /// out takes its shadow with it instead of leaving a dark patch behind.
    /// </remarks>
    public static void DrawCover(
        SKCanvas canvas, SKBitmap? image, SKRect rect, float alpha, float radius, float shadowAlpha)
    {
        if (image is null || alpha <= 0.01f) return;

        if (shadowAlpha > 0f)
        {
            var blur = rect.Width * 0.10f / 2f;
            using var shadowPaint = new SKPaint
            {
                IsAntialias = true,
                ImageFilter = SKImageFilter.CreateDropShadowOnly(
                    0f, rect.Width * 0.035f, blur, blur,
                    SKColors.Black.WithAlpha((byte)Math.Clamp(shadowAlpha * alpha * 255f, 0f, 255f))),
            };
            canvas.DrawRoundRect(rect, radius, radius, shadowPaint);
        }

        DrawImage(canvas, image, rect, alpha, radius, shadow: false);
    }

    /// <summary>A rectangle scaled about its own centre.</summary>
    public static SKRect Scaled(SKRect rect, float factor) => SKRect.Create(
        rect.MidX - (rect.Width * factor / 2f),
        rect.MidY - (rect.Height * factor / 2f),
        rect.Width * factor,
        rect.Height * factor);

    /// <summary>
    /// The crossfade the single-album styles share: one cover replacing another.
    /// </summary>
    /// <remarks>
    /// The outgoing cover drifts back and away while the incoming one rises
    /// into place, and the seven per cent difference in scale between them is
    /// the whole effect. Without it the two images simply dissolve into each
    /// other, which reads as a glitch. With it, it reads as one object being
    /// replaced by another.
    /// </remarks>
    public static void DrawFeaturedCover(
        SKCanvas canvas, SKBitmap? incoming, SKBitmap? outgoing, SKRect rect,
        float fadeRaw, float radius, float shadowAlpha)
    {
        if (fadeRaw >= 1f)
        {
            DrawCover(canvas, incoming, rect, 1f, radius, shadowAlpha);
            return;
        }

        var eased = Ease.InOut(fadeRaw);

        DrawCover(
            canvas, outgoing, Scaled(rect, 1f + (0.07f * eased)),
            1f - eased, radius, shadowAlpha * (1f - eased));

        DrawCover(
            canvas, incoming, Scaled(rect, 0.93f + (0.07f * eased)),
            eased, radius, shadowAlpha * eased);
    }

    /// <summary>Flat black over everything drawn so far.</summary>
    public static void Scrim(SKCanvas canvas, float width, float height, float alpha)
    {
        if (alpha <= 0.002f) return;

        using var paint = new SKPaint
        {
            Color = SKColors.Black.WithAlpha((byte)Math.Clamp(alpha * 255f, 0f, 255f)),
        };
        canvas.DrawRect(SKRect.Create(0, 0, width, height), paint);
    }

    /// <summary>
    /// The label three of the four base styles use: the most recently played
    /// album in the archive.
    /// </summary>
    /// <remarks>
    /// Deliberately not whatever cover happens to be on screen, and never a
    /// now-playing badge. Only Hero + Grid shows the live track.
    /// </remarks>
    public static void DrawArchiveLabel(
        SKCanvas canvas, SaverData data, float width, float height, bool isPreview)
    {
        if (data.Albums.Count == 0) return;
        var newest = data.Albums[0];

        DrawLabel(
            canvas, width, height,
            newest.Name, newest.Artist, badge: null,
            isPreview, data.Settings.ShowTrackLabel);
    }

    /// <summary>
    /// The bottom-left overlay shared by the grid styles.
    /// </summary>
    /// <remarks>
    /// The specification places these at 44, 68 and 96 points <em>up from the
    /// bottom-left corner</em>, as text origins. Converted to y-down, the
    /// baselines sit at height minus those numbers. All three are single line,
    /// unwrapped and unclipped: a long title runs off the right edge, which is
    /// existing behaviour.
    /// </remarks>
    public static void DrawLabel(
        SKCanvas canvas, float width, float height,
        string title, string subtitle, string? badge, bool isPreview, bool showTrackLabel)
    {
        if (!showTrackLabel || isPreview) return;

        // Black at 0.9, blur 6 (so sigma 3), offset 1 point downward.
        using var shadow = SKImageFilter.CreateDropShadow(
            0f, 1f, 3f, 3f, SKColors.Black.WithAlpha(230));

        const float left = 44f;

        if (!string.IsNullOrEmpty(badge))
        {
            using var badgePaint = new SKPaint
            {
                IsAntialias = true,
                Color = BadgeGreen,
                TextSize = 11f,
                Typeface = Bold,
                ImageFilter = shadow,
            };
            DrawTracked(canvas, badge, left, height - 96f, 1.6f, badgePaint);
        }

        if (!string.IsNullOrEmpty(subtitle))
        {
            using var subtitlePaint = new SKPaint
            {
                IsAntialias = true,
                Color = SKColors.White.WithAlpha(209),
                TextSize = 17f,
                Typeface = Regular,
                ImageFilter = shadow,
            };
            canvas.DrawText(subtitle, left, height - 44f, subtitlePaint);
        }

        if (!string.IsNullOrEmpty(title))
        {
            using var titlePaint = new SKPaint
            {
                IsAntialias = true,
                Color = SKColors.White,
                TextSize = 22f,
                Typeface = SemiBold,
                ImageFilter = shadow,
            };
            canvas.DrawText(title, left, height - 68f, titlePaint);
        }
    }

    /// <summary>
    /// Draws text with extra space between characters. Skia has no letter
    /// spacing, so the run is advanced by hand.
    /// </summary>
    private static void DrawTracked(
        SKCanvas canvas, string text, float x, float y, float tracking, SKPaint paint)
    {
        foreach (var character in text)
        {
            var glyph = character.ToString();
            canvas.DrawText(glyph, x, y, paint);
            x += paint.MeasureText(glyph) + tracking;
        }
    }

    /// <summary>
    /// Shown when there is no art at all. Deliberately reworded from the macOS
    /// text, which names the menu bar and Spotify: Windows has a tray, and this
    /// build ships no Spotify source.
    /// </summary>
    public static void DrawEmptyState(SKCanvas canvas, float width, float height, bool isPreview)
    {
        string[] lines =
        [
            "No album art yet.",
            "",
            "Open Album Cover Screen Saver from the system tray and connect",
            "a music source, and the collage will fill in as your history",
            "is archived.",
            "",
            "Styles and options live in that app's Settings window.",
        ];

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(166, 166, 166),
            TextSize = isPreview ? 9f : 22f,
            TextAlign = SKTextAlign.Center,
            Typeface = Medium,
        };

        var lineHeight = paint.TextSize * 1.45f;
        var start = (height / 2f) - (lineHeight * (lines.Length - 1) / 2f);

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length == 0) continue;
            canvas.DrawText(lines[i], width / 2f, start + (lineHeight * i), paint);
        }
    }

    /// <summary>The corner line that makes the window-per-display model checkable.</summary>
    public static void DrawDiagnostic(SKCanvas canvas, string text, float width, float height)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White.WithAlpha(115),
            TextSize = 14f,
            Typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyleWeight.Normal,
                SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default,
        };

        canvas.DrawText(text, 28f, 36f, paint);
        canvas.DrawText("move the mouse or press a key to quit", 28f, height - 28f, paint);
    }
}

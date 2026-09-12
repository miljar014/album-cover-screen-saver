using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// Wrapped text: measuring it, shrinking it until it fits, and drawing it.
/// </summary>
/// <remarks>
/// <para>
/// <b>On the font.</b> The macOS build uses Futura for its retro styles, with a
/// fallback to the system font. Windows does not ship Futura, so the fallback is
/// the normal path here: Century Gothic where it exists, then Segoe UI. Metrics
/// differ between them and that is harmless, because every caller goes through
/// <see cref="Fitted"/>, which measures. A substitute lands on a different
/// chosen size rather than on clipped text.
/// </para>
/// <para>
/// Skia has no wrapping text layout of its own, so the wrap is done here: break
/// on spaces, keep adding words while they fit, and start a new line when they
/// do not. A single word longer than the line is left to overflow rather than
/// being broken mid-word, which is what a person would do with a title.
/// </para>
/// </remarks>
internal static class TextLayout
{
    /// <summary>Line height as a multiple of the font size.</summary>
    /// <remarks>
    /// The macOS version takes this from the font's own leading. Skia reports
    /// leading too, and it is used below; this is only the floor, for a face
    /// that reports none.
    /// </remarks>
    private const float MinimumLineSpacing = 1.15f;

    private static readonly SKTypeface Retro = Pick("Century Gothic", SKFontStyleWeight.Medium);
    private static readonly SKTypeface RetroBold = Pick("Century Gothic", SKFontStyleWeight.Bold);

    private static SKTypeface Pick(string family, SKFontStyleWeight weight) =>
        SKTypeface.FromFamilyName(family, weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        ?? SKTypeface.FromFamilyName("Segoe UI", weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        ?? SKTypeface.Default;

    public static SKTypeface Face(bool bold) => bold ? RetroBold : Retro;

    /// <summary>A paint ready to measure or draw with.</summary>
    public static SKPaint Paint(float size, bool bold, SKColor colour, float tracking = 0f) => new()
    {
        IsAntialias = true,
        TextSize = size,
        Typeface = Face(bold),
        Color = colour,
        TextAlign = SKTextAlign.Left,
        TextScaleX = 1f,
        // Tracking is applied by hand in Draw, because Skia has no letter
        // spacing property of its own.
    };

    /// <summary>The height this text takes when wrapped into a given width.</summary>
    public static float Measure(string text, float size, bool bold, float width, float tracking = 0f)
    {
        if (string.IsNullOrEmpty(text) || width <= 0f) return 0f;

        using var paint = Paint(size, bold, SKColors.White);
        var lines = Wrap(text, paint, width, tracking);
        return MathF.Ceiling(lines.Count * LineHeight(paint));
    }

    /// <summary>
    /// The largest size at or below <paramref name="baseSize"/> whose wrapped
    /// text fits the box, down to a floor of 45%.
    /// </summary>
    public static float Fitted(string text, float width, float maxHeight, float baseSize, bool bold) =>
        TextFit.Size(baseSize, maxHeight, size => Measure(text, size, bold, width));

    /// <summary>
    /// Draws wrapped text and returns the height it used.
    /// </summary>
    /// <param name="top">
    /// The top edge of the block. The text grows downward from it, so callers
    /// stack blocks with <c>top += Draw(...)</c>.
    /// </param>
    /// <remarks>
    /// An empty string draws nothing and returns zero, so a missing subtitle
    /// closes the gap it would have taken rather than leaving a hole.
    /// </remarks>
    public static float Draw(
        SKCanvas canvas, string text, float x, float top, float width,
        SKPaint paint, bool centred = false, float tracking = 0f)
    {
        if (string.IsNullOrEmpty(text) || width <= 0f) return 0f;

        var lines = Wrap(text, paint, width, tracking);
        var lineHeight = LineHeight(paint);

        // Skia draws from the baseline, so the first line sits one ascent below
        // the top edge of the block.
        var baseline = top - paint.FontMetrics.Ascent;

        foreach (var line in lines)
        {
            var lineWidth = Width(line, paint, tracking);
            var left = centred ? x + ((width - lineWidth) / 2f) : x;

            if (tracking == 0f) canvas.DrawText(line, left, baseline, paint);
            else DrawTracked(canvas, line, left, baseline, tracking, paint);

            baseline += lineHeight;
        }

        return MathF.Ceiling(lines.Count * lineHeight);
    }

    /// <summary>One line, no wrapping, drawn from its own left edge.</summary>
    public static void DrawLine(
        SKCanvas canvas, string text, float x, float baseline, SKPaint paint, float tracking = 0f)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (tracking == 0f) canvas.DrawText(text, x, baseline, paint);
        else DrawTracked(canvas, text, x, baseline, tracking, paint);
    }

    /// <summary>
    /// Draws with extra space between characters. Skia has no letter spacing, so
    /// the run is advanced by hand.
    /// </summary>
    private static void DrawTracked(
        SKCanvas canvas, string text, float x, float baseline, float tracking, SKPaint paint)
    {
        foreach (var character in text)
        {
            var glyph = character.ToString();
            canvas.DrawText(glyph, x, baseline, paint);
            x += paint.MeasureText(glyph) + tracking;
        }
    }

    public static float Width(string text, SKPaint paint, float tracking = 0f)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        return paint.MeasureText(text) + (tracking * (text.Length - 1));
    }

    private static float LineHeight(SKPaint paint)
    {
        var metrics = paint.FontMetrics;

        // Descent is positive and ascent negative, so this is the full band,
        // plus whatever leading the face asks for.
        var fromFont = metrics.Descent - metrics.Ascent + metrics.Leading;
        return Math.Max(fromFont, paint.TextSize * MinimumLineSpacing);
    }

    /// <summary>
    /// Breaks text into lines that fit a width.
    /// </summary>
    /// <remarks>
    /// A single word wider than the line is left on its own line and allowed to
    /// overflow rather than being split. Hyphenating an album title mid-word
    /// looks like a rendering fault; an overhanging one looks like a long title.
    /// </remarks>
    private static List<string> Wrap(string text, SKPaint paint, float width, float tracking)
    {
        var lines = new List<string>();

        foreach (var paragraph in text.Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                lines.Add("");
                continue;
            }

            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var line = "";

            foreach (var word in words)
            {
                var candidate = line.Length == 0 ? word : $"{line} {word}";

                if (Width(candidate, paint, tracking) <= width || line.Length == 0)
                {
                    line = candidate;
                    continue;
                }

                lines.Add(line);
                line = word;
            }

            if (line.Length > 0) lines.Add(line);
        }

        return lines;
    }
}

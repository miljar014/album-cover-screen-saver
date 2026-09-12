namespace AlbumCoverScreenSaver.Shared;

/// <summary>One dot of phosphor: whether it lights at all, how big, how bright.</summary>
public readonly record struct Phosphor(bool Lit, float Diameter, float Alpha);

/// <summary>One line of the terminal's readout, before any of it has been typed.</summary>
public readonly record struct ConsoleLine(string Text, bool Small, float Brightness);

/// <summary>As much of one line as has been typed so far.</summary>
public readonly record struct Shown(int Index, string Text, bool Partial);

/// <summary>Everything the terminal has on screen this frame.</summary>
public readonly record struct Transcript(IReadOnlyList<Shown> Lines, bool Complete);

/// <summary>
/// CRT Terminal: a phosphor screen with the cover printed on it in dots and the
/// track typed out beside it.
/// </summary>
/// <remarks>
/// The cover is halftoned rather than pasted in, using the same grid as
/// Newsstand but inverted: there the dots are ink on paper, here they are light
/// on a dark tube. A full-colour photograph on a green screen would break the
/// illusion in the first frame.
/// </remarks>
public static class CrtTerminal
{
    /// <summary>Below this much light the tube is simply left dark.</summary>
    private const float DarkThreshold = 0.06f;

    /// <summary>Characters typed per second.</summary>
    public const float TypingSpeed = 38f;

    /// <summary>The scanlines are in device pixels and do not scale with the screen.</summary>
    public const float ScanPeriodPixels = 4f;
    public const float ScanBarPixels = 1.5f;
    public const float ScanAlpha = 0.22f;

    /// <summary>
    /// The dot for one cell of the halftone grid.
    /// </summary>
    /// <remarks>
    /// Both the size and the brightness carry the value, which is the difference
    /// between this and the Newsstand mapping. A bright cell overflows its own
    /// cell by up to eight per cent and at full brightness as well, so highlights
    /// run into each other and bloom the way they do on a real tube.
    /// </remarks>
    public static Phosphor DotFor(float value, float cell, float fade)
    {
        if (value <= DarkThreshold) return new Phosphor(false, 0f, 0f);

        return new Phosphor(
            Lit: true,
            Diameter: cell * (0.30f + (value * 0.78f)),
            Alpha: (0.25f + (value * 0.75f)) * fade);
    }

    /// <summary>The side of the square halftone portrait, in points.</summary>
    public static float PortraitSide(float width, float height) =>
        Math.Min(height * 0.52f, width * 0.34f);

    /// <summary>The left edge of the typed column.</summary>
    public static float ReadoutLeft(float width, float side) => (width * 0.16f) + side;

    /// <summary>How wide that column is.</summary>
    public static float ReadoutWidth(float width, float left) => width - left - (width * 0.07f);

    /// <summary>
    /// How many characters have been typed since the album came up.
    /// </summary>
    /// <remarks>
    /// A whole number, so the text advances a character at a time rather than
    /// sliding. Thirty eight a second puts a typical block out in two or three
    /// seconds.
    /// </remarks>
    public static int Budget(double phase, double shownSince) =>
        (int)Math.Floor(Math.Max(0.0, phase - shownSince) * TypingSpeed);

    /// <summary>The block cursor blinks once a second: half on, half off.</summary>
    public static bool CursorVisible(double phase) => (int)Math.Floor(phase * 2.0) % 2 == 0;

    /// <summary>
    /// The readout, as eight lines.
    /// </summary>
    /// <remarks>
    /// The last line only exists when something is playing, because it is the
    /// album a track came from and there is no such thing when the style is
    /// rotating through the archive on its own.
    /// </remarks>
    public static List<ConsoleLine> Lines(string title, string artist, string sub, bool live)
    {
        var lines = new List<ConsoleLine>
        {
            new("SPOTIFYCOLLAGE v1.0", true, 0.45f),
            new("READY.", true, 0.45f),
            new("", true, 0.45f),
            new(live ? "> NOW PLAYING" : "> FROM ARCHIVE", true, 0.80f),
            new("", true, 0.45f),
            new(title.ToUpperInvariant(), false, 1.00f),
            new(artist.ToUpperInvariant(), false, 0.75f),
        };

        if (!string.IsNullOrEmpty(sub)) lines.Add(new ConsoleLine(sub.ToUpperInvariant(), true, 0.55f));

        return lines;
    }

    /// <summary>
    /// How much of the readout is on screen, given a character budget.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing below an unfinished line is returned at all: a terminal that
    /// printed line six while line five was still going would not be a terminal.
    /// </para>
    /// <para>
    /// A blank line costs one character even though it has none, which is what
    /// makes the pauses between the blocks real rather than instant. The
    /// specification writes that as <c>budget -= max(1, length)</c> and then
    /// takes a prefix of the remainder, which can ask for a prefix of minus one;
    /// it is clamped here.
    /// </para>
    /// </remarks>
    public static Transcript Reveal(IReadOnlyList<ConsoleLine> lines, int budget)
    {
        var shown = new List<Shown>(lines.Count);

        for (var i = 0; i < lines.Count; i++)
        {
            var text = lines[i].Text;

            if (budget >= text.Length)
            {
                shown.Add(new Shown(i, text, false));
                budget -= Math.Max(1, text.Length);
                continue;
            }

            shown.Add(new Shown(i, text[..Math.Clamp(budget, 0, text.Length)], true));
            return new Transcript(shown, false);
        }

        return new Transcript(shown, true);
    }

    /// <summary>Where the block of text starts, measured down from the top.</summary>
    /// <remarks>
    /// The block is pulled up by a fixed amount per line so that it stays near
    /// the middle of the screen however many lines it has, rather than growing
    /// downward off the bottom.
    /// </remarks>
    public static float BlockTop(float height, int lineCount) =>
        (height * 0.5f) - (lineCount * height * 0.021f);
}

namespace AlbumCoverScreenSaver.Shared;

/// <summary>One printed dot: whether to print it at all, and how big.</summary>
public readonly record struct Dot(bool Print, float Diameter);

/// <summary>
/// Turning a cover into printed dots, the way a newspaper photograph is made.
/// </summary>
/// <remarks>
/// The cover is reduced to a small grid of brightness values and each cell gets
/// one dot of ink sized by how dark that cell is. Dark areas get dots wide
/// enough to overlap their neighbours, which is exactly how a real halftone
/// fills in solid black.
/// </remarks>
public static class Halftone
{
    /// <summary>Cells across and down. A newspaper screen, not a photograph.</summary>
    public const int Grid = 46;

    /// <summary>Below this much ink the paper is simply left as paper.</summary>
    private const float PaperThreshold = 0.05f;

    /// <summary>
    /// The dot for a cell of a given brightness.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ink is the inverse of light, so a bright cell gets a small dot and a dark
    /// one a large dot.
    /// </para>
    /// <para>
    /// At maximum the dot is a fifth wider than its own cell and therefore runs
    /// into its neighbours. That is not a rounding error: it is the only way a
    /// halftone reaches solid black. The floor at the other end keeps a visible
    /// speck in the lightest printed areas, so a pale sky reads as printed
    /// rather than as a hole in the page.
    /// </para>
    /// </remarks>
    public static Dot DotFor(float brightness, float cell)
    {
        var ink = 1f - brightness;
        if (ink <= PaperThreshold) return new Dot(false, 0f);

        return new Dot(true, cell * (0.25f + (ink * 0.95f)));
    }

    /// <summary>
    /// Where in the photograph a cell of the grid is drawn.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Row zero is the top row, and there is no flip here. Read the rest of
    /// this before changing it.</b>
    /// </para>
    /// <para>
    /// A bitmap's rows are stored top first. On macOS the drawing space runs the
    /// other way, with y increasing upward, so the macOS build has to map row r
    /// to visual row <c>n - 1 - r</c> or the photograph comes out upside down.
    /// That line exists in the Mac source with a comment on it, and the
    /// specification flags it as a real bug that was found and fixed.
    /// </para>
    /// <para>
    /// On Windows both conventions already agree: the bitmap is top first and so
    /// is the canvas. Porting the Mac's flip across would reintroduce exactly
    /// the same bug in a mirror image. So: no flip, deliberately, and this note
    /// is here because the specification says the next person to touch it will
    /// get it wrong.
    /// </para>
    /// </remarks>
    public static (float X, float Y) CellOrigin(int row, int column, float cell, float diameter)
    {
        var inset = (cell - diameter) / 2f;
        return ((column * cell) + inset, (row * cell) + inset);
    }

    /// <summary>Rec.709 luma on raw bytes, deliberately not gamma corrected.</summary>
    /// <remarks>
    /// Skipping the gamma step is what gives the dots their contrast. It is
    /// wrong as colour science and right as printing, and the specification says
    /// in as many words not to fix it.
    /// </remarks>
    public static float Luma(byte red, byte green, byte blue) =>
        ((0.2126f * red) + (0.7152f * green) + (0.0722f * blue)) / 255f;
}

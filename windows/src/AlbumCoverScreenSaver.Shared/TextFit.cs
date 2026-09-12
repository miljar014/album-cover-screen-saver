namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// Shrinks a font size until the text fits the space it has.
/// </summary>
/// <remarks>
/// <para>
/// Album titles vary from "Aja" to "Everything Everything Everything", and every
/// style that draws one has a fixed box to put it in. So a long title wraps
/// <em>and</em> scales down, to a floor of 45% of the base size, rather than
/// being clipped.
/// </para>
/// <para>
/// The measuring is passed in, because it is the one part that needs a real font
/// and a real text engine. Everything else is a loop, and lives here so the loop
/// itself can be proved: that it terminates, that it never goes below the floor,
/// and that it returns the base size untouched when the text already fits.
/// </para>
/// <para>
/// This is also what makes the missing Futura harmless. The Mac uses Futura for
/// its retro styles and Windows does not have it, but every use of it goes
/// through here, so a substitute with different metrics simply lands on a
/// different chosen size rather than on clipped text.
/// </para>
/// </remarks>
public static class TextFit
{
    /// <summary>The smallest fraction of the base size this will return.</summary>
    public const float Floor = 0.45f;

    /// <summary>Each step is 8% smaller, which is about ten steps to the floor.</summary>
    private const float Shrink = 0.92f;

    /// <summary>
    /// The largest size at or below <paramref name="baseSize"/> whose wrapped
    /// height fits, or the floor if nothing does.
    /// </summary>
    /// <param name="measureHeight">
    /// Given a font size, the wrapped height of the text at the available width.
    /// </param>
    public static float Size(float baseSize, float maxHeight, Func<float, float> measureHeight)
    {
        if (baseSize <= 0f) return 0f;

        var size = baseSize;
        var floor = baseSize * Floor;

        // Strictly greater than the floor, so the loop always terminates even if
        // the text never fits at any size.
        while (size > floor)
        {
            if (measureHeight(size) <= maxHeight) return size;
            size *= Shrink;
        }

        return floor;
    }
}

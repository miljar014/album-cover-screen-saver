namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// How much of a Newsstand front page is left by the time the photograph goes
/// on it.
/// </summary>
/// <remarks>
/// <para>
/// The page is built from the top down and everything above the photograph
/// sizes itself to its own text: the masthead shrinks to fit the artist's name,
/// the headline grows to one line or two depending on the track. So how far
/// down the page the photograph starts is not known until it is reached.
/// </para>
/// <para>
/// A photograph sized only from the width of the page therefore runs off the
/// bottom of a short screen, or of any screen whose headline needed two lines,
/// and it takes its caption and the colour bar with it. This is the rule that
/// stops that: take the width the design asks for, then give up whatever the
/// page cannot afford.
/// </para>
/// <para>
/// <b>A deliberate departure from the specification.</b> The Mac sizes the
/// photograph from the width of the page alone, <c>colW * 0.46</c>, with no
/// check that it fits, so its own page overhangs the bottom of the screen by a
/// few per cent on a sixteen-by-ten display and a great deal more on a wider
/// one. It was caught on Windows only because the wider screen made it
/// unmissable. The width the specification asks for is kept as the ceiling, so
/// on a page with room the two builds are identical.
/// </para>
/// </remarks>
public static class NewsstandPage
{
    /// <summary>
    /// The foot of the page as a fraction of the screen height. The colour bar
    /// sits below this and nothing else may.
    /// </summary>
    public const float BottomFraction = 0.930f;

    /// <summary>Room kept under the photograph for its caption.</summary>
    public const float CaptionFraction = 0.032f;

    /// <summary>The width of the photograph as the design asks for it.</summary>
    public const float WantedFraction = 0.46f;

    /// <summary>
    /// A floor, because a page with a stamp on it is worse than a page with a
    /// photograph slightly too big for it.
    /// </summary>
    public const float FloorFraction = 0.22f;

    /// <summary>
    /// The side of the square photograph, in points.
    /// </summary>
    /// <param name="columnWidth">The printed width of the page, inside its margins.</param>
    /// <param name="height">The screen height.</param>
    /// <param name="top">How far down the page the photograph starts.</param>
    public static float PhotoSide(float columnWidth, float height, float top)
    {
        var room = (height * BottomFraction) - top - (height * CaptionFraction);

        return Math.Max(
            columnWidth * FloorFraction,
            Math.Min(columnWidth * WantedFraction, room));
    }
}

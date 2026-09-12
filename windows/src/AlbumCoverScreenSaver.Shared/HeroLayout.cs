namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// The arithmetic behind Hero + Grid: how big the featured cover is, and which
/// album it rotates to when nothing is playing.
/// </summary>
public static class HeroLayout
{
    /// <summary>Seconds the crossfade between two heroes takes.</summary>
    public const double FadeDuration = 1.1;

    /// <summary>
    /// The background grid is texture, not subject, so its cells are 75% of the
    /// size Mosaic would use at the same setting. It is denser on purpose.
    /// </summary>
    public const float GridCellFraction = 0.75f;

    /// <summary>
    /// The side of the featured square, in points.
    /// </summary>
    /// <remarks>
    /// The width term is what keeps the hero inside a narrow window. On a 16:9
    /// display the height term always wins (0.56 H against 0.42 W, which is
    /// 0.747 H), so the width term looks like dead code until the saver runs on
    /// something portrait or heavily letterboxed, where it is the only thing
    /// stopping the cover from running off both sides.
    /// </remarks>
    public static float Side(float width, float height, double heroSize)
    {
        var fraction = (float)Math.Clamp(heroSize, 0.2, 0.9);
        return Math.Min(height * fraction, width * fraction * 0.75f);
    }

    /// <summary>
    /// The exclusive upper bound for the idle rotation's pick.
    /// </summary>
    /// <remarks>
    /// The hero is the headline, so it leans on recency harder than the grid
    /// behind it does: a uniform draw from the newest 12.5% of the archive
    /// rather than the picker's 20%-head-or-everything coin flip. Note that
    /// <c>recencyBias</c> has no effect on the hero at all once there are more
    /// than three albums.
    /// </remarks>
    public static int IdleRotationBound(int albumCount) =>
        albumCount > 3 ? Math.Max(3, albumCount / 8) : albumCount;

    /// <summary>True when the idle rotation applies rather than a plain pick.</summary>
    public static bool RotationIsNarrowed(int albumCount) => albumCount > 3;

    /// <summary>The interval between hero swaps, floored so it cannot thrash.</summary>
    public static double Interval(double heroInterval) => Math.Max(2.0, heroInterval);
}

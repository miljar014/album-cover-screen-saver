namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// The station of Subway Platform: tiled wall, poster slots, and a train that
/// goes through every twenty six seconds.
/// </summary>
public static class SubwayPlatform
{
    /// <summary>Seconds between trains.</summary>
    public const double TrainCycle = 26.0;

    /// <summary>The fraction of that cycle a train is actually on screen.</summary>
    public const double TrainOnScreen = 0.34;

    /// <summary>How far up the screen the platform edge sits.</summary>
    public const float PlatformFraction = 0.26f;

    /// <summary>Where we are in the train's cycle, 0 to 1.</summary>
    public static double TrainCyclePosition(double phase) =>
        phase % TrainCycle / TrainCycle;

    public static bool TrainIsVisible(double phase) =>
        TrainCyclePosition(phase) < TrainOnScreen;

    /// <summary>
    /// The left edge of the train, as a multiple of the screen width.
    /// </summary>
    /// <remarks>
    /// It starts fully off the left and ends fully off the right, travelling two
    /// and a bit screen widths in under nine seconds. Then nothing for another
    /// seventeen, which is most of what makes it feel like a station rather than
    /// a train set.
    /// </remarks>
    public static float TrainLeft(double phase)
    {
        var through = TrainCyclePosition(phase) / TrainOnScreen;
        return (float)(-1.1 + (2.4 * through));
    }

    /// <summary>
    /// How light one tile is, from 0.86 to 0.96.
    /// </summary>
    /// <remarks>
    /// Seeded from the row and the tile's x <em>in points</em> rather than from
    /// its column number. That is not an accident worth correcting: with a
    /// fractional tile size the truncated position does not repeat down a
    /// column, so the wall has no visible vertical banding.
    /// </remarks>
    public static float TileShade(int row, float x) =>
        0.86f + ((float)Noise.Hash01((row * 71) + (int)x) * 0.10f);

    /// <summary>How many posters fit along the platform.</summary>
    public static int PosterSlots(float width, float posterWidth) =>
        Math.Max(2, (int)(width / (posterWidth * 1.5f)));

    /// <summary>Which one is the lit, backlit box showing what is playing.</summary>
    public static int LitSlot(int slots) => slots / 2;

    /// <summary>Where a poster's centre sits across the platform.</summary>
    public static float PosterCentre(float width, int slot, int slots) =>
        (width * 0.10f) + (width * 0.82f * slot / Math.Max(1, slots - 1));
}

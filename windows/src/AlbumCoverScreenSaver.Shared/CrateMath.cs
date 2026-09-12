namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// The perspective of Crate Digging: a row of sleeves leaning back into a box.
/// </summary>
/// <remarks>
/// Four cues, all driven by one number. A sleeve further back is smaller, sits a
/// little higher, leans a little further over, and is hazier. No one of them is
/// enough on its own, and together they read as depth without any actual
/// perspective maths at all.
/// </remarks>
public static class CrateMath
{
    /// <summary>
    /// Depth stops changing after this many sleeves. Everything beyond the
    /// thirteenth is drawn identically, which is invisible because by then the
    /// haze is at two thirds and the covers are barely there.
    /// </summary>
    private const float DepthLimit = 12f;

    /// <summary>The side of a sleeve at the front of the crate, in points.</summary>
    public static float Side(float width, float height) =>
        Math.Min(height * 0.46f, width * 0.30f);

    /// <summary>
    /// The gap between sleeves.
    /// </summary>
    /// <remarks>
    /// Derived from a fractionally smaller reference than the sleeve itself
    /// (0.44 against 0.46), which is why the sleeves overlap slightly rather
    /// than standing in a neat row. Records in a crate lean on each other.
    /// </remarks>
    public static float Spacing(float width, float height) =>
        Math.Min(height * 0.44f, width * 0.30f) * 0.15f;

    /// <summary>Seconds a sleeve spends at the front before the next slides in.</summary>
    public static double Dwell(double featureSeconds) => Math.Max(3.0, featureSeconds / 6.0);

    /// <summary>How many sleeves to build, which is two more than are asked for.</summary>
    /// <remarks>
    /// The two extra are off the right-hand edge, so there is always something
    /// to slide in and the row never runs out mid-travel.
    /// </remarks>
    public static int Want(int crateCount) => Math.Clamp(crateCount, 4, 40) + 2;

    /// <summary>How many to keep when live playback keeps inserting at the front.</summary>
    public static int RetentionCap(int crateCount) => Math.Max(6, crateCount + 2);

    /// <summary>How far back the sleeve in a given position is, 0 to 1.</summary>
    public static float DepthAt(int position) => Math.Min(1f, position / DepthLimit);

    public static float ScaleOf(float depth) => 1f - (depth * 0.10f);

    /// <summary>Black-out from the album's dark colour, thickening with distance.</summary>
    public static float HazeOf(float depth) => 0.20f + (depth * 0.45f);

    /// <summary>
    /// How far a sleeve leans, in radians. Everything tips the same way, which
    /// is what a hand pushing through a crate leaves behind.
    /// </summary>
    public static float LeanOf(float depth) => -0.055f - (depth * 0.03f);

    /// <summary>How much higher up a sleeve sits, as a fraction of the height.</summary>
    public static float RiseOf(float depth) => depth * 0.03f;
}

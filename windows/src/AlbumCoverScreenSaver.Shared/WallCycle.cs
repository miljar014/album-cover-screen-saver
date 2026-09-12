namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// The timing of one Slow-Building Wall cycle: fill, hold, dissolve, repeat.
/// </summary>
/// <remarks>
/// <para>
/// All of it is arithmetic over a single number, the seconds elapsed since the
/// cycle began, so it lives here and can be proved by test rather than by
/// watching a wall for half a minute to see whether a tile came back.
/// </para>
/// <para>
/// The one idea worth holding on to: the fill duration is derived from tempo
/// and the tile count together, so a denser grid does <em>not</em> take
/// proportionally longer to fill. The wall always takes about
/// <c>tempo * 3</c> seconds to build, whether it is forty covers or four
/// hundred.
/// </para>
/// </remarks>
public static class WallCycle
{
    /// <summary>Seconds a tile takes to fade in, and to grow into its cell.</summary>
    public const double FadeIn = 0.6;

    /// <summary>
    /// How many tile positions the dissolve front covers while any one tile
    /// fades out. Three, so three tiles are always mid-dissolve: one alone
    /// reads as a cursor deleting text, and a dozen reads as the whole wall
    /// simply switching off.
    /// </summary>
    private const double DissolveSpread = 3.0;

    /// <summary>The dissolve is compressed into this fraction of the fill.</summary>
    private const double DissolveFraction = 0.6;

    /// <summary>
    /// Seconds between one tile arriving and the next.
    /// </summary>
    /// <remarks>
    /// The 0.02 floor caps the fill at fifty tiles a second, which matters on a
    /// very dense grid where the unfloored value would be small enough that
    /// several tiles land in the same frame and the wall appears to snap on.
    /// </remarks>
    public static double Step(double tempo, int tileCount, double buildSpeed) =>
        Math.Max(0.02, tempo / Math.Max(tileCount, 1) * 3.0 / Math.Max(0.15, buildSpeed));

    public static double FillTime(double step, int tileCount) => tileCount * step;

    public static double HoldTime(double wallHold) => Math.Max(1.0, wallHold);

    public static double CycleTime(double fillTime, double holdTime) =>
        fillTime + holdTime + (fillTime * DissolveFraction);

    /// <summary>
    /// How visible one tile is, given how far into the cycle we are.
    /// </summary>
    /// <param name="elapsed">Seconds since the cycle began.</param>
    /// <param name="appearAt">This tile's offset within the fill, in seconds.</param>
    /// <param name="step">The gap between arrivals, from <see cref="Step"/>.</param>
    /// <param name="tileCount">How many tiles the wall has.</param>
    /// <param name="fillTime">From <see cref="FillTime"/>.</param>
    /// <param name="holdTime">From <see cref="HoldTime"/>.</param>
    /// <remarks>
    /// The dissolve takes the <em>lower</em> of the two alphas, never the newer
    /// one. A tile still fading in when the dissolve front arrives keeps
    /// dimming; it never brightens back up, which would read as a flicker.
    /// </remarks>
    public static float AlphaAt(
        double elapsed, double appearAt, double step, int tileCount, double fillTime, double holdTime)
    {
        var alpha = 0f;
        if (elapsed >= appearAt) alpha = (float)Math.Min(1.0, (elapsed - appearAt) / FadeIn);

        var dissolveStart = fillTime + holdTime;
        if (elapsed > dissolveStart)
        {
            // How far the dissolve front has swept past this tile, measured in
            // tile positions. appearAt / step recovers the tile's place in the
            // shuffled arrival order, so tiles leave in the order they arrived.
            var out01 = (elapsed - dissolveStart) / Math.Max(0.0001, fillTime * DissolveFraction);
            var mine = (out01 * tileCount) - (appearAt / Math.Max(0.0001, step));
            var fading = (float)Math.Max(0.0, 1.0 - (mine / DissolveSpread));
            alpha = Math.Min(alpha, fading);
        }

        return alpha;
    }

    /// <summary>
    /// How much of its cell a tile fills, on the same clock as the fade-in: it
    /// lands at 94% and settles to 100%.
    /// </summary>
    public static float GrowAt(double elapsed, double appearAt)
    {
        if (elapsed < appearAt) return 0.94f;
        return 0.94f + (0.06f * (float)Math.Min(1.0, (elapsed - appearAt) / FadeIn));
    }
}

namespace AlbumCoverScreenSaver.Shared;

/// <summary>One rung of the grid rushing toward the viewer.</summary>
public readonly record struct Rung(float DownFromHorizon, float Alpha);

/// <summary>
/// The perspective grid of Vaporwave Grid, and the slots across its sun.
/// </summary>
/// <remarks>
/// <para>
/// The grid runs at exactly one rung per second, and that is not an
/// approximation. It works by taking the fractional part of the clock, so after
/// one second every rung has moved into the place the one behind it occupied and
/// the pattern is seamless.
/// </para>
/// <para>
/// Drive it from an accumulating delta instead and the wrap drifts, which shows
/// as a visible hitch once a second forever. This is the one piece of the style
/// that has to be exact.
/// </para>
/// </remarks>
public static class VaporwaveGrid
{
    /// <summary>Rungs between the horizon and the bottom of the screen.</summary>
    public const int Rungs = 22;

    /// <summary>Lines fanning out from the vanishing point, either side of centre.</summary>
    public const int Rays = 14;

    public const float HorizonFraction = 0.46f;

    /// <summary>Where the grid is in its one second cycle, 0 to 1.</summary>
    public static float Cycle(double phase) => (float)(phase - Math.Floor(phase));

    /// <summary>
    /// Rung <paramref name="k"/>, as a fraction of the distance from the horizon
    /// to the bottom of the screen.
    /// </summary>
    /// <remarks>
    /// The square is what makes it a perspective grid rather than a ladder:
    /// rungs bunch up at the horizon and spread out as they arrive, exactly as
    /// road markings do coming toward you.
    /// </remarks>
    public static Rung RungAt(int k, float cycle)
    {
        var t = (k + cycle) / Rungs;

        return new Rung(
            DownFromHorizon: t * t,
            Alpha: Math.Max(0f, 0.75f - (t * 0.6f)));
    }

    /// <summary>
    /// Where a ray crosses the horizon and where it leaves the bottom edge, as
    /// fractions of the width either side of centre.
    /// </summary>
    /// <remarks>
    /// A constant fifteen to one, so the whole fan is one straight-line
    /// construction. Only the middle handful reach the bottom of the screen; the
    /// rest have run off the sides long before, which is what gives the ground
    /// its width.
    /// </remarks>
    public static (float AtHorizon, float AtBottom) Ray(int i) => (i * 0.02f, i * 0.30f);

    /// <summary>
    /// The bands cut across the sun, from just below its middle downward.
    /// </summary>
    /// <remarks>
    /// Each band is a quarter wider than the last and the step between them is
    /// wider again, so the sun does not simply get striped: it dissolves toward
    /// its base. That widening is the signature of the whole genre, and a fixed
    /// band width loses it entirely.
    /// </remarks>
    public static IEnumerable<(float Down, float Band)> SunSlots(float radius)
    {
        var down = radius * 0.10f;
        var band = 2f;

        while (down < radius)
        {
            yield return (down, band);

            down += band * 2.4f;
            band *= 1.25f;
        }
    }
}

namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// The wind that Drifting Float's covers travel on, and how each of them turns
/// when it changes.
/// </summary>
/// <remarks>
/// <para>
/// Every cover moves along one shared direction with a degree or two of its own
/// variation. Every half minute or so the wind swings somewhere genuinely
/// different, and the covers already on screen lean round to it one at a time
/// rather than all snapping together.
/// </para>
/// <para>
/// All of this is angles and interpolation, so it is here where it can be
/// tested. The two pieces that are easy to get wrong and expensive to spot by
/// eye are <see cref="Lerp"/> taking the short way round, and the stagger in
/// <see cref="TurnOf"/> leaving the slowest cover fractionally short when the
/// window closes.
/// </para>
/// </remarks>
public static class Wind
{
    public const double Tau = Math.PI * 2.0;

    /// <summary>Seconds a change of direction takes to work through the flock.</summary>
    public const double TurnSeconds = 5.0;

    /// <summary>The wind holds a direction for somewhere in this range.</summary>
    public const double HoldMinimum = 25.0;

    public const double HoldMaximum = 50.0;

    /// <summary>
    /// The smallest change worth making, in radians. About 34 degrees: below
    /// that the turn reads as drift rather than as a change of weather.
    /// </summary>
    private const double MinimumChange = 0.6;

    /// <summary>The angle between two directions, from 0 to pi.</summary>
    public static double Distance(double a, double b)
    {
        var d = Math.Abs(a - b) % Tau;
        return d > Math.PI ? Tau - d : d;
    }

    /// <summary>
    /// Interpolates between two angles the short way round.
    /// </summary>
    /// <remarks>
    /// Without this a turn from 350 degrees to 10 sweeps three quarters of a
    /// circle backwards, which on screen is the whole flock wheeling the wrong
    /// way for five seconds.
    /// </remarks>
    public static double Lerp(double from, double to, double t)
    {
        var d = (to - from) % Tau;
        if (d > Math.PI) d -= Tau;
        if (d < -Math.PI) d += Tau;
        return from + (d * t);
    }

    /// <summary>
    /// Picks a new direction that is meaningfully different from the current
    /// one, giving up after twelve tries.
    /// </summary>
    /// <remarks>
    /// Giving up matters: insisting would loop, and a turn that happens to be
    /// small is a far better outcome than a saver that stops drawing. A full
    /// reversal is allowed and is the most striking of the lot.
    /// </remarks>
    public static double PickNewDirection(Random random, double current)
    {
        var next = current;

        for (var attempt = 0; attempt < 12; attempt++)
        {
            next = random.NextDouble() * Tau;
            if (Distance(next, current) > MinimumChange) break;
        }

        return next;
    }

    /// <summary>How far through its own turn one cover is, and how much it slows.</summary>
    /// <param name="u">How far through the five second window, 0 to 1.</param>
    /// <param name="agility">
    /// 0 is a small distant cover, shoved around by the change: it starts late,
    /// takes longer, and comes to a complete stop in the middle of the turn.
    /// 1 is a large near one, heavy enough to lean into the new direction and
    /// carry on at eighty per cent of its pace.
    /// </param>
    /// <returns>
    /// <c>Local</c> is its own progress through the turn, for feeding to the
    /// easing curve; <c>Pace</c> multiplies its speed.
    /// </returns>
    /// <remarks>
    /// The least agile cover starts at u = 0.30 and spans 0.80, so it would
    /// finish at u = 1.10, past the end of the window. At u = 1 it has reached
    /// about 99.2% of its turn and the remainder is assigned outright. That
    /// residual snap is under one per cent of the angle and is invisible; it is
    /// described here so nobody "fixes" it and changes every other timing in the
    /// process.
    ///
    /// Pace dips on a raised cosine rather than a step, so there is no moment of
    /// a cover being parked: the approach to a standstill and the departure from
    /// it are both smooth.
    /// </remarks>
    public static (float Local, double Pace) TurnOf(double u, double agility)
    {
        var begin = (1.0 - agility) * 0.30;
        var span = 0.50 + (0.30 * (1.0 - agility));
        var local = Ease.Clamp((float)((u - begin) / span));

        var stop = 0.18 + (0.82 * (1.0 - agility));
        var bump = 0.5 - (0.5 * Math.Cos(Tau * local));

        return (local, 1.0 - (stop * bump));
    }

    /// <summary>
    /// Where a cover enters the screen from, given the direction the wind is
    /// blowing and how big the cover is.
    /// </summary>
    /// <remarks>
    /// Step back from the centre of the screen by the half diagonal plus the
    /// cover's own side, which puts it fully off screen at <em>any</em> angle,
    /// then slide along the perpendicular. Spawning on a named edge only works
    /// while the wind blows one way; this covers every entry the screen has,
    /// corners included, with no special cases.
    ///
    /// The line is a full half diagonal wide, so some spawns never cross the
    /// visible area at all and are recycled straight away. That is expected.
    /// </remarks>
    public static (float X, float Y) SpawnUpwind(
        float width, float height, double windAngle, float side, double alongFraction)
    {
        var half = Math.Sqrt((width * width) + (height * height)) / 2.0;
        var reach = half + side;
        var along = alongFraction * half;

        var x = (width / 2.0) - (Math.Cos(windAngle) * reach) - (Math.Sin(windAngle) * along);
        var y = (height / 2.0) - (Math.Sin(windAngle) * reach) + (Math.Cos(windAngle) * along);

        return ((float)x, (float)y);
    }
}

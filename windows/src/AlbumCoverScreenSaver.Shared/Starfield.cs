namespace AlbumCoverScreenSaver.Shared;

/// <summary>Where one star sits, and how near it is.</summary>
public readonly record struct Star(double X01, double Y01, double Depth);

/// <summary>Where one orbiting cover is, relative to the sun.</summary>
public readonly record struct Orbiter(float OffsetX, float OffsetUp, float Size, float Depth);

/// <summary>
/// The sky of Starfield Orbit: a field of stars that does not crawl, and covers
/// circling a sun.
/// </summary>
/// <remarks>
/// The stars are not stored. They are computed from their own index every frame
/// by a hash, which is the standard trick from shader code: take the sine of a
/// large multiple of the index, throw away everything but the fractional part,
/// and what comes out is evenly spread and completely repeatable. Two hundred
/// and twenty stars for no memory at all, in exactly the same places every time.
/// </remarks>
public static class Starfield
{
    public const int Count = 220;

    /// <summary>
    /// The vertical squash that turns a circle into an orbit seen nearly
    /// edge-on. Without it the covers go round in a hoop rather than on a plane.
    /// </summary>
    public const float OrbitTilt = 0.34f;

    private static double Fraction(double value) => value - Math.Floor(value);

    /// <summary>
    /// Star <paramref name="index"/>, as fractions of the screen.
    /// </summary>
    /// <remarks>
    /// <b>Every step of this is in double, and that is load-bearing.</b> The hash
    /// works by keeping only the part of a very large number that floating point
    /// can barely hold, so it is maximally sensitive to precision. Done in
    /// single precision it still produces a perfectly good star field, just a
    /// visibly different one from the Mac's. It would never look like a bug, and
    /// it would never be found.
    /// </remarks>
    public static Star At(int index)
    {
        var x = Fraction(Math.Sin(index * 12.9898) * 43758.5453);
        var y = Fraction(Math.Sin(index * 78.233) * 12345.6789);
        var depth = Fraction(Math.Sin(index * 39.77) * 5647.31);

        return new Star(x, y, depth);
    }

    /// <summary>How wide a star is drawn, in points.</summary>
    public static float SizeOf(double depth) => (float)(0.5 + (depth * 1.6));

    /// <summary>How bright it is.</summary>
    public static float AlphaOf(double depth) => (float)(0.15 + (depth * 0.55));

    /// <summary>
    /// How far a star has drifted sideways by now.
    /// </summary>
    /// <remarks>
    /// Near stars run six times faster than far ones. That parallax is the only
    /// thing giving the field any depth, and it is also what stops the stars and
    /// the orbiting covers from looking like they belong to the same flat plane.
    /// </remarks>
    public static double DriftOf(double phase, double depth) => phase * (2.0 + (depth * 10.0));

    public static int OrbiterCount(int setting, bool isPreview) =>
        isPreview ? 5 : Math.Clamp(setting, 3, 40);

    /// <summary>
    /// Radians per unit of time for an orbit at a given radius.
    /// </summary>
    /// <remarks>
    /// Inner orbits are faster, as they are in the sky. The coin flip is what
    /// stops two covers that happen to share a radius from staying locked
    /// together forever.
    /// </remarks>
    public static double SpeedAt(double radius, bool slower) =>
        0.30 / (radius + 0.18) * (slower ? 0.85 : 1.0);

    /// <summary>
    /// Where a cover is on its orbit, and how near it is.
    /// </summary>
    /// <remarks>
    /// <paramref name="OffsetUp"/> is positive away from the viewer, in the
    /// specification's y-up space. Depth runs 1 at the far side of the orbit to
    /// 0 at the near side, and drives both the size and the shading, so a cover
    /// passing behind the sun is smaller and darker than the same cover passing
    /// in front of it.
    /// </remarks>
    public static Orbiter PlaceOrbiter(double angle, float shortestSide, float radius, float size)
    {
        var depth = (float)((Math.Sin(angle) + 1.0) / 2.0);

        return new Orbiter(
            OffsetX: (float)(Math.Cos(angle) * shortestSide * radius),
            OffsetUp: (float)(Math.Sin(angle) * shortestSide * radius * OrbitTilt),
            Size: shortestSide * size * (1.25f - (depth * 0.5f)),
            Depth: depth);
    }

    /// <summary>True when this cover is on the far side and is drawn first.</summary>
    public static bool IsBehind(double angle) => Math.Sin(angle) >= 0;
}

namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// The hash several styles use to get repeatable randomness out of nothing.
/// </summary>
/// <remarks>
/// <para>
/// Take the sine of a large multiple of a number, throw away everything but the
/// fractional part, and what comes out is evenly spread and identical every
/// time. It is the standard idiom from shader code, and it is here because three
/// styles want it: the stars, the subway tiles, and the greeked text on the
/// newspaper.
/// </para>
/// <para>
/// <b>Double throughout, and that is load-bearing.</b> It works by keeping only
/// the part of a very large number that floating point can barely hold, so it is
/// maximally sensitive to precision. In single precision it still produces
/// perfectly good randomness, just different randomness from the Mac's, and
/// nobody would ever notice by looking.
/// </para>
/// </remarks>
public static class Noise
{
    public static double Fraction(double value) => value - Math.Floor(value);

    /// <summary>A repeatable number from 0 to 1 for any seed.</summary>
    public static double Hash01(double seed) => Fraction(Math.Sin(seed * 12.9898) * 43758.5453);
}

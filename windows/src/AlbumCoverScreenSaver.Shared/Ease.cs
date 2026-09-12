namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// The easing curves. Linear transitions feel robotic, so everything that
/// changes goes through one of these.
/// </summary>
/// <remarks>
/// In the shared library rather than the saver because it is pure arithmetic
/// with nothing platform-specific about it, which means it can be tested
/// anywhere. All three clamp their input first.
/// </remarks>
public static class Ease
{
    public static float Clamp(float t) => Math.Min(1f, Math.Max(0f, t));

    /// <summary>Cubic ease-in-out. The default for anything that starts and stops.</summary>
    public static float InOut(float t)
    {
        var x = Clamp(t);
        return x < 0.5f
            ? 4f * x * x * x
            : 1f - (MathF.Pow((-2f * x) + 2f, 3f) / 2f);
    }

    /// <summary>Cubic ease-out. Quick off the mark, long settle.</summary>
    public static float Out(float t)
    {
        var x = Clamp(t);
        return 1f - MathF.Pow(1f - x, 3f);
    }

    /// <summary>
    /// Overshoots by about 10% near x = 0.7 and settles. For things that land
    /// on something, like the polaroid pin-in.
    /// </summary>
    public static float Back(float t)
    {
        var x = Clamp(t);
        const float c1 = 1.55f;
        const float c3 = c1 + 1f;
        return 1f + (c3 * MathF.Pow(x - 1f, 3f)) + (c1 * MathF.Pow(x - 1f, 2f));
    }
}

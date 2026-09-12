namespace AlbumCoverScreenSaver.Shared;

/// <summary>One stroke of the neon routine: how wide, how bright, and whether it is the core.</summary>
public readonly record struct NeonPass(float Width, float Alpha, bool Core);

/// <summary>One bubble in a tube.</summary>
/// <remarks>
/// <paramref name="Up"/> is a fraction of the tube's length, <paramref name="Across"/>
/// a fraction of its width, and <paramref name="Size"/> a fraction of its width too.
/// Nothing here is in points, so a bubble survives a resolution change.
/// </remarks>
public readonly record struct Bubble(
    int Tube, float Up, float Across, float Size, float Speed, int Tint);

/// <summary>
/// Neon Jukebox: a Wurlitzer-style cabinet with an arch of neon, bubble tubes
/// down each shoulder and a list of selection strips under the display.
/// </summary>
public static class Jukebox
{
    public const int Bubbles = 44;

    /// <summary>Strips of card under the display window.</summary>
    public const int Strips = 5;

    /// <summary>
    /// The glow, as five strokes of the same path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wide and faint first, narrow and bright last. The last pass is
    /// <b>white, not the tube's colour</b>, and that white-hot core inside a
    /// coloured halo is the whole of why it reads as neon rather than as a fat
    /// coloured line.
    /// </para>
    /// <para>
    /// It is also the cheapest convincing neon there is in two dimensions. A
    /// real bloom would cost a filter pass per frame and look no better.
    /// </para>
    /// </remarks>
    public static readonly NeonPass[] Passes =
    [
        new(5.5f, 0.05f, false),
        new(3.2f, 0.10f, false),
        new(1.9f, 0.20f, false),
        new(1.0f, 0.85f, false),
        new(0.38f, 0.80f, true),
    ];

    public static float CabinetWidth(float width, float height) =>
        Math.Min(width * 0.62f, height * 1.05f);

    public static float ShoulderY(float height) => height * 0.50f;

    /// <summary>
    /// The slow breath of the neon, between a half and full brightness.
    /// </summary>
    /// <remarks>
    /// Period a little under five seconds. Anything faster stops reading as a
    /// gas tube warming and cooling and starts reading as a fault.
    /// </remarks>
    public static float Pulse(double phase) => 0.75f + (0.25f * MathF.Sin((float)phase * 1.3f));

    /// <summary>
    /// The inner tube's pulse, which runs against the outer one.
    /// </summary>
    /// <remarks>
    /// It ranges 0.75 to 1.25, so it is allowed past full brightness. Two tubes
    /// breathing in opposite phase is what stops the arch reading as one
    /// throbbing blob.
    /// </remarks>
    public static float CounterPulse(float pulse) => 1.75f - pulse;

    public static float TubeCentre(float cabinetLeft, float cabinetWidth, int tube) =>
        cabinetLeft + (cabinetWidth * (tube == 0 ? 0.075f : 0.925f));

    public static float TubeWidth(float cabinetWidth) => cabinetWidth * 0.045f;

    public static float DisplaySide(float cabinetWidth, float height) =>
        Math.Min(cabinetWidth * 0.44f, height * 0.30f);

    /// <summary>A bubble at the bottom of its tube, or wherever it starts life.</summary>
    public static Bubble Spawn(Random random, int tube, float up)
    {
        return new Bubble(
            Tube: tube,
            Up: up,
            Across: (float)((random.NextDouble() * 1.2) - 0.6),
            Size: (float)(0.18 + (random.NextDouble() * 0.32)),
            Speed: (float)(0.05 + (random.NextDouble() * 0.11)),
            Tint: random.Next(0, 6));
    }

    /// <summary>
    /// One frame of one bubble.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sideways wobble is seeded from the bubble's own place in the list, so
    /// each has its own phase and they drift out of step the way real ones do.
    /// All of them on the same sine would look like a wave rather than a tube.
    /// </para>
    /// <para>
    /// A bubble reaching the top is not moved back to the bottom unchanged: it
    /// is a new bubble, of a new size and colour, because a repeating cast of
    /// forty four is something the eye eventually picks up on.
    /// </para>
    /// </remarks>
    public static Bubble Step(Bubble bubble, int index, double phase, double dt, Random random)
    {
        var up = bubble.Up + (float)(bubble.Speed * dt);
        var across = bubble.Across + (float)(Math.Sin((phase * 2.0) + index) * dt * 0.10);

        if (up > 1f) return Spawn(random, bubble.Tube, 0f);

        return bubble with { Up = up, Across = across };
    }

    /// <summary>How far down the screen the strips may reach.</summary>
    public const float StripFoot = 0.97f;

    /// <summary>The height the strips want, as a fraction of the screen.</summary>
    public const float StripHeight = 0.042f;

    /// <summary>The smallest a strip may be squeezed to and still be read.</summary>
    public const float StripSmallest = 0.028f;

    /// <summary>
    /// How many strips fit under the display window, and how tall each one is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A deliberate departure from the specification</b>, which gives the
    /// strips a fixed height and stacks five of them downward from a fixed
    /// point. The window sits inside the arch and the arch is half the height of
    /// the screen, so on an ordinary screen the fifth strip is off the bottom
    /// edge and the fourth is half off it. The same fault as Newsstand's
    /// photograph and the CD player's credits, in a third place.
    /// </para>
    /// <para>
    /// The strips are squeezed to fit first, because five slightly shorter
    /// strips still read as a list of five records. Only when they would be too
    /// short to read does the list get shorter instead, which is the honest
    /// failure: fewer records legibly listed beats five illegibly.
    /// </para>
    /// </remarks>
    public static (int Count, float Height) StripRows(float height, float top)
    {
        var room = Math.Max(0f, (height * StripFoot) - top);
        var rowHeight = Math.Min(height * StripHeight, room / Strips);

        if (rowHeight >= height * StripSmallest) return (Strips, rowHeight);

        var smallest = height * StripSmallest;
        return (Math.Clamp((int)Math.Floor(room / smallest), 0, Strips), smallest);
    }

    /// <summary>
    /// Only the newest album can be the one playing.
    /// </summary>
    /// <remarks>
    /// The archive is newest first, so the live album, if it is in the archive
    /// at all, is at the top of it. A strip further down showing NOW PLAYING
    /// would be claiming something that cannot be true.
    /// </remarks>
    public static bool IsNowPlaying(int strip, int? live, int featured) =>
        strip == 0 && live is not null && live == featured;
}

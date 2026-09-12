namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// A nineties portable CD player seen from directly above, lid open, jewel cases
/// strewn around it.
/// </summary>
/// <remarks>
/// Every measurement is a multiple of one radius, so the whole object scales as
/// a unit and the proportions hold from a laptop screen to a wall.
/// </remarks>
public static class CdPlayer
{
    /// <summary>Radians a second the disc turns, which is about eleven rpm.</summary>
    /// <remarks>
    /// <b>A free clock.</b> The Record Player's platter runs at a speed the
    /// settings choose, and the Cassette Deck's reels are driven by the position
    /// within the track. This one is neither: it simply turns, whether or not
    /// anything is playing, because a CD spinning down when the music stops
    /// would read as the saver having crashed.
    /// </remarks>
    public const double SpinRate = 1.15;

    /// <summary>Arcs in the diffraction ring.</summary>
    public const int Arcs = 64;

    /// <summary>
    /// How far each arc sweeps, in degrees.
    /// </summary>
    /// <remarks>
    /// The arcs are spaced 5.625 degrees apart and each sweeps six, so they
    /// overlap. That overlap is the whole trick: sixty four thin bands at eight
    /// and a half per cent opacity compose into one continuous wheel of hue
    /// rather than into sixty four visible stripes.
    /// </remarks>
    public const float ArcSweep = 6f;

    public const float ArcAlpha = 0.085f;

    public static float Radius(float width, float height) =>
        Math.Min(height * 0.32f, width * 0.23f);

    public static (float X, float Y) Centre(float width, float height) =>
        (width * 0.5f, height * 0.53f);

    public static float BodySide(float radius) => radius * 2.84f;

    public static float BodyCorner(float radius) => radius * 0.30f;

    public static float DiscWell(float radius) => radius * 1.14f;

    public static float LidRim(float radius) => radius * 1.16f;

    public static float RimWidth(float radius) => radius * 0.05f;

    /// <summary>The printed label in the middle of the disc.</summary>
    public static float LabelRadius(float radius) => radius * 0.46f;

    /// <summary>
    /// How far the disc has turned, in radians, clockwise on screen.
    /// </summary>
    /// <remarks>
    /// The specification states this as a negative angle because it is written
    /// in the Mac's y-up space, where a negative angle is clockwise. Windows is
    /// y-down and the sign flips, so this is positive and means the same thing.
    /// Reading the minus sign across without thinking would spin the disc
    /// backwards, which is one of those faults nobody can name but everybody
    /// notices.
    /// </remarks>
    public static float Spin(double phase) => (float)(phase * SpinRate);

    /// <summary>Where one diffraction arc starts, in degrees.</summary>
    public static float ArcStart(int index) => index * 360f / Arcs;

    /// <summary>Its hue, as a fraction of the wheel.</summary>
    public static float ArcHue(int index) => index / (float)Arcs;

    /// <summary>The radius the arcs are drawn at, before the stroke widens them.</summary>
    public static float ArcRadius(float radius) => radius * 0.74f;

    /// <summary>
    /// The stroke width, which is what turns a thin arc into a broad band.
    /// </summary>
    /// <remarks>
    /// Stroked at nearly half a radius, the arc at 0.74R covers roughly 0.52R to
    /// 0.96R. It is a band, drawn as a stroke rather than as a filled wedge, and
    /// reproducing it as a wedge gives a visibly different edge.
    /// </remarks>
    public static float ArcWidth(float radius) => radius * 0.44f;

    /// <summary>
    /// The three rings at the middle: clear plastic, then the hub, then the hole.
    /// </summary>
    /// <remarks>
    /// This is the giveaway that the object is a compact disc and not a record.
    /// A record has a paper label and a small spindle hole; a CD has a wide band
    /// of clear plastic around a much bigger one.
    /// </remarks>
    public static (float Clear, float Hub, float Hole) Middle(float radius) =>
        (radius * 0.19f, radius * 0.145f, radius * 0.085f);

    /// <summary>Where the jewel cases may not land, so the player stays clear.</summary>
    public static (float X, float Y, float Radius) KeepOut(float width, float height) =>
        (width * 0.5f, height * 0.52f, Math.Min(height * 0.36f, width * 0.26f) * 1.35f);

    /// <summary>The smallest and largest a jewel case may be, as fractions of the height.</summary>
    public const float CaseSmallest = 0.14f;
    public const float CaseLargest = 0.22f;

    /// <summary>The gap the specification asks for between the body and the credits.</summary>
    public const float CreditsGap = 0.030f;

    /// <summary>How far down the screen the credits may reach.</summary>
    public const float CreditsFoot = 0.970f;

    /// <summary>
    /// Where the title and artist go, under the player.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A deliberate departure from the specification</b>, which puts them a
    /// flat three per cent of the height below the bottom edge of the body and
    /// leaves it there. The body is 2.84 radii square and the radius is up to
    /// 0.32 of the height, so on an ordinary sixteen by nine screen the player
    /// is ninety one per cent of the height and its bottom edge is already
    /// within three per cent of the floor. The credits land off the screen
    /// entirely, and they do on a sixteen by ten one too.
    /// </para>
    /// <para>
    /// So the gap is what the specification asks for when there is room for it,
    /// and otherwise the block is pushed up until it fits, which puts the words
    /// over the lower edge of the body. They are drawn with a shadow and the
    /// body there is bare metal, so they read perfectly well on it.
    /// </para>
    /// </remarks>
    public static float CreditsTop(float height, float bodyBottom, float blockHeight) =>
        Math.Max(
            height * 0.60f,
            Math.Min(
                bodyBottom + (height * CreditsGap),
                (height * CreditsFoot) - blockHeight));
}

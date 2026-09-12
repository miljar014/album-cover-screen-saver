namespace AlbumCoverScreenSaver.Shared;

/// <summary>How one card of the carousel is projected.</summary>
public readonly record struct FlowCard(
    float Squash, float Scale, float Alpha, float OffsetX, float Shear, float Haze);

/// <summary>
/// The perspective of Cover Flow, which is a 3D carousel faked entirely in 2D.
/// </summary>
/// <remarks>
/// <para>
/// There is no 3D here at all: no scene graph, no z-buffer, no camera. Each card
/// gets four numbers derived from how far it is from the centre, and they add up
/// to something the eye reads as a row of records turned edge-on. The four are a
/// horizontal squash, a size, a shear, and an opacity.
/// </para>
/// <para>
/// The squash is honest trigonometry: it is the cosine of the angle a card
/// would be turned through if it really were rotating about its own vertical
/// edge. The size is an honest perspective divide. The shear is the dishonest
/// one, and it is the cue that actually sells it: a card to the right has its
/// right edge pushed down and its left edge pushed up, as though you were
/// looking at it from slightly above and to one side.
/// </para>
/// </remarks>
public static class CoverFlowMath
{
    /// <summary>
    /// How many cards either side of centre are drawn.
    /// </summary>
    /// <remarks>
    /// <b>The renderer has to honour this itself.</b> Every term below is capped
    /// at this distance, opacity included, so a card five slots out is a fifth
    /// opaque and stays exactly that however far away it really is. Nothing here
    /// ever returns zero. Hand the projection a whole forty album ring and it
    /// will happily describe forty covers stacked on the horizon.
    /// </remarks>
    public const int Visible = 5;

    /// <summary>
    /// The maximum turn, in radians. About 72 degrees, which is as far as a
    /// cover can go before it stops reading as a cover.
    /// </summary>
    private const float MaximumTurn = 1.25f;

    public static double Dwell(double featureSeconds) => Math.Max(2.5, featureSeconds / 8.0);

    public static int Count(int flowCount) => Math.Clamp(flowCount, 5, 60);

    /// <summary>
    /// The projection for a card <paramref name="offset"/> slots from centre.
    /// </summary>
    /// <param name="offset">
    /// Signed and fractional: zero is dead centre, and it slides continuously as
    /// the carousel turns.
    /// </param>
    public static FlowCard Project(float offset)
    {
        var distance = Math.Min(Math.Abs(offset), Visible);
        var direction = offset < 0 ? -1f : 1f;

        // cos of the angle the card would be turned through. The floor never
        // actually binds, since cos(1.25) is already 0.315.
        var squash = Math.Max(0.16f, MathF.Cos(Math.Min(MaximumTurn, distance * 0.52f)));

        // A 1/z divide, with z growing linearly away from the centre.
        var scale = 1f / (1f + (distance * 0.17f));

        var alpha = Math.Max(0f, 1f - (distance * 0.16f));

        // Vertical shear. The sign flips either side of centre, which is what
        // makes the two halves look like they are facing each other.
        var shear = -direction * 0.02f * distance;

        // Depth haze, reaching its ceiling about four cards out.
        var haze = Math.Min(0.55f, distance * 0.13f);

        return new FlowCard(squash, scale, alpha, OffsetOf(offset, distance, direction), shear, haze);
    }

    /// <summary>
    /// How far from centre a card sits, in multiples of the card's own side.
    /// </summary>
    /// <remarks>
    /// <b>This is discontinuous at one slot out, and deliberately so.</b> As a
    /// card slides toward the first side position its offset is computed one
    /// way, and the moment it arrives it is computed another, and the two do not
    /// meet: there is a jump of about a third of a card. Mid-slide the first
    /// side card can land almost on top of the second.
    ///
    /// That is what the macOS build does, and matching it frame for frame is
    /// worth more than smoothing it, because the alternative is a carousel that
    /// moves differently on the two platforms for no stated reason. The smooth
    /// version is one line and is written out in the specification if it is ever
    /// wanted.
    /// </remarks>
    private static float OffsetOf(float offset, float distance, float direction)
    {
        if (distance == 0f) return 0f;

        var far = (0.34f + ((distance - 1f) * 0.30f)) * (distance < 1f ? distance : 1f);
        var near = distance < 1f ? offset * 0.34f : 0f;

        return (direction * far) + near;
    }
}

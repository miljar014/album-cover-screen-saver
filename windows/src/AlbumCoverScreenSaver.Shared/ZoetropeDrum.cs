namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// The spinning drum of Zoetrope: covers standing inside it, and the slits in
/// its front wall you see them through.
/// </summary>
/// <remarks>
/// <para>
/// The drum is a circle seen from slightly above, squashed to a third of its
/// height. Everything else follows from the angle a card is at: where it sits on
/// the ellipse, how big it is, how dark it is, and whether it is the near wall
/// or the far one.
/// </para>
/// <para>
/// Note that the covers on the drum are the newest in the archive in order, and
/// the drum never turns to face anything. The record playing is on there because
/// the archive is newest-first, not because the style went looking for it.
/// </para>
/// </remarks>
public static class ZoetropeDrum
{
    /// <summary>A circle seen from slightly above rather than edge-on.</summary>
    public const float Squash = 0.30f;

    /// <summary>Seconds for one full revolution, whatever the card count.</summary>
    public const double Revolution = Math.PI * 2 / 0.45;

    public static int CardCount(int setting) => Math.Clamp(setting, 6, 30);

    /// <summary>
    /// How far round the drum has turned. Negative, so the near wall travels
    /// left to right, which is the direction a zoetrope is always drawn turning.
    /// </summary>
    public static double Spin(double phase) => -phase * 0.45;

    public static double AngleOf(int card, int count, double spin) =>
        spin + (card * Math.PI * 2 / count);

    /// <summary>Slits sit half a step round from the cards, between them.</summary>
    public static double SlitAngleOf(int slit, int count, double spin) =>
        spin + ((slit + 0.5) * Math.PI * 2 / count);

    /// <summary>1 at the back of the drum, 0 at the front.</summary>
    public static float DepthOf(double angle) => (float)((Math.Sin(angle) + 1.0) / 2.0);

    public static float CardWidth(float cardHeight, float depth) =>
        cardHeight * (1.15f - (depth * 0.42f));

    /// <summary>Black over a card, thickening toward the back of the drum.</summary>
    public static float ShadeOf(float depth) => depth * 0.60f;

    /// <summary>
    /// Whether a slit is on the near wall, and so drawn at all.
    /// </summary>
    /// <remarks>
    /// The tolerance is slightly positive rather than exactly zero, which stops
    /// a slit flickering in and out at the precise side of the drum where it is
    /// a hairline anyway.
    /// </remarks>
    public static bool IsNearWall(double angle) => Math.Sin(angle) < 0.1;

    /// <summary>
    /// How wide a slit looks, which is the foreshortening of a bar turning about
    /// the drum's axis.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Floored at two points, so a bar that is exactly edge-on is still drawn
    /// rather than blinking out for a frame.
    /// </para>
    /// <para>
    /// <b>Note which way round this is</b>, because it is the opposite of the
    /// first guess. Slits are widest at the left and right extremes of the drum
    /// and narrowest at the front centre. A slit cut into a curved wall would
    /// behave the other way, but one built as a radial fin standing out from the
    /// axis behaves exactly like this, and the specification is explicit about
    /// the formula. Followed as written; if the drum ever looks wrong at the
    /// front, this is the line to compare against the Mac.
    /// </para>
    /// </remarks>
    public static float SlitWidth(float radius, double angle) =>
        Math.Max(2f, radius * 0.055f * (float)Math.Abs(Math.Cos(angle)));
}

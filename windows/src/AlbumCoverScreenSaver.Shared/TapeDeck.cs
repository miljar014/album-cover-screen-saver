namespace AlbumCoverScreenSaver.Shared;

/// <summary>Where the tape is, and how far each reel has turned to put it there.</summary>
public readonly record struct Reels(
    float SupplyRadius, float TakeupRadius, float SupplyAngle, float TakeupAngle);

/// <summary>
/// Cassette Deck: two reels wound by the position within the track.
/// </summary>
/// <remarks>
/// <b>Nothing here runs on the animation clock while music is playing.</b> That
/// is the point of the style. When the music stops the reels stop, and when you
/// skip forward they jump, because they are showing where the tape is rather
/// than pretending to. A clock-driven spin would keep turning through a pause
/// and give the whole thing away in a second.
/// </remarks>
public static class TapeDeck
{
    /// <summary>How long a track is assumed to be when there is nothing playing.</summary>
    /// <remarks>
    /// Three and a half minutes, which is a song. It is deliberately not the
    /// featured-album timer: winding a whole cassette in the thirty seconds
    /// between album changes would look like a rewind, not a play.
    /// </remarks>
    public const double IdleCycle = 210.0;

    /// <summary>The full pack, as a fraction of the shell's height.</summary>
    public static float MaxRadius(float shellHeight) => shellHeight * 0.30f;

    /// <summary>The bare hub, as a fraction of the full pack.</summary>
    public static float HubRadius(float maxRadius) => maxRadius * 0.36f;

    /// <summary>
    /// Where the tape has got to, and how far each reel has turned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Wound tape takes up area, not radius.</b> The total is conserved as it
    /// moves from one hub to the other, so both radii go as square roots: the
    /// take-up pack grows quickly at first and then slows, and the supply pack
    /// does the reverse. That asymmetry is the whole tell that this is real
    /// geometry rather than two circles being slid between two sizes.
    /// </para>
    /// <para>
    /// The turns follow from it. Each wrap adds one thickness of tape to the
    /// radius, so the number of turns a pack has made is linear in how far its
    /// radius has moved, and scaling that by the track's own length keeps the
    /// deck running at much the same pace whether the song is ninety seconds or
    /// eight minutes. The emptying reel has the smaller radius and therefore
    /// visibly outruns the full one, with neither on a clock of its own.
    /// </para>
    /// </remarks>
    public static Reels Wind(float maxRadius, float hubRadius, double progress, double span)
    {
        var t = (float)Math.Clamp(progress, 0.0, 1.0);

        var ring = (maxRadius * maxRadius) - (hubRadius * hubRadius);

        var supply = MathF.Sqrt((maxRadius * maxRadius) - (t * ring));
        var takeup = MathF.Sqrt((hubRadius * hubRadius) + (t * ring));

        var perRadius = (float)(Math.PI * 2 * span * 0.15 / Math.Max(0.001f, maxRadius - hubRadius));

        return new Reels(
            SupplyRadius: supply,
            TakeupRadius: takeup,
            SupplyAngle: perRadius * (maxRadius - supply),
            TakeupAngle: perRadius * (takeup - hubRadius));
    }

    /// <summary>
    /// Which clock the reels run on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two things here are easy to get wrong and both matter. The live test is
    /// on the now-playing signal itself rather than on whether the track's album
    /// is in the archive, so the reels still follow a song whose album never got
    /// recorded and therefore has no featured index to match against.
    /// </para>
    /// <para>
    /// And with nothing playing the tape winds over its own two hundred and ten
    /// second cycle on the animation clock, so it keeps running across album
    /// changes instead of snapping back to the beginning every time the featured
    /// album rotates.
    /// </para>
    /// </remarks>
    public static (double Progress, double Span) Clock(
        bool live, double durationSeconds, double liveProgress, double phase)
    {
        if (live && durationSeconds > 0)
        {
            return (Math.Clamp(liveProgress, 0.0, 1.0), durationSeconds);
        }

        return (phase % IdleCycle / IdleCycle, IdleCycle);
    }

    /// <summary>
    /// Where the tape leaves a wound pack, which is a true tangent of it.
    /// </summary>
    /// <remarks>
    /// Both ends of the exposed run are tangents of the wound radius, so the
    /// span shifts by itself as one pack empties into the other. The tape
    /// follows the playback rather than being painted on at a fixed angle, and
    /// it is the difference between a deck and a picture of one.
    /// </remarks>
    public static (float X, float Y) Tangent(
        float fromX, float fromY, float centreX, float centreY, float radius, bool takeLeft)
    {
        var dx = fromX - centreX;
        var dy = fromY - centreY;

        var distance = Math.Max(radius + 0.001f, MathF.Sqrt((dx * dx) + (dy * dy)));

        var basis = MathF.Atan2(dy, dx);
        var offset = MathF.Acos(Math.Min(1f, radius / distance));

        var ax = centreX + (MathF.Cos(basis + offset) * radius);
        var ay = centreY + (MathF.Sin(basis + offset) * radius);
        var bx = centreX + (MathF.Cos(basis - offset) * radius);
        var by = centreY + (MathF.Sin(basis - offset) * radius);

        // Of the two solutions, the one on the outside of the shell, which is
        // the side real tape leaves from.
        var takeA = takeLeft ? ax <= bx : ax >= bx;

        return takeA ? (ax, ay) : (bx, by);
    }

    /// <summary>
    /// How far a transport key rides down.
    /// </summary>
    /// <remarks>
    /// Only the middle key, and only while something is actually playing. That
    /// one twelve per cent of a key's height is the whole of what makes the
    /// panel read as responding to the music rather than as a printed decal.
    /// </remarks>
    public static float KeyDrop(int key, bool running, float keyHeight) =>
        key == 1 && running ? keyHeight * 0.12f : 0f;

    /// <summary>True when the key should be drawn pushed in.</summary>
    public static bool IsPressed(int key, bool running) => key == 1 && running;
}

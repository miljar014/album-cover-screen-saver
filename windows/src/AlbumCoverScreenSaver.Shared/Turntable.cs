namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// Record Player: the platter, the arm that crosses the record in step with the
/// song, and the rule that hands the screen to another style when the music
/// stops.
/// </summary>
public static class Turntable
{
    /// <summary>A record change, lift to lower.</summary>
    public const double ChangeDuration = 1.9;

    /// <summary>
    /// How long the turntable is kept on screen after the music stops.
    /// </summary>
    /// <remarks>
    /// Skipping a track, or a short gap between songs, must not yank the whole
    /// visual away and drop it back. Twenty seconds covers both.
    /// </remarks>
    public const double Grace = 20.0;

    /// <summary>The shortest a record may be held when nothing is playing.</summary>
    public const double MinimumSecondsPerRecord = 20.0;

    /// <summary>
    /// The style actually drawn.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A turntable with nothing on it is a dead screen, so Record Player hands
    /// over to the style the user picked for it when the music has been off for
    /// the grace period.
    /// </para>
    /// <para>
    /// <paramref name="lastLiveAt"/> starts a long way in the past rather than
    /// at zero, so with nothing playing the saver opens straight into the
    /// fallback and never shows an idle deck at all.
    /// </para>
    /// </remarks>
    public static CollageMode ActiveMode(
        CollageMode wanted, double phase, double lastLiveAt, CollageMode fallback)
    {
        if (wanted != CollageMode.Vinyl) return wanted;

        return phase - lastLiveAt < Grace ? CollageMode.Vinyl : fallback;
    }

    /// <summary>Radians the platter turns in one frame, clockwise on screen.</summary>
    /// <remarks>
    /// The default is nine revolutions a minute rather than thirty three and a
    /// third. Filling a whole screen, a real speed reads as frantic; this reads
    /// as hypnotic, which is what a screen saver is for.
    /// </remarks>
    public static double SpinStep(double rpm, double dt) =>
        dt * (Math.Max(0.5, rpm) / 60.0) * Math.PI * 2;

    /// <summary>
    /// Where the stylus sits, measured from the middle of the record.
    /// </summary>
    /// <remarks>
    /// From the lead-in groove at 0.94 of the radius to the run-out at 0.50 of
    /// it, which is a modest band. A real arm does not travel from the rim to
    /// the spindle, and overstating the range makes it look like it is racing.
    /// </remarks>
    public static float StylusRadius(float radius, float playProgress) =>
        radius * (0.94f - (0.44f * Math.Clamp(playProgress, 0f, 1f)));

    public static float ParkedRadius(float radius) => radius * 1.24f;

    public static float TipRadius(float radius, float playProgress, float armLift)
    {
        var playing = StylusRadius(radius, playProgress);
        var parked = ParkedRadius(radius);

        return playing + ((parked - playing) * armLift);
    }

    /// <summary>
    /// The angle the arm has to sit at for its tip to land on the record.
    /// </summary>
    /// <remarks>
    /// The law of cosines, because the tip has two constraints at once: it is a
    /// fixed arm's length from the pivot, and it has to be a given distance from
    /// the middle of the platter. The angle is measured in the caller's own
    /// coordinate space, so a y-down caller gets a y-down answer and nothing
    /// needs converting.
    /// </remarks>
    public static double ArmAngle(
        float pivotX, float pivotY, float centreX, float centreY,
        float armLength, float tipRadius)
    {
        var dx = centreX - pivotX;
        var dy = centreY - pivotY;

        var distance = Math.Max(0.001, Math.Sqrt((dx * dx) + (dy * dy)));

        var cos = ((armLength * armLength) + (distance * distance) - (tipRadius * tipRadius))
                  / (2 * armLength * distance);

        return Math.Atan2(dy, dx) - Math.Acos(Math.Clamp(cos, -1.0, 1.0));
    }

    /// <summary>
    /// How far the arm is lifted during a change: up, hold, down.
    /// </summary>
    /// <remarks>
    /// The pace of a real changer. Eased at both ends, because a linear lift
    /// looks like a robot arm rather than a counterweighted one.
    /// </remarks>
    public static float ArmLift(double t)
    {
        if (t < 0) return 0f;
        if (t < 0.5) return Ease.InOut((float)(t / 0.5));
        if (t < 1.0) return 1f;

        return 1f - Ease.InOut(Ease.Clamp((float)((t - 1.0) / 0.9)));
    }

    /// <summary>
    /// How much of the new record is showing during a change.
    /// </summary>
    /// <remarks>
    /// Nothing happens for the first half second: the old record is still there
    /// under the arm while it lifts. The swap then happens entirely inside the
    /// window where the arm is up and clear, which is the only way it reads as a
    /// record being changed rather than as one dissolving into another.
    /// </remarks>
    public static float Fade(double t)
    {
        if (t < 0.55) return 0f;
        if (t < 1.0) return Ease.InOut((float)((t - 0.55) / 0.45));

        return 1f;
    }

    /// <summary>
    /// How far through the record we are, 0 to 1.
    /// </summary>
    /// <remarks>
    /// The real position within the track when the style is following live
    /// playback, and otherwise the time since this record went on as a fraction
    /// of the seconds-per-record setting. The arm has to keep working through
    /// the grace period and while following is switched off, so the idle branch
    /// is not decoration.
    /// </remarks>
    public static float PlayProgress(
        bool following, double liveProgress, double phase, double startedAt, double secondsPerRecord)
    {
        if (following) return (float)Math.Clamp(liveProgress, 0.0, 1.0);

        var span = Math.Max(MinimumSecondsPerRecord, secondsPerRecord);
        return (float)Math.Clamp((phase - startedAt) / span, 0.0, 1.0);
    }

    /// <summary>
    /// The scrim behind the credits: where it starts, and how dark it is across.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A deliberate departure from the specification</b>, which lays a
    /// gradient from nothing to 0.93 across the right 54% of the screen and then
    /// puts a <em>flat</em> fill of 0.35 over the same rectangle. The gradient
    /// begins at nothing, so the flat fill is the whole of what you see at its
    /// left edge: the wash goes from nothing to thirty five per cent in no
    /// distance at all, and the result is a hard vertical line down the full
    /// height of the screen at 46% across.
    /// </para>
    /// <para>
    /// The same darkness is reached by one gradient instead of two fills, begun
    /// a tenth of the screen earlier so it has somewhere to fade in from. From
    /// 46% rightward the values are identical to the specification's; all that
    /// changes is that the edge is no longer an edge.
    /// </para>
    /// </remarks>
    public const float ScrimStart = 0.36f;

    /// <summary>Where the specification's own scrim began, as a fraction of the screen.</summary>
    public const float ScrimSeam = 0.46f;

    /// <summary>What the two stacked fills came to at the seam, and at the right edge.</summary>
    public const float ScrimAtSeam = 0.35f;
    public const float ScrimAtEdge = 0.955f;

    /// <summary>The seam's position within the widened scrim, 0 to 1.</summary>
    public static float ScrimSeamStop() => (ScrimSeam - ScrimStart) / (1f - ScrimStart);

    /// <summary>How dark the scrim is at a given fraction across the screen.</summary>
    public static float ScrimAlpha(float acrossScreen)
    {
        if (acrossScreen <= ScrimStart) return 0f;
        if (acrossScreen >= 1f) return ScrimAtEdge;

        if (acrossScreen <= ScrimSeam)
        {
            return ScrimAtSeam * (acrossScreen - ScrimStart) / (ScrimSeam - ScrimStart);
        }

        var across = (acrossScreen - ScrimSeam) / (1f - ScrimSeam);
        return ScrimAtSeam + ((ScrimAtEdge - ScrimAtSeam) * across);
    }

    /// <summary>How many records stay on the table.</summary>
    public static int SleeveCap(int setting, bool isPreview) =>
        isPreview ? 5 : Math.Clamp(setting, 0, 40);

    /// <summary>The grooves, from the outside in.</summary>
    /// <remarks>
    /// Every third one is drawn brighter and thinner, which is what stops fifty
    /// evenly weighted rings reading as a flat grey disc.
    /// </remarks>
    public static bool IsBrightGroove(int index) => index % 3 == 0;

    public static float GrooveStart(float radius) => radius * 0.985f;

    public static float GrooveEnd(float radius) => radius * 0.40f;

    public static float GrooveStep(float radius) => radius * 0.0125f;
}

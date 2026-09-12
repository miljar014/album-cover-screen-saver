namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// Which album a single-album style is showing, and the change from the last
/// one.
/// </summary>
/// <remarks>
/// <para>
/// Fourteen of the fifteen styles still to build stand on this. They differ in
/// what they draw around the featured cover, not in how they choose it, so the
/// choosing is here and is tested once rather than reimplemented fourteen times.
/// </para>
/// <para>
/// The rule in one sentence: follow the music when there is music, and otherwise
/// rotate on a timer, but never interrupt a change that is already under way.
/// </para>
/// <para>
/// That last clause is the one worth spelling out. A change takes 1.35 seconds
/// and nothing can start another one during it, so skipping rapidly through
/// tracks does not produce a stack of half-finished crossfades. The index is
/// simply frozen until the current one lands.
/// </para>
/// </remarks>
public sealed class Featured
{
    /// <summary>Seconds one change takes.</summary>
    public const double ChangeDuration = 1.35;

    /// <summary>
    /// The idle rotation never runs faster than this, whatever the setting says.
    /// </summary>
    public const double MinimumIdleSpan = 5.0;

    private readonly AlbumPicker _picker;
    private readonly double _recencyBias;

    private double _changeStart = -1;
    private double _startedAt;

    public Featured(AlbumPicker picker, double recencyBias, int startIndex = 0)
    {
        _picker = picker;
        _recencyBias = recencyBias;
        Index = startIndex;
        PreviousIndex = startIndex;
    }

    /// <summary>The album on show.</summary>
    public int Index { get; private set; }

    /// <summary>The one it is replacing, which is the same once a change lands.</summary>
    public int PreviousIndex { get; private set; }

    public bool IsChanging => _changeStart >= 0;

    /// <summary>
    /// One frame of the rule.
    /// </summary>
    /// <param name="live">
    /// The album being played, or null. Already gated on the signal being fresh
    /// and the cover being on disk, so a null here means "show whatever you
    /// like" rather than "nothing is playing".
    /// </param>
    /// <param name="idleSpan">Seconds between rotations when nothing is playing.</param>
    public void Advance(double phase, int? live, double idleSpan, int albumCount)
    {
        if (albumCount <= 0) return;

        if (Index >= albumCount) Index = 0;
        if (PreviousIndex >= albumCount) PreviousIndex = Index;

        if (_changeStart >= 0)
        {
            // A change in flight cannot be interrupted, and finishing one takes
            // the frame: the idle clock restarts from the moment it lands, not
            // from the moment it began.
            if (phase - _changeStart < ChangeDuration) return;

            _changeStart = -1;
            _startedAt = phase;
            return;
        }

        if (live is { } playing)
        {
            if (playing != Index) Begin(phase, playing);

            // Held forward while the music is on, so the rotation cannot fire
            // underneath live playback, and so that when the music stops there
            // is a full span before the next change rather than an immediate
            // jump. Hero + Grid does exactly this with its own clock, for the
            // same two reasons; pausing to answer the door should not throw the
            // whole visual away.
            //
            // The specification's pseudocode leaves this out and its third
            // branch has no "not live" guard, which would rotate away from the
            // playing album and then snap straight back to it once a second
            // change was allowed to start. Deliberate deviation.
            else _startedAt = phase;

            return;
        }

        if (phase - _startedAt >= Math.Max(MinimumIdleSpan, idleSpan))
        {
            Begin(phase, PickDifferent(albumCount));
        }
    }

    private int PickDifferent(int albumCount)
    {
        if (albumCount <= 1) return Index;

        var avoid = new HashSet<int> { Index };
        return _picker.PickAvoiding(albumCount, _recencyBias, avoid);
    }

    private void Begin(double phase, int next)
    {
        PreviousIndex = Index;
        Index = next;
        _changeStart = phase;
    }

    /// <summary>How far through a change we are, 0 to 1, and 1 when not changing.</summary>
    public float FadeRaw(double phase) =>
        _changeStart < 0 ? 1f : Ease.Clamp((float)((phase - _changeStart) / ChangeDuration));

    /// <summary>The same, eased. What almost every caller actually wants.</summary>
    public float Fade(double phase) => Ease.InOut(FadeRaw(phase));

    /// <summary>
    /// Starts the clock without animating, for a style that has just been built.
    /// </summary>
    public void Reset(double phase, int index)
    {
        Index = index;
        PreviousIndex = index;
        _changeStart = -1;
        _startedAt = phase;
    }
}

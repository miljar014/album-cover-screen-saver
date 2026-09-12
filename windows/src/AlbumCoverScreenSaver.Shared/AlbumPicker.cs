namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// Chooses which album to show next, biased toward recent listening.
/// </summary>
/// <remarks>
/// The archive is newest-first, so "recent" is simply the front 20% of the
/// array. That is the whole trick: no scoring, no weights, just a coin flip
/// against <c>recencyBias</c> deciding whether to draw from the head or from
/// everything.
///
/// Each screen gets its own picker with its own random source, so two displays
/// running the same style do not produce the same wall.
/// </remarks>
public sealed class AlbumPicker
{
    private readonly Random _random;

    public AlbumPicker(Random random) => _random = random;

    public AlbumPicker(int seed) : this(new Random(seed)) { }

    public int Pick(int albumCount, double recencyBias)
    {
        if (albumCount <= 1) return 0;

        if (_random.NextDouble() < recencyBias)
        {
            var head = Math.Max(1, (int)(albumCount * 0.2));
            return _random.Next(0, head);
        }

        return _random.Next(0, albumCount);
    }

    /// <summary>
    /// Picks an album not already in <paramref name="used"/>, giving up after 24
    /// tries.
    /// </summary>
    /// <remarks>
    /// Giving up is deliberate and must not be turned into a hard guarantee: on
    /// a small archive an insistent version loops forever. A duplicate on screen
    /// is a far better outcome than a frozen saver.
    /// </remarks>
    public int PickAvoiding(int albumCount, double recencyBias, IReadOnlySet<int> used)
    {
        if (albumCount <= used.Count) return Pick(albumCount, recencyBias);

        for (var attempt = 0; attempt < 24; attempt++)
        {
            var index = Pick(albumCount, recencyBias);
            if (!used.Contains(index)) return index;
        }

        return Pick(albumCount, recencyBias);
    }
}

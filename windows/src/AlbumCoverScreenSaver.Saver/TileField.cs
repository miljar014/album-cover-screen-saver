using AlbumCoverScreenSaver.Shared;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// The grid of covers that Mosaic Grid, Slow-Building Wall and Hero + Grid all
/// stand on: building it, choosing what goes in it, and turning tiles over.
/// </summary>
/// <remarks>
/// Pulled out of the three styles rather than copied into each of them, because
/// the flip arrival process below is the single detail most easily got wrong,
/// and three copies of it is three chances to get it wrong differently. What
/// each style does with the field is what makes it that style: Mosaic flips at
/// the plain tempo, Hero flips at two thirds of it behind a dimmed scrim, and
/// Wall never flips at all.
/// </remarks>
internal sealed class TileField
{
    /// <summary>The nominal frame interval. See the note on phase in Scene.</summary>
    public const double FrameInterval = 1.0 / 30.0;

    private readonly SaverData _data;
    private readonly AlbumPicker _picker;
    private readonly Random _random;

    public TileField(SaverData data, AlbumPicker picker, Random random)
    {
        _data = data;
        _picker = picker;
        _random = random;
    }

    public List<Tile> Tiles { get; } = [];

    public int Count => Tiles.Count;

    /// <summary>Lays out the cells and fills them with albums.</summary>
    public void Build(float width, float height, float cellTarget, bool varied)
    {
        Tiles.Clear();

        if (_data.Albums.Count == 0 || width <= 10 || height <= 10) return;

        var cells = varied
            ? GridBuilder.MakeVariedGrid(width, height, cellTarget, _random)
            : GridBuilder.MakeGrid(width, height, cellTarget);

        foreach (var cell in cells) Tiles.Add(new Tile(cell));

        AssignAlbums();
    }

    /// <summary>
    /// Re-picks every album, keeping the cells exactly where they are.
    /// </summary>
    /// <remarks>
    /// Distinct albums where the archive allows it. Once every album is already
    /// on screen the picker degrades to a plain pick and repeats appear, which
    /// is correct rather than a failure: the alternative loops forever on a
    /// small archive.
    /// </remarks>
    public void AssignAlbums()
    {
        var albums = _data.Albums;
        if (albums.Count == 0) return;

        var used = new HashSet<int>();
        foreach (var tile in Tiles)
        {
            var index = _picker.PickAvoiding(albums.Count, _data.Settings.RecencyBias, used);
            used.Add(index);
            tile.Index = index;
            tile.NextIndex = -1;
            tile.FlipStart = -1;
        }
    }

    /// <summary>
    /// A 200 point target on a preview pane a couple of hundred points wide
    /// would give four covers rather than anything representative.
    /// </summary>
    public static float CellTarget(double tileSize, bool isPreview) =>
        isPreview ? Math.Max(12f, (float)tileSize / 6f) : (float)tileSize;

    public void AdvanceFlips(double phase, double interval)
    {
        RetireFinished(phase);
        StartNew(phase, interval);
    }

    private void RetireFinished(double phase)
    {
        foreach (var tile in Tiles)
        {
            if (!tile.IsFlipping) continue;

            var t = (phase - tile.FlipStart) / Math.Max(0.15, tile.FlipDuration);

            // Swapped at the zero-width moment, and exactly once, because
            // NextIndex is cleared as it happens.
            if (t >= 0.5 && tile.NextIndex >= 0)
            {
                tile.Index = tile.NextIndex;
                tile.NextIndex = -1;
            }

            if (t >= 1) tile.FlipStart = -1;
        }
    }

    /// <summary>
    /// The arrival process. This is the single most important detail in the grid
    /// styles.
    /// </summary>
    /// <remarks>
    /// An earlier version of the macOS build fired a whole batch of flips on a
    /// metronome, and several tiles turned in perfect unison. That reads as a
    /// glitch, and it is described as the most consistent piece of visual
    /// feedback from the entire project.
    ///
    /// So flips arrive as independent Bernoulli trials whose success
    /// probabilities sum to the expected number of flips this frame: one trial
    /// at p for an expected value below 1, and for 2.3 three trials at p = 1, 1
    /// and 0.3. Same average throughput, but the gaps between arrivals vary and
    /// no two are ever scheduled together.
    ///
    /// Do not replace this with a timer.
    /// </remarks>
    private void StartNew(double phase, double interval)
    {
        if (Tiles.Count == 0 || _data.Albums.Count == 0) return;

        var perSecond = Math.Max(1, _data.Settings.FlipsAtOnce) / Math.Max(0.3, interval);
        var expected = perSecond * FrameInterval;

        while (expected > 0)
        {
            if (_random.NextDouble() < Math.Min(1.0, expected)) StartFlip(phase);
            expected -= 1;
        }
    }

    private void StartFlip(double phase)
    {
        var tile = Tiles[_random.Next(0, Tiles.Count)];

        // An arrival landing on a tile that is already turning is discarded, not
        // queued. That self-limits the realised rate when many tiles are busy.
        if (tile.IsFlipping) return;

        var onScreen = new HashSet<int>();
        foreach (var other in Tiles) onScreen.Add(other.Index);

        tile.FlipStart = phase;
        tile.NextIndex = _picker.PickAvoiding(_data.Albums.Count, _data.Settings.RecencyBias, onScreen);

        // Each flip gets its own duration, within -28% and +38% of the setting.
        // Identical timing reads as machinery even when the starts are staggered.
        tile.FlipDuration = Math.Max(0.15, _data.Settings.FlipDuration)
                            * (0.72 + (_random.NextDouble() * 0.66));
    }
}

using AlbumCoverScreenSaver.Shared;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>One cell of a grid style, and whatever it is doing right now.</summary>
internal sealed class Tile
{
    public Tile(GridCell cell) => Cell = cell;

    public GridCell Cell { get; set; }

    /// <summary>The album currently shown.</summary>
    public int Index { get; set; }

    /// <summary>The album being flipped to, or -1 when idle.</summary>
    public int NextIndex { get; set; } = -1;

    /// <summary>The phase at which the current flip began, or -1 when idle.</summary>
    public double FlipStart { get; set; } = -1;

    /// <summary>
    /// Per tile, not shared. Identical timing reads as machinery even when the
    /// starts are staggered.
    /// </summary>
    public double FlipDuration { get; set; } = 0.75;

    /// <summary>
    /// Slow-Building Wall only: this tile's offset within the fill, in seconds.
    /// Dividing it by the step recovers the tile's place in the arrival order,
    /// which is how the dissolve leaves them in the order they came.
    /// </summary>
    public double AppearAt { get; set; }

    public bool IsFlipping => FlipStart >= 0;
}

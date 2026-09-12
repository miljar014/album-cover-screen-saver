namespace AlbumCoverScreenSaver.Shared;

/// <summary>One cell of a grid, in points, with Y as the top edge.</summary>
public readonly record struct GridCell(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
}

/// <summary>
/// The exact-fit grid shared by Mosaic Grid, Slow-Building Wall and Hero + Grid.
/// </summary>
/// <remarks>
/// <para>
/// The point of it is in the name: the cells tile the display <em>exactly</em>,
/// with no partial row running off the bottom edge. A screen's aspect ratio
/// almost never divides into whole square cells, so the cells come out slightly
/// non-square and covers are drawn aspect-fill, cropping a sliver. On album art
/// a crop reads as nothing at all, whereas a sliced-off bottom row is obvious.
/// </para>
/// <para>
/// This lives in the shared library because it is arithmetic, which means the
/// tiling can be proved by test rather than by squinting at a screen.
/// </para>
/// <para>
/// <b>Coordinates.</b> The specification is written in AppKit's y-up space where
/// row 0 is the bottom row. These cells are y-down, so row 0 is the top. For a
/// grid that tiles the screen exactly, and whose albums are assigned randomly,
/// the two are visually identical. Nothing else in the port mixes the two: the
/// conversion happens here and in the label placement, and nowhere else.
/// </para>
/// </remarks>
public static class GridBuilder
{
    /// <summary>
    /// A uniform grid of cells covering the whole area.
    /// </summary>
    /// <remarks>
    /// Note that the row count comes from <c>cellW</c>, not from
    /// <c>targetSide</c>. That is what keeps the cells close to square after the
    /// column count has been rounded.
    /// </remarks>
    public static List<GridCell> MakeGrid(float width, float height, float targetSide)
    {
        var (columns, cellWidth, rows, cellHeight) = Measure(width, height, targetSide);

        var cells = new List<GridCell>(rows * columns);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                cells.Add(new GridCell(column * cellWidth, row * cellHeight, cellWidth, cellHeight));
            }
        }
        return cells;
    }

    /// <summary>
    /// The same grid with some covers promoted to 2x2 blocks, so the wall reads
    /// as a mosaic rather than a uniform checkerboard.
    /// </summary>
    /// <remarks>
    /// Blocks are placed first and the leftover gaps filled with single cells
    /// afterwards. That order is what guarantees the screen still tiles exactly,
    /// with no overlaps and no holes.
    ///
    /// The loop counts <em>attempts</em>, not placements: a block that collides
    /// with one already placed is skipped, so the realised number of blocks is
    /// at or below one per seven cells. Enough to break the grid up, sparse
    /// enough that the big ones still read as accents.
    /// </remarks>
    public static List<GridCell> MakeVariedGrid(float width, float height, float targetSide, Random random)
    {
        var (columns, cellWidth, rows, cellHeight) = Measure(width, height, targetSide);

        if (rows <= 1 || columns <= 1) return MakeGrid(width, height, targetSide);

        var taken = new bool[rows, columns];
        var cells = new List<GridCell>();

        var attempts = Math.Max(1, rows * columns / 7);
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            // Upper bound is exclusive, so row + 1 and column + 1 are always in range.
            var row = random.Next(0, rows - 1);
            var column = random.Next(0, columns - 1);

            if (taken[row, column] || taken[row + 1, column] ||
                taken[row, column + 1] || taken[row + 1, column + 1])
            {
                continue;
            }

            taken[row, column] = true;
            taken[row + 1, column] = true;
            taken[row, column + 1] = true;
            taken[row + 1, column + 1] = true;

            cells.Add(new GridCell(
                column * cellWidth, row * cellHeight, 2f * cellWidth, 2f * cellHeight));
        }

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                if (taken[row, column]) continue;
                cells.Add(new GridCell(column * cellWidth, row * cellHeight, cellWidth, cellHeight));
            }
        }

        return cells;
    }

    /// <summary>
    /// The column and row counts and the resulting cell size. Public because
    /// Hero + Grid needs the same measurement at a different target, and
    /// because it is what the tiling tests check against.
    /// </summary>
    public static GridLayout Measure(float width, float height, float targetSide)
    {
        if (targetSide <= 0) targetSide = 1;

        // Away from zero, because Swift's rounded() is, and .NET's default is
        // banker's rounding. On a 1920 wide screen with a 320 point target that
        // is the difference between six columns and six columns, but at a
        // half-way value it is a different grid on the two platforms.
        var columns = Math.Max(3, (int)Math.Round(width / targetSide, MidpointRounding.AwayFromZero));
        var cellWidth = width / columns;

        var rows = Math.Max(2, (int)Math.Round(height / cellWidth, MidpointRounding.AwayFromZero));
        var cellHeight = height / rows;

        return new GridLayout(columns, cellWidth, rows, cellHeight);
    }
}

/// <summary>How a grid divides an area.</summary>
public readonly record struct GridLayout(int Columns, float CellWidth, int Rows, float CellHeight);

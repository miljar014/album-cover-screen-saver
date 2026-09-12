namespace AlbumCoverScreenSaver.Shared.Tests;

/// <summary>
/// The arithmetic behind Mosaic Grid. Every one of these is something doc 01
/// warns is easy to get wrong, and every one of them fails silently on screen
/// rather than throwing, so they are worth proving here.
/// </summary>
public static class EngineMathTests
{
    /// <summary>
    /// The real test of a grid: sample the centre of every notional cell and
    /// check exactly one returned rectangle covers it. Zero means a hole, two
    /// means an overlap, and both look like a rendering glitch rather than a
    /// maths bug.
    /// </summary>
    private static void CheckTilesExactly(
        IReadOnlyList<GridCell> cells, float width, float height, float target, string what)
    {
        var layout = GridBuilder.Measure(width, height, target);

        for (var row = 0; row < layout.Rows; row++)
        {
            for (var column = 0; column < layout.Columns; column++)
            {
                var x = (column + 0.5f) * layout.CellWidth;
                var y = (row + 0.5f) * layout.CellHeight;

                var covering = cells.Count(cell =>
                    x >= cell.X && x < cell.Right && y >= cell.Y && y < cell.Bottom);

                Check.Equal(1, covering, $"{what}: cell ({row},{column}) covered exactly once");
            }
        }

        var area = cells.Sum(cell => (double)cell.Width * cell.Height);
        Check.Close(width * height, area, width * height * 1e-4, $"{what}: total area");
    }

    public static void Register(TestRunner runner)
    {
        runner.Group("Easing");

        runner.Add("all three curves start at zero and end at one", () =>
        {
            foreach (var (name, curve) in new (string, Func<float, float>)[]
                     { ("InOut", Ease.InOut), ("Out", Ease.Out), ("Back", Ease.Back) })
            {
                Check.Close(0d, curve(0f), 1e-5, $"{name}(0)");
                Check.Close(1d, curve(1f), 1e-5, $"{name}(1)");
            }
        });

        runner.Add("clamp before easing, so out of range input is harmless", () =>
        {
            Check.Close(0d, Ease.InOut(-4f), 1e-6, "InOut(-4)");
            Check.Close(1d, Ease.InOut(9f), 1e-6, "InOut(9)");
            Check.Close(0d, Ease.Clamp(-0.5f), 1e-6, "Clamp(-0.5)");
            Check.Close(1d, Ease.Clamp(1.5f), 1e-6, "Clamp(1.5)");
        });

        runner.Add("ease-in-out is symmetric about the halfway point", () =>
        {
            Check.Close(0.5d, Ease.InOut(0.5f), 1e-5, "InOut(0.5)");
            for (var i = 1; i < 10; i++)
            {
                var t = i / 10f;
                Check.Close(1d - Ease.InOut(t), Ease.InOut(1f - t), 1e-5, $"symmetry at {t}");
            }
        });

        runner.Add("ease-in-out never goes backwards", () =>
        {
            var previous = -1f;
            for (var i = 0; i <= 100; i++)
            {
                var value = Ease.InOut(i / 100f);
                Check.True(value >= previous - 1e-6f, $"monotonic at {i / 100f}");
                previous = value;
            }
        });

        runner.Add("back overshoots, which is the whole point of it", () =>
        {
            var peak = 0f;
            for (var i = 0; i <= 100; i++) peak = Math.Max(peak, Ease.Back(i / 100f));
            Check.True(peak > 1.05f, $"expected an overshoot above 1.05, peaked at {peak}");
            Check.True(peak < 1.20f, $"expected the overshoot to stay modest, peaked at {peak}");
        });

        runner.Group("Grid maths");

        runner.Add("never fewer than three columns or two rows", () =>
        {
            // A huge tile target on a small screen must not collapse the grid.
            var layout = GridBuilder.Measure(400, 300, 5000);
            Check.Equal(3, layout.Columns, "columns");
            Check.Equal(2, layout.Rows, "rows");
        });

        runner.Add("derives the row count from the cell width, not the target", () =>
        {
            // This is the line that keeps cells close to square after the column
            // count has been rounded. Deriving rows from targetSide instead
            // gives visibly stretched cells on most screens.
            var layout = GridBuilder.Measure(1920, 1080, 200);
            Check.Equal(10, layout.Columns, "columns from 1920/200");
            Check.Close(192d, layout.CellWidth, 1e-3, "cell width");
            Check.Equal(6, layout.Rows, "rows from 1080/192, not 1080/200");
        });

        runner.Add("rounds away from zero, as Swift does", () =>
        {
            // .NET rounds halves to even by default. On an exact half that is a
            // different grid from the Mac's, for no reason anyone would ever
            // find by looking.
            var layout = GridBuilder.Measure(500, 400, 200);
            Check.Equal(3, layout.Columns, "500/200 = 2.5 rounds up to 3");
        });

        runner.Add("a plain grid tiles the screen exactly", () =>
        {
            foreach (var (width, height, target) in new[]
                     {
                         (1920f, 1080f, 200f),
                         (2560f, 1440f, 200f),
                         (4112f, 2580f, 200f),
                         (1080f, 1920f, 160f),
                         (3440f, 1440f, 240f),
                         (800f, 600f, 90f),
                     })
            {
                var cells = GridBuilder.MakeGrid(width, height, target);
                CheckTilesExactly(cells, width, height, target, $"{width}x{height} at {target}");
            }
        });

        runner.Add("a varied grid also tiles the screen exactly", () =>
        {
            // Blocks placed first, gaps filled after. Any hole or overlap here
            // is visible as a black gap or a seam between two covers.
            for (var seed = 0; seed < 25; seed++)
            {
                var cells = GridBuilder.MakeVariedGrid(1920, 1080, 200, new Random(seed));
                CheckTilesExactly(cells, 1920, 1080, 200, $"seed {seed}");
            }
        });

        runner.Add("promotes some cells to 2x2 and not too many", () =>
        {
            var layout = GridBuilder.Measure(1920, 1080, 200);
            var cap = Math.Max(1, layout.Rows * layout.Columns / 7);

            var sawABlock = false;
            for (var seed = 0; seed < 25; seed++)
            {
                var cells = GridBuilder.MakeVariedGrid(1920, 1080, 200, new Random(seed));

                var blocks = cells.Count(cell => cell.Width > layout.CellWidth * 1.5f);
                Check.True(blocks <= cap, $"seed {seed}: {blocks} blocks is within the cap of {cap}");
                if (blocks > 0) sawABlock = true;

                foreach (var cell in cells)
                {
                    var wide = cell.Width > layout.CellWidth * 1.5f;
                    var tall = cell.Height > layout.CellHeight * 1.5f;
                    Check.Equal(wide, tall, $"seed {seed}: blocks are square in cells, never 1x2 or 2x1");
                }
            }
            Check.True(sawABlock, "at least one seed produced a block");
        });

        runner.Add("falls back to a plain grid when a block cannot fit", () =>
        {
            var cells = GridBuilder.MakeVariedGrid(400, 300, 5000, new Random(1));
            CheckTilesExactly(cells, 400, 300, 5000, "tiny grid");
        });

        runner.Group("Album selection");

        runner.Add("always returns a valid index", () =>
        {
            var picker = new AlbumPicker(7);
            foreach (var count in new[] { 1, 2, 3, 10, 137 })
            {
                for (var i = 0; i < 300; i++)
                {
                    var index = picker.Pick(count, 0.4);
                    Check.True(index >= 0 && index < count, $"count {count}: index {index} in range");
                }
            }
            Check.Equal(0, new AlbumPicker(1).Pick(0, 0.4), "an empty archive picks zero rather than throwing");
        });

        runner.Add("a bias of one draws only from the newest fifth", () =>
        {
            // The archive is newest first, so recency is just the front of the
            // array. Nothing scores or weights anything.
            var picker = new AlbumPicker(11);
            var head = Math.Max(1, (int)(100 * 0.2));

            for (var i = 0; i < 400; i++)
            {
                Check.True(picker.Pick(100, 1.0) < head, "stayed inside the newest 20 albums");
            }
        });

        runner.Add("a bias of zero reaches the whole archive", () =>
        {
            var picker = new AlbumPicker(13);
            var seen = new HashSet<int>();
            for (var i = 0; i < 4000; i++) seen.Add(picker.Pick(50, 0.0));

            Check.True(seen.Count > 45, $"expected to see nearly every album, saw {seen.Count} of 50");
            Check.True(seen.Contains(49), "including the very oldest");
        });

        runner.Add("avoids albums already on screen", () =>
        {
            var picker = new AlbumPicker(17);
            var used = new HashSet<int> { 0, 1, 2, 3, 4 };

            var collisions = 0;
            for (var i = 0; i < 500; i++)
            {
                if (used.Contains(picker.PickAvoiding(60, 0.4, used))) collisions++;
            }

            // 24 tries against a large archive should essentially never collide.
            Check.True(collisions < 5, $"expected almost no repeats, got {collisions} in 500");
        });

        runner.Add("gives up rather than looping when the archive is too small", () =>
        {
            // Insisting here is how a small archive freezes the saver. A
            // duplicate on screen is the better failure.
            var picker = new AlbumPicker(19);
            var used = new HashSet<int> { 0, 1, 2 };

            for (var i = 0; i < 200; i++)
            {
                var index = picker.PickAvoiding(3, 0.4, used);
                Check.True(index >= 0 && index < 3, "still returned a usable index");
            }
        });
    }
}

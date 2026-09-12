namespace AlbumCoverScreenSaver.Shared.Tests;

/// <summary>
/// The persistent board that Record Player and Polaroid Corkboard stand on.
/// </summary>
/// <remarks>
/// Doc 01 calls the persistence rules the point of the whole thing, and every
/// one of them exists because of something that went wrong without it: a pile
/// wiped every three minutes, records that jumped when the resolution changed,
/// newly played songs that silently never appeared. All three failures look like
/// the app having stopped working rather than like a layout bug, so they are
/// pinned down here.
/// </remarks>
public static class ScatterTests
{
    private const float Width = 1920f;
    private const float Height = 1080f;

    private static ScatterLayout NewBoard(int seed = 1234) => new(new Random(seed));

    private static TableSpot? Spot(ScatterLayout board) =>
        board.FindSpot(Width, Height, 0.10f, 0.16f, 1.0f);

    public static void Register(TestRunner runner)
    {
        Persistence(runner);
        Placement(runner);
        Scatter(runner);
    }

    // --- the rules that make it a board rather than a backdrop --------------

    private static void Persistence(TestRunner runner)
    {
        runner.Group("Board: what stays put and why");

        runner.Add("an album already on the board is never moved", () =>
        {
            var board = NewBoard();
            board.Pin("lfm-miles", Spot(board), phase: 10);

            var before = board.Table[0];

            // Every subsequent play of the same record is a no-op. Without
            // this, a record you are listening to walks around the table.
            for (var play = 0; play < 20; play++)
            {
                Check.False(board.Pin("lfm-miles", Spot(board), phase: 100 + play), "re-pin refused");
            }

            Check.Equal(1, board.Table.Count, "still one record");
            Check.Equal(before, board.Table[0], "in exactly the same place");
        });

        runner.Add("the board is cleared only when a different style takes it", () =>
        {
            var board = NewBoard();
            board.Claim(CollageMode.Vinyl);
            board.Pin("lfm-a", Spot(board), 1);
            board.Pin("lfm-b", Spot(board), 2);

            // Claiming again for the same style is what happens on every frame.
            board.Claim(CollageMode.Vinyl);
            Check.Equal(2, board.Table.Count, "kept across a re-claim");

            board.Claim(CollageMode.Polaroid);
            Check.Equal(0, board.Table.Count, "cleared for a different style");
        });

        runner.Add("the cap retires the oldest pinned record, not the furthest or the least played", () =>
        {
            var board = NewBoard();
            for (var i = 0; i < 10; i++) board.Pin($"lfm-{i}", Spot(board), i);

            board.TrimTo(4);

            Check.Equal(4, board.Table.Count, "trimmed");
            Check.Sequence(
                new[] { "lfm-6", "lfm-7", "lfm-8", "lfm-9" },
                board.Table.Select(item => item.AlbumId),
                "the six oldest went, in order");
        });

        runner.Add("a cap of zero empties the board", () =>
        {
            var board = NewBoard();
            for (var i = 0; i < 5; i++) board.Pin($"lfm-{i}", Spot(board), i);

            board.TrimTo(0);
            Check.Equal(0, board.Table.Count, "emptied");
        });

        runner.Add("positions are normalised, so a change of resolution carries the pile", () =>
        {
            var board = NewBoard();
            for (var i = 0; i < 8; i++) board.Pin($"lfm-{i}", Spot(board), i);

            foreach (var item in board.Table)
            {
                Check.True(item.Nx is >= 0f and <= 1f, $"nx {item.Nx} is outside the view");
                Check.True(item.Ny is >= 0f and <= 1f, $"ny {item.Ny} is outside the view");
            }

            // The same fractions on a different screen put every record in the
            // same relative place rather than re-scattering the table.
            var first = board.Table[0];
            Check.Close(first.Nx * 3840f, first.Nx * Width * 2f, 0.001, "scales with the width");
        });

        runner.Add("a record is identified by its album id, not by a position in a list", () =>
        {
            // The album list reorders every time the archive grows. An index
            // stored on the board would come to mean a different record, which
            // is silent and looks like the art being wrong.
            var board = NewBoard();
            board.Pin("lfm-kate-bush-hounds-of-love", Spot(board), 1);

            Check.Equal("lfm-kate-bush-hounds-of-love", board.Table[0].AlbumId, "the id is what is stored");
        });

        runner.Add("seeding puts the most recently played record on top", () =>
        {
            var board = NewBoard();
            string[] newestFirst = ["lfm-newest", "lfm-middle", "lfm-oldest"];

            board.Seed(newestFirst, cap: 3, phase: 0, () => Spot(board));

            // Drawn in order, so last pinned is on top, which is where a person
            // would have put the record they just played.
            Check.Equal("lfm-newest", board.Table[^1].AlbumId, "newest went on last");
            Check.Equal("lfm-oldest", board.Table[0].AlbumId, "oldest went on first");
        });

        runner.Add("seeding does nothing to a board that already has records on it", () =>
        {
            var board = NewBoard();
            board.Pin("lfm-already-here", Spot(board), 1);
            board.Seed(["lfm-a", "lfm-b"], cap: 5, phase: 2, () => Spot(board));

            Check.Equal(1, board.Table.Count, "left alone");
        });

        runner.Add("a record given nowhere to go is not pinned", () =>
        {
            var board = NewBoard();
            Check.False(board.Pin("lfm-nowhere", null, 1), "no spot, no pin");
            Check.Equal(0, board.Table.Count, "nothing added");
        });
    }

    // --- finding somewhere to put one ---------------------------------------

    private static void Placement(TestRunner runner)
    {
        runner.Group("Board: finding a free spot");

        runner.Add("a spot is always fully inside the view", () =>
        {
            var board = NewBoard(99);

            for (var i = 0; i < 60; i++)
            {
                var spot = board.FindSpot(Width, Height, 0.10f, 0.16f, 1.0f);
                Check.True(spot is not null, "a spot was found");

                var half = Height * spot!.Value.SizeFraction * 0.75f;
                var x = spot.Value.Nx * Width;
                var y = spot.Value.Ny * Height;

                Check.True(x >= half - 0.5f && x <= Width - half + 0.5f, $"x {x:0} against half {half:0}");
                Check.True(y >= half - 0.5f && y <= Height - half + 0.5f, $"y {y:0} against half {half:0}");

                board.Pin($"lfm-{i}", spot, i);
            }
        });

        runner.Add("early records do not land on top of each other", () =>
        {
            var board = NewBoard(2468);
            for (var i = 0; i < 8; i++) board.Pin($"lfm-{i}", Spot(board), i);

            for (var a = 0; a < board.Table.Count; a++)
            {
                for (var b = a + 1; b < board.Table.Count; b++)
                {
                    var first = board.Table[a];
                    var second = board.Table[b];

                    var dx = (first.Nx - second.Nx) * Width;
                    var dy = (first.Ny - second.Ny) * Height;
                    var apart = MathF.Sqrt((dx * dx) + (dy * dy));

                    var sizes = (first.SizeFraction + second.SizeFraction) * Height;

                    // The loosest tolerance the search will ever accept.
                    Check.True(apart >= sizes * 0.30f * 0.98f,
                        $"records {a} and {b} are {apart:0} apart at combined size {sizes:0}");
                }
            }
        });

        runner.Add("a crowded board keeps accepting records rather than refusing them", () =>
        {
            // This is the emptiest-spot fallback earning its place. Returning
            // nothing here is what made newly played songs silently fail to
            // appear, which looks like the app having stopped noticing music.
            var board = NewBoard(31415);
            var pinned = 0;

            for (var i = 0; i < 40; i++)
            {
                if (board.Pin($"lfm-{i}", Spot(board), i)) pinned++;
            }

            Check.Equal(40, pinned, "every record found somewhere");
        });

        runner.Add("the keep-out circle is respected", () =>
        {
            // Record Player uses this to hold the area under the turntable
            // clear, so records do not pile onto the platter.
            var board = NewBoard(5);
            const float centreX = Width * 0.33f;
            const float centreY = Height * 0.5f;
            const float radius = 300f;

            for (var i = 0; i < 40; i++)
            {
                var spot = board.FindSpot(
                    Width, Height, 0.10f, 0.16f, 1.0f,
                    keepOutCentreX: centreX, keepOutCentreY: centreY, keepOutRadius: radius);

                if (spot is null) continue;

                var dx = (spot.Value.Nx * Width) - centreX;
                var dy = (spot.Value.Ny * Height) - centreY;
                var distance = MathF.Sqrt((dx * dx) + (dy * dy));

                // The fallback path tightens the circle to 92% and stops
                // padding it, so that is the limit that has to hold.
                Check.True(distance >= radius * 0.92f,
                    $"a record landed {distance:0} from the platter, inside {radius * 0.92f:0}");

                board.Pin($"lfm-{i}", spot, i);
            }
        });

        runner.Add("the keep-out rectangle is respected", () =>
        {
            // Used to hold a column of typography clear.
            var board = NewBoard(6);
            var box = (X: Width * 0.62f, Y: Height * 0.2f, Width: Width * 0.3f, Height: Height * 0.6f);
            var landedInside = 0;

            for (var i = 0; i < 12; i++)
            {
                var spot = board.FindSpot(Width, Height, 0.08f, 0.12f, 1.0f, keepOutRect: box);
                if (spot is null) continue;

                var x = spot.Value.Nx * Width;
                var y = spot.Value.Ny * Height;

                if (x >= box.X && x <= box.X + box.Width && y >= box.Y && y <= box.Y + box.Height)
                {
                    landedInside++;
                }

                board.Pin($"lfm-{i}", spot, i);
            }

            Check.Equal(0, landedInside, "records inside the reserved column");
        });

        runner.Add("a view too small for the record gives nothing rather than looping", () =>
        {
            var board = NewBoard();

            Check.True(board.FindSpot(4f, 4f, 0.1f, 0.16f, 1f) is null, "a view of nothing");
            Check.True(board.FindSpot(Width, Height, 2.0f, 2.5f, 1f) is null, "a record bigger than the screen");
        });

        runner.Add("the size multiplier scales the records", () =>
        {
            var small = NewBoard(11).FindSpot(Width, Height, 0.10f, 0.16f, 0.6f)!.Value;
            var large = NewBoard(11).FindSpot(Width, Height, 0.10f, 0.16f, 1.9f)!.Value;

            Check.True(large.SizeFraction > small.SizeFraction * 2.5f,
                $"{small.SizeFraction:0.000} against {large.SizeFraction:0.000}");
        });

        runner.Add("a pinned record gets a crooked angle and sometimes a peeking disc", () =>
        {
            var board = NewBoard(777);
            for (var i = 0; i < 60; i++) board.Pin($"lfm-{i}", Spot(board), i);

            foreach (var item in board.Table)
            {
                Check.True(Math.Abs(item.Angle) <= 0.24f, $"angle {item.Angle} is more than 14 degrees");
                Check.True(item.DiscPeek == 0f || (item.DiscPeek is >= 0.20f and <= 0.44f),
                    $"disc peek {item.DiscPeek}");
                Check.True(Math.Abs(item.DiscDirection) <= 0.7f, $"disc direction {item.DiscDirection}");
            }

            var peeking = board.Table.Count(item => item.DiscPeek > 0f);
            Check.True(peeking is > 10 and < 50, $"{peeking} of 60 records show a disc, expected about 27");
        });
    }

    // --- the disposable sibling ---------------------------------------------

    private static void Scatter(TestRunner runner)
    {
        runner.Group("Board: the disposable scatter");

        runner.Add("it builds the number of covers asked for", () =>
        {
            var board = NewBoard(17);
            var scatter = board.BuildScatter(
                Width, Height, count: 14, albumCount: 24, recencyBias: 0.4,
                picker: new AlbumPicker(9), minSizeFraction: 0.10f, maxSizeFraction: 0.18f);

            Check.Equal(14, scatter.Count, "covers placed");
        });

        runner.Add("a preview gets a third of them, never fewer than three", () =>
        {
            var board = NewBoard(17);

            var few = board.BuildScatter(
                Width, Height, 15, 24, 0.4, new AlbumPicker(9), 0.10f, 0.18f, isPreview: true);
            Check.Equal(5, few.Count, "a third of fifteen");

            var floor = board.BuildScatter(
                Width, Height, 4, 24, 0.4, new AlbumPicker(9), 0.10f, 0.18f, isPreview: true);
            Check.Equal(3, floor.Count, "the floor of three");
        });

        runner.Add("it stores pixels, not fractions", () =>
        {
            // The opposite of the board, and deliberately so: a scatter is
            // rebuilt whenever the layout changes, so it has nothing to carry
            // across a resolution change.
            var scatter = NewBoard(3).BuildScatter(
                Width, Height, 10, 24, 0.4, new AlbumPicker(9), 0.10f, 0.18f);

            Check.True(scatter.Any(item => item.CentreX > 1.5f), "centres are in pixels");
            Check.True(scatter.All(item => item.Side > 50f), "sides are in pixels");
        });

        runner.Add("nothing to draw gives an empty scatter rather than a crash", () =>
        {
            var board = NewBoard();
            var picker = new AlbumPicker(1);

            Check.Equal(0, board.BuildScatter(Width, Height, 0, 24, 0.4, picker, 0.1f, 0.2f).Count, "no covers wanted");
            Check.Equal(0, board.BuildScatter(Width, Height, 10, 0, 0.4, picker, 0.1f, 0.2f).Count, "no albums");
            Check.Equal(0, board.BuildScatter(4f, 4f, 10, 24, 0.4, picker, 0.1f, 0.2f).Count, "no room");
        });

        runner.Add("a scatter respects its keep-out circle", () =>
        {
            var scatter = NewBoard(21).BuildScatter(
                Width, Height, 20, 24, 0.4, new AlbumPicker(9), 0.08f, 0.14f,
                keepOutCentreX: Width / 2f, keepOutCentreY: Height / 2f, keepOutRadius: 280f);

            foreach (var item in scatter)
            {
                var dx = item.CentreX - (Width / 2f);
                var dy = item.CentreY - (Height / 2f);
                Check.True(MathF.Sqrt((dx * dx) + (dy * dy)) >= 280f, "kept clear of the centre");
            }
        });

        runner.Add("a scatter never shows a disc peeking out", () =>
        {
            // Only the board does that. A backdrop of half-pulled records reads
            // as clutter rather than as a pile someone has been through.
            var scatter = NewBoard(4).BuildScatter(
                Width, Height, 12, 24, 0.4, new AlbumPicker(9), 0.10f, 0.18f);

            Check.True(scatter.All(item => item.DiscPeek == 0f), "no discs");
        });
    }
}

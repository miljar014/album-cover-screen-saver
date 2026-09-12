namespace AlbumCoverScreenSaver.Shared.Tests;

/// <summary>
/// Turning a cover into printed dots.
/// </summary>
public static class HalftoneTests
{
    public static void Register(TestRunner runner)
    {
        Dots(runner);
        Page(runner);
    }

    private static void Page(TestRunner runner)
    {
        runner.Group("Newsstand: fitting the page");

        runner.Add("a tall page gets the photograph the design asks for", () =>
        {
            // A page taller than it is wide has room to spare, and then the
            // width the specification asks for is exactly what it gets. This is
            // the case where the rule must keep out of the way.
            const float columnWidth = 1080f * 0.88f;

            Check.Close(
                columnWidth * NewsstandPage.WantedFraction,
                NewsstandPage.PhotoSide(columnWidth, 1920f, 1920f * 0.33f),
                0.01, "the full width");
        });

        runner.Add("a wide page gets exactly the room it has left, and no more", () =>
        {
            // The specification sizes the photograph from the width of the page
            // alone: colW * 0.46. On any ordinary screen that is taller than
            // what is left below the headline, so the Mac's own page runs off
            // the bottom too. This is a deliberate departure from it.
            const float height = 1080f;
            const float columnWidth = 1920f * 0.88f;
            var top = height * 0.33f;

            var wanted = columnWidth * NewsstandPage.WantedFraction;
            var got = NewsstandPage.PhotoSide(columnWidth, height, top);

            Check.True(got < wanted, "sixteen by nine has room for the full width, which it should not");

            Check.Close(
                (height * NewsstandPage.BottomFraction) - top - (height * NewsstandPage.CaptionFraction),
                got, 0.01, "it takes everything that is left");
        });

        runner.Add("a photograph never runs past the foot of the page", () =>
        {
            // The case the recording caught: a tall page and a headline that
            // pushed the photograph a long way down. It used to be sized from
            // the width alone and ran off the bottom, taking its caption and
            // the colour bar with it.
            foreach (var height in new[] { 720f, 1080f, 1290f, 2160f })
            {
                foreach (var ratio in new[] { 1.33f, 1.6f, 1.78f, 2.39f, 3.55f })
                {
                    var columnWidth = height * ratio * 0.88f;

                    foreach (var startFraction in new[] { 0.30f, 0.38f, 0.45f, 0.52f })
                    {
                        var top = height * startFraction;
                        var side = NewsstandPage.PhotoSide(columnWidth, height, top);
                        var bottom = top + side;

                        // The floor is allowed to win on an absurdly wide page,
                        // and then the photograph may reach past the foot. That
                        // is the one case the rule deliberately gives up on.
                        if (side <= columnWidth * NewsstandPage.FloorFraction + 0.01f) continue;

                        Check.True(
                            bottom <= height * NewsstandPage.BottomFraction,
                            $"{ratio:0.00} at {height:0} starting at {startFraction:0.00} "
                            + $"ends at {bottom / height:0.000} of the height");
                    }
                }
            }
        });

        runner.Add("the caption always has somewhere to go", () =>
        {
            const float height = 1080f;
            const float columnWidth = 1920f * 0.88f;

            var top = height * 0.44f;
            var bottom = top + NewsstandPage.PhotoSide(columnWidth, height, top);

            Check.True(
                height - bottom >= height * NewsstandPage.CaptionFraction,
                "the caption is squeezed off the page");
        });

        runner.Add("the photograph never shrinks to a stamp", () =>
        {
            // A page with no room at all still prints something recognisable.
            // A tiny photograph reads as a mistake; one slightly too big reads
            // as a crowded front page, which is what a front page is.
            const float columnWidth = 1000f;

            Check.Close(
                columnWidth * NewsstandPage.FloorFraction,
                NewsstandPage.PhotoSide(columnWidth, 1080f, 1080f * 0.90f),
                0.01, "the floor holds");
        });
    }

    private static void Dots(TestRunner runner)
    {
        runner.Group("Newsstand: the halftone");

        runner.Add("ink is the inverse of light", () =>
        {
            Check.False(Halftone.DotFor(1f, 10f).Print, "white paper stays paper");
            Check.True(Halftone.DotFor(0f, 10f).Print, "black is solid ink");

            var pale = Halftone.DotFor(0.8f, 10f);
            var dark = Halftone.DotFor(0.2f, 10f);

            Check.True(dark.Diameter > pale.Diameter, "darker cells print bigger dots");
        });

        runner.Add("the darkest dots overlap their neighbours", () =>
        {
            // Not a rounding error. A dot confined to its own cell can never
            // reach solid black, because the corners between four dots stay
            // white. Overlapping is how a halftone fills in.
            var solid = Halftone.DotFor(0f, 10f);

            Check.Close(12.0, solid.Diameter, 0.0001, "a fifth wider than its cell");
            Check.True(solid.Diameter > 10f, "and therefore into the next cell");
        });

        runner.Add("the palest printed areas still show a speck", () =>
        {
            // The floor. Without it a pale sky fades to nothing and reads as a
            // hole in the page rather than as lightly printed.
            var faintest = Halftone.DotFor(0.94f, 10f);

            Check.True(faintest.Print, "just dark enough to print");
            Check.True(faintest.Diameter > 2.5f, $"and visible at {faintest.Diameter:0.0} of a 10 point cell");
        });

        runner.Add("nearly white is left as paper", () =>
        {
            Check.False(Halftone.DotFor(0.96f, 10f).Print, "above the threshold");
            Check.True(Halftone.DotFor(0.94f, 10f).Print, "and below it");
        });

        runner.Add("row zero is the top row, with no flip", () =>
        {
            // The whole point of this test. The Mac has to map row r to n-1-r
            // because its drawing space runs upward while a bitmap's rows run
            // downward; the specification records that as a real bug it once
            // had. On Windows both run downward, so porting that flip across
            // would reintroduce the bug in mirror image.
            //
            // If this test ever fails, somebody has added the flip back and the
            // photograph is now upside down.
            var first = Halftone.CellOrigin(0, 0, 10f, 10f);
            var last = Halftone.CellOrigin(Halftone.Grid - 1, 0, 10f, 10f);

            Check.Close(0.0, first.Y, 0.0001, "row zero is at the top");
            Check.True(last.Y > first.Y, "and the last row is below it");
        });

        runner.Add("a dot sits centred in its own cell", () =>
        {
            var small = Halftone.CellOrigin(3, 5, 10f, 4f);

            Check.Close(53.0, small.X, 0.0001, "inset horizontally");
            Check.Close(33.0, small.Y, 0.0001, "and vertically");
        });

        runner.Add("an oversized dot hangs outside its cell evenly", () =>
        {
            var solid = Halftone.CellOrigin(2, 2, 10f, 12f);

            Check.Close(19.0, solid.X, 0.0001, "a point over on each side");
            Check.Close(19.0, solid.Y, 0.0001, "in both directions");
        });

        runner.Add("brightness is Rec.709 and deliberately not gamma corrected", () =>
        {
            // Skipping the gamma step is what gives the dots their contrast. It
            // is wrong as colour science and right as printing, and the
            // specification says in as many words not to fix it.
            Check.Close(0.0, Halftone.Luma(0, 0, 0), 0.0001, "black");
            Check.Close(1.0, Halftone.Luma(255, 255, 255), 0.0001, "white");
            Check.Close(0.7152, Halftone.Luma(0, 255, 0), 0.0001, "green carries most of it");
            Check.Close(0.0722, Halftone.Luma(0, 0, 255), 0.0001, "and blue almost none");

            // Mid grey lands at exactly half, which a gamma-corrected version
            // would not: it would come out near 0.21.
            Check.Close(0.5, Halftone.Luma(128, 128, 128), 0.002, "mid grey is mid");
        });

        runner.Add("the whole grid fits the photograph exactly", () =>
        {
            const float photo = 460f;
            var cell = photo / Halftone.Grid;

            var lastOrigin = Halftone.CellOrigin(Halftone.Grid - 1, Halftone.Grid - 1, cell, cell);

            Check.Close(photo - cell, lastOrigin.X, 0.0001, "the last column ends at the edge");
            Check.Close(photo - cell, lastOrigin.Y, 0.0001, "and so does the last row");
        });
    }
}

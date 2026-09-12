namespace AlbumCoverScreenSaver.Shared.Tests;

/// <summary>
/// Turning a cover into printed dots.
/// </summary>
public static class HalftoneTests
{
    public static void Register(TestRunner runner)
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

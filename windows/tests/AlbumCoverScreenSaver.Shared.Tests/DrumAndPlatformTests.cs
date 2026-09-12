namespace AlbumCoverScreenSaver.Shared.Tests;

/// <summary>
/// Zoetrope's drum, and Subway Platform's station.
/// </summary>
public static class DrumAndPlatformTests
{
    public static void Register(TestRunner runner)
    {
        Drum(runner);
        Station(runner);
    }

    private static void Drum(TestRunner runner)
    {
        runner.Group("Zoetrope: the drum");

        runner.Add("a revolution takes about fourteen seconds whatever the card count", () =>
        {
            // The pace is a property of the drum, not of how much is in it. A
            // fuller drum that also spun slower would feel like it had jammed.
            Check.Close(13.96, ZoetropeDrum.Revolution, 0.01, "one turn");

            foreach (var count in new[] { 6, 14, 30 })
            {
                var spun = ZoetropeDrum.AngleOf(0, count, ZoetropeDrum.Spin(ZoetropeDrum.Revolution))
                           - ZoetropeDrum.AngleOf(0, count, ZoetropeDrum.Spin(0));

                Check.Close(-Math.PI * 2, spun, 0.001, $"{count} cards");
            }
        });

        runner.Add("the near wall travels left to right", () =>
        {
            // Which is the direction a zoetrope is always drawn turning. The
            // sign of the spin is the whole of it.
            Check.True(ZoetropeDrum.Spin(1) < 0, "the drum turns clockwise");
        });

        runner.Add("cards are evenly spaced round the drum", () =>
        {
            const int count = 12;
            var spin = ZoetropeDrum.Spin(3.3);

            for (var k = 1; k < count; k++)
            {
                var gap = ZoetropeDrum.AngleOf(k, count, spin) - ZoetropeDrum.AngleOf(k - 1, count, spin);
                Check.Close(Math.PI * 2 / count, gap, 0.0001, $"between cards {k - 1} and {k}");
            }
        });

        runner.Add("slits sit between the cards, not on them", () =>
        {
            // Half a step round. A slit lined up with a card would hide the one
            // thing the style exists to show.
            const int count = 12;
            var spin = ZoetropeDrum.Spin(0);

            var card = ZoetropeDrum.AngleOf(0, count, spin);
            var slit = ZoetropeDrum.SlitAngleOf(0, count, spin);

            Check.Close(Math.PI / count, slit - card, 0.0001, "exactly half a step");
        });

        runner.Add("a card at the back is smaller and darker than one at the front", () =>
        {
            var back = ZoetropeDrum.DepthOf(Math.PI / 2);
            var front = ZoetropeDrum.DepthOf(-Math.PI / 2);

            Check.Close(1.0, back, 0.0001, "the far wall");
            Check.Close(0.0, front, 0.0001, "the near wall");

            Check.Close(73.0, ZoetropeDrum.CardWidth(100f, back), 0.01, "far cards");
            Check.Close(115.0, ZoetropeDrum.CardWidth(100f, front), 0.01, "near cards");

            Check.Close(0.60, ZoetropeDrum.ShadeOf(back), 0.0001, "the far wall is well knocked back");
            Check.Close(0.0, ZoetropeDrum.ShadeOf(front), 0.0001, "and the near wall is not");
        });

        runner.Add("a slit foreshortens to a hairline and never vanishes", () =>
        {
            // A bar turning about the drum's axis goes edge-on twice a
            // revolution. Letting it reach zero makes it blink out.
            //
            // Which end is which is worth stating, because it is the opposite
            // of the first guess: the widest slits are at the left and right
            // extremes of the drum and the narrowest are at the front centre.
            // See the note on SlitWidth for why that follows from the
            // specification and what it implies about how the slits are built.
            var widest = ZoetropeDrum.SlitWidth(400f, 0);
            var narrowest = ZoetropeDrum.SlitWidth(400f, -Math.PI / 2);

            Check.Close(22.0, widest, 0.01, "at the extremes of the drum");
            Check.Close(2.0, narrowest, 0.0001, "at the front centre, floored");
            Check.True(narrowest > 0, "and never actually zero");
        });

        runner.Add("only the near wall's slits are drawn", () =>
        {
            Check.True(ZoetropeDrum.IsNearWall(-Math.PI / 2), "the front");
            Check.False(ZoetropeDrum.IsNearWall(Math.PI / 2), "the back");

            // The cutoff is a shade past the side rather than exactly at it, so
            // a slit does not flicker in and out where it is a hairline anyway.
            Check.True(ZoetropeDrum.IsNearWall(0), "and the side counts as near");
        });

        runner.Add("the card count is clamped", () =>
        {
            Check.Equal(14, ZoetropeDrum.CardCount(14), "the default");
            Check.Equal(6, ZoetropeDrum.CardCount(1), "never bare");
            Check.Equal(30, ZoetropeDrum.CardCount(300), "never solid");
        });
    }

    private static void Station(TestRunner runner)
    {
        runner.Group("Subway Platform: the station");

        runner.Add("a train goes through every twenty six seconds and is gone for most of them", () =>
        {
            // The waiting is the point. A train every couple of seconds is a
            // train set; one every twenty six is a station.
            Check.True(SubwayPlatform.TrainIsVisible(0), "one arrives at the start");
            Check.True(SubwayPlatform.TrainIsVisible(8), "still going through at eight seconds");
            Check.False(SubwayPlatform.TrainIsVisible(12), "gone by twelve");
            Check.False(SubwayPlatform.TrainIsVisible(25), "and still gone at twenty five");
            Check.True(SubwayPlatform.TrainIsVisible(26.1), "the next one arrives on time");
        });

        runner.Add("the train enters fully off one side and leaves fully off the other", () =>
        {
            var entering = SubwayPlatform.TrainLeft(0);
            var leaving = SubwayPlatform.TrainLeft(SubwayPlatform.TrainCycle * SubwayPlatform.TrainOnScreen * 0.999);

            Check.Close(-1.1, entering, 0.001, "starts a screen and a bit off the left");
            Check.True(leaving > 1.2, $"ends at {leaving:0.00} screens, which is not clear of the right");
        });

        runner.Add("the train never jumps backwards mid-pass", () =>
        {
            var previous = float.NegativeInfinity;

            for (var frame = 0; frame < 30 * 9; frame++)
            {
                var phase = frame / 30.0;
                if (!SubwayPlatform.TrainIsVisible(phase)) break;

                var left = SubwayPlatform.TrainLeft(phase);
                Check.True(left >= previous, $"went backwards at {phase:0.00}s");
                previous = left;
            }
        });

        runner.Add("tiles vary but stay a warm off-white", () =>
        {
            for (var row = 0; row < 20; row++)
            {
                for (var x = 0; x < 2000; x += 37)
                {
                    var shade = SubwayPlatform.TileShade(row, x);
                    Check.True(shade is >= 0.86f and <= 0.96f, $"tile at row {row}, x {x} is {shade}");
                }
            }
        });

        runner.Add("the wall has no vertical banding", () =>
        {
            // The hash is seeded from the tile's position in points rather than
            // its column number, so with a fractional tile size a column does
            // not repeat down the wall. That is what stops the eye finding
            // stripes in it.
            const float tile = 81.7f;
            var shades = new List<float>();

            for (var row = 0; row < 12; row++)
            {
                var x = row % 2 == 0 ? 0f : -tile / 2f;
                shades.Add(SubwayPlatform.TileShade(row, x + (5 * tile)));
            }

            Check.True(shades.Distinct().Count() > 8, "one column of tiles is too repetitive");
        });

        runner.Add("the same tile is the same shade every frame", () =>
        {
            // Otherwise the wall shimmers, which is the one thing a tiled wall
            // must never do.
            Check.Equal(
                SubwayPlatform.TileShade(4, 327f), SubwayPlatform.TileShade(4, 327f), "the same tile twice");
        });

        runner.Add("posters fit the platform and one of them is lit", () =>
        {
            foreach (var width in new[] { 1280f, 1920f, 3440f })
            {
                var posterWidth = Math.Min(width * 0.19f, 1080f * 0.30f);
                var slots = SubwayPlatform.PosterSlots(width, posterWidth);

                Check.True(slots >= 2, $"only {slots} posters at {width:0} wide");

                var lit = SubwayPlatform.LitSlot(slots);
                Check.True(lit >= 0 && lit < slots, $"the lit slot {lit} is not one of the {slots}");
            }
        });

        runner.Add("posters are spread across the platform, not bunched", () =>
        {
            const float width = 1920f;
            const int slots = 5;

            var first = SubwayPlatform.PosterCentre(width, 0, slots);
            var last = SubwayPlatform.PosterCentre(width, slots - 1, slots);

            Check.Close(192.0, first, 0.01, "the first sits in from the left edge");
            Check.Close(1766.4, last, 0.01, "and the last in from the right");

            for (var i = 1; i < slots; i++)
            {
                Check.True(
                    SubwayPlatform.PosterCentre(width, i, slots) >
                    SubwayPlatform.PosterCentre(width, i - 1, slots),
                    $"poster {i} is not right of {i - 1}");
            }
        });

        runner.Add("two posters do not land on top of each other", () =>
        {
            const float width = 1920f;
            var posterWidth = Math.Min(width * 0.19f, 1080f * 0.30f);
            var slots = SubwayPlatform.PosterSlots(width, posterWidth);

            var gap = SubwayPlatform.PosterCentre(width, 1, slots)
                      - SubwayPlatform.PosterCentre(width, 0, slots);

            // The lit one is a fifth wider than the rest, so the gap has to
            // clear that rather than merely the nominal width.
            Check.True(gap > posterWidth * 1.22f, $"a gap of {gap:0} against a lit poster of {posterWidth * 1.22f:0}");
        });
    }
}

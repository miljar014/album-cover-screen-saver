namespace AlbumCoverScreenSaver.Shared.Tests;

/// <summary>
/// Crate Digging's perspective, and Vaporwave Grid's moving floor.
/// </summary>
public static class SceneMathTests
{
    public static void Register(TestRunner runner)
    {
        Crate(runner);
        Vaporwave(runner);
    }

    private static void Crate(TestRunner runner)
    {
        runner.Group("Crate Digging: the depth cues");

        runner.Add("sleeves overlap rather than standing in a neat row", () =>
        {
            // The spacing comes off a fractionally smaller reference than the
            // sleeve, which is the whole reason they lean on each other the way
            // records in a crate do.
            var side = CrateMath.Side(1920f, 1080f);
            var spacing = CrateMath.Spacing(1920f, 1080f);

            Check.True(spacing < side, "a sleeve is wider than the gap to the next");
            Check.True(spacing < side * 0.2f, $"and by a lot: {spacing:0} against {side:0}");
        });

        runner.Add("all four depth cues move together", () =>
        {
            var front = CrateMath.DepthAt(0);
            var back = CrateMath.DepthAt(12);

            Check.True(CrateMath.ScaleOf(back) < CrateMath.ScaleOf(front), "further back is smaller");
            Check.True(CrateMath.HazeOf(back) > CrateMath.HazeOf(front), "further back is hazier");
            Check.True(CrateMath.RiseOf(back) > CrateMath.RiseOf(front), "further back sits higher");
            Check.True(CrateMath.LeanOf(back) < CrateMath.LeanOf(front), "further back leans more");
        });

        runner.Add("every sleeve leans the same way", () =>
        {
            // A crate where some records tip left and others right looks like a
            // bug rather than like a crate.
            for (var position = 0; position < 40; position++)
            {
                Check.True(CrateMath.LeanOf(CrateMath.DepthAt(position)) < 0, $"sleeve {position}");
            }
        });

        runner.Add("depth stops changing past the thirteenth sleeve", () =>
        {
            Check.Close(1.0, CrateMath.DepthAt(12), 0.0001, "saturated");
            Check.Close(1.0, CrateMath.DepthAt(39), 0.0001, "and stays there");

            // Invisible, because by then the haze is at two thirds.
            Check.Close(0.65, CrateMath.HazeOf(CrateMath.DepthAt(12)), 0.0001, "the haze at the back");
        });

        runner.Add("the back of the crate never goes fully black", () =>
        {
            // At 0.65 the covers are still just legible, which is what makes the
            // crate look deep rather than look like it ends.
            Check.True(CrateMath.HazeOf(1f) < 0.7f, "something is still visible back there");
            Check.True(CrateMath.HazeOf(0f) > 0.15f, "and even the front is knocked back a little");
        });

        runner.Add("the crate slides one sleeve per dwell", () =>
        {
            Check.Close(5.0, CrateMath.Dwell(30), 0.0001, "the default");
            Check.Close(3.0, CrateMath.Dwell(6), 0.0001, "floored so it cannot blur");
            Check.Close(3.0, CrateMath.Dwell(0), 0.0001, "and a zero cannot stop it");
        });

        runner.Add("two extra sleeves are always built, so the row never runs out", () =>
        {
            Check.Equal(18, CrateMath.Want(16), "the default");
            Check.Equal(6, CrateMath.Want(1), "the floor, plus its two");
            Check.Equal(42, CrateMath.Want(400), "the ceiling, plus its two");

            foreach (var asked in new[] { 4, 16, 40 })
            {
                Check.True(CrateMath.Want(asked) > asked, $"{asked} asked for, more built");
            }
        });

        runner.Add("live inserts cannot grow the crate without limit", () =>
        {
            // Every track change pushes one in at the front. Without the cap an
            // evening's listening is a thousand sleeves.
            Check.Equal(18, CrateMath.RetentionCap(16), "the default");
            Check.Equal(6, CrateMath.RetentionCap(0), "and never fewer than six");
        });
    }

    private static void Vaporwave(TestRunner runner)
    {
        runner.Group("Vaporwave Grid: the moving floor");

        runner.Add("the grid advances exactly one rung per second", () =>
        {
            // Not approximately. The cycle is the fractional part of the clock,
            // so after one second every rung is where the one behind it was.
            Check.Close(0.0, VaporwaveGrid.Cycle(0), 0.0001, "at the start");
            Check.Close(0.5, VaporwaveGrid.Cycle(10.5), 0.0001, "halfway through a second");
            Check.Close(0.0, VaporwaveGrid.Cycle(11.0), 0.0001, "and back to the start");
        });

        runner.Add("the pattern is seamless across the wrap", () =>
        {
            // The test that matters, and it has to be stated carefully. Over one
            // second rung k travels from where it starts to exactly where rung
            // k+1 started. So at the instant before the wrap, rung k must be
            // sitting where rung k+1 sits at the instant after it. If that is
            // not true there is a visible hitch once a second, forever.
            for (var k = 0; k < VaporwaveGrid.Rungs - 1; k++)
            {
                var justBefore = VaporwaveGrid.RungAt(k, 0.9999f);
                var justAfter = VaporwaveGrid.RungAt(k + 1, 0f);

                Check.Close(justAfter.DownFromHorizon, justBefore.DownFromHorizon, 0.0005,
                    $"rung {k} across the wrap");
                Check.Close(justAfter.Alpha, justBefore.Alpha, 0.0005, "and its brightness");
            }
        });

        runner.Add("rungs bunch at the horizon and spread as they arrive", () =>
        {
            // The square is what makes it a perspective grid rather than a
            // ladder: evenly spaced rungs read as a fire escape.
            var nearHorizon = VaporwaveGrid.RungAt(1, 0).DownFromHorizon
                              - VaporwaveGrid.RungAt(0, 0).DownFromHorizon;

            var nearViewer = VaporwaveGrid.RungAt(21, 0).DownFromHorizon
                             - VaporwaveGrid.RungAt(20, 0).DownFromHorizon;

            Check.True(nearViewer > nearHorizon * 10,
                $"{nearViewer:0.0000} against {nearHorizon:0.0000} is not enough foreshortening");
        });

        runner.Add("the grid spans exactly the horizon to the bottom edge", () =>
        {
            Check.Close(0.0, VaporwaveGrid.RungAt(0, 0).DownFromHorizon, 0.0001, "starts at the horizon");
            Check.Close(1.0, VaporwaveGrid.RungAt(VaporwaveGrid.Rungs, 0).DownFromHorizon, 0.0001,
                "and the next one off the end is the bottom edge");
        });

        runner.Add("rungs fade out as they reach the viewer", () =>
        {
            var previous = 1f;

            for (var k = 0; k < VaporwaveGrid.Rungs; k++)
            {
                var alpha = VaporwaveGrid.RungAt(k, 0).Alpha;

                Check.True(alpha <= previous + 0.0001f, $"rung {k} got brighter");
                Check.True(alpha >= 0f, $"rung {k} went negative");
                previous = alpha;
            }

            Check.Close(0.75, VaporwaveGrid.RungAt(0, 0).Alpha, 0.0001, "brightest at the horizon");
        });

        runner.Add("the rays fan out fifteen to one", () =>
        {
            var (horizon, bottom) = VaporwaveGrid.Ray(1);

            Check.Close(0.02, horizon, 0.0001, "tight at the vanishing point");
            Check.Close(0.30, bottom, 0.0001, "wide at the viewer");
            Check.Close(15.0, bottom / horizon, 0.0001, "a constant ratio");
        });

        runner.Add("only a handful of rays are still on screen at the bottom", () =>
        {
            // The rest have run off the sides, which is what gives the ground
            // its apparent width.
            var onScreen = 0;
            for (var i = -VaporwaveGrid.Rays; i <= VaporwaveGrid.Rays; i++)
            {
                if (Math.Abs(VaporwaveGrid.Ray(i).AtBottom) <= 0.5f) onScreen++;
            }

            Check.True(onScreen is > 1 and < 8, $"{onScreen} rays reach the bottom edge");
        });

        runner.Add("the sun dissolves toward its base rather than being striped", () =>
        {
            var slots = VaporwaveGrid.SunSlots(200f).ToList();

            // The specification's prose guesses at "typically 5 to 7 bands", but
            // its own formula gives eleven at this radius, and the formula is
            // the part that is stated precisely. The first band is two points
            // wide in absolute terms rather than a fraction of the sun, so the
            // count grows with the screen: the estimate was probably written
            // against a smaller one. Following the formula.
            Check.True(slots.Count is >= 6 and <= 16, $"{slots.Count} bands across the sun");

            for (var i = 1; i < slots.Count; i++)
            {
                Check.True(slots[i].Band > slots[i - 1].Band, $"band {i} is no wider than the one above");
                Check.True(slots[i].Down > slots[i - 1].Down, $"band {i} is not below the one above");
            }

            // A quarter wider each time, which is the whole look.
            Check.Close(1.25, slots[1].Band / slots[0].Band, 0.0001, "the growth rate");
        });

        runner.Add("the slots stay inside the sun", () =>
        {
            foreach (var radius in new[] { 80f, 200f, 600f })
            {
                foreach (var (down, _) in VaporwaveGrid.SunSlots(radius))
                {
                    Check.True(down < radius, $"a band at {down:0} fell outside a sun of {radius:0}");
                }
            }
        });
    }
}

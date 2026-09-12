namespace AlbumCoverScreenSaver.Shared.Tests;

/// <summary>
/// The arithmetic behind Slow-Building Wall, Hero + Grid and Drifting Float.
/// </summary>
/// <remarks>
/// None of these three styles can be checked by looking at them. A wall that
/// dissolves in the wrong order, a hero that leans on the wrong slice of the
/// archive, a turn that sweeps the long way round: each of those looks perfectly
/// plausible for the several seconds a person watches before deciding it is
/// fine. So the arithmetic is pulled into the shared library and pinned down by
/// number here.
/// </remarks>
public static class StyleMathTests
{
    public static void Register(TestRunner runner)
    {
        Wall(runner);
        Hero(runner);
        Angles(runner);
        Turning(runner);
        Spawning(runner);
        Blur(runner);
        Drift(runner);
    }

    // --- Slow-Building Wall -------------------------------------------------

    private static void Wall(TestRunner runner)
    {
        runner.Group("Slow-Building Wall: the cycle");

        runner.Add("the defaults give a 12 second fill and a 25.2 second cycle", () =>
        {
            // The specification's own worked example.
            var step = WallCycle.Step(tempo: 4.0, tileCount: 60, buildSpeed: 1.0);
            var fill = WallCycle.FillTime(step, 60);
            var hold = WallCycle.HoldTime(6.0);

            Check.Close(12.0, fill, 0.0001, "fill time");
            Check.Close(6.0, hold, 0.0001, "hold time");
            Check.Close(25.2, WallCycle.CycleTime(fill, hold), 0.0001, "cycle time");
        });

        runner.Add("a denser grid fills in the same time, not proportionally longer", () =>
        {
            var small = WallCycle.FillTime(WallCycle.Step(4.0, 40, 1.0), 40);
            var large = WallCycle.FillTime(WallCycle.Step(4.0, 400, 1.0), 400);

            Check.Close(small, large, 0.0001, "fill time is independent of tile count");
        });

        runner.Add("the step floor caps a very dense grid at fifty tiles a second", () =>
        {
            // Unfloored this is 0.0024 seconds, so seven tiles land in a single
            // frame and the wall appears to snap on rather than build.
            Check.Close(0.02, WallCycle.Step(4.0, 5000, 1.0), 0.0001, "step floor");
        });

        runner.Add("build speed divides the fill, and a zero cannot stall it", () =>
        {
            Check.Close(0.1, WallCycle.Step(4.0, 60, 2.0), 0.0001, "twice as fast");
            Check.Close(0.2 / 0.15, WallCycle.Step(4.0, 60, 0.0), 0.0001, "zero is floored at 0.15");
        });

        runner.Add("a tile is invisible before its turn and solid 0.6s after it", () =>
        {
            var step = WallCycle.Step(4.0, 60, 1.0);
            var fill = WallCycle.FillTime(step, 60);

            float At(double elapsed) => WallCycle.AlphaAt(elapsed, 2.0, step, 60, fill, 6.0);

            Check.Close(0f, At(1.9), 0.0001, "before its turn");
            Check.Close(0f, At(2.0), 0.0001, "at its turn");
            Check.Close(0.5f, At(2.3), 0.0001, "halfway through the fade");
            Check.Close(1f, At(2.6), 0.0001, "faded in");
            Check.Close(1f, At(9.0), 0.0001, "still up during the fill");
            Check.Close(1f, At(17.0), 0.0001, "still up during the hold");
        });

        runner.Add("tiles leave in the order they arrived", () =>
        {
            const int tiles = 60;
            var step = WallCycle.Step(4.0, tiles, 1.0);
            var fill = WallCycle.FillTime(step, tiles);
            const double hold = 6.0;
            var dissolveStart = fill + hold;

            var first = WallCycle.AlphaAt(dissolveStart + 0.5, 0 * step, step, tiles, fill, hold);
            var middle = WallCycle.AlphaAt(dissolveStart + 0.5, 30 * step, step, tiles, fill, hold);
            var last = WallCycle.AlphaAt(dissolveStart + 0.5, 59 * step, step, tiles, fill, hold);

            Check.True(first < middle, "the first tile in is the first out");
            Check.Close(1f, middle, 0.0001, "the middle is untouched this early");
            Check.Close(1f, last, 0.0001, "the last is untouched this early");
        });

        runner.Add("about three tiles are mid-dissolve at any moment", () =>
        {
            // One alone reads as a cursor deleting text; a dozen reads as the
            // wall simply being switched off.
            const int tiles = 60;
            var step = WallCycle.Step(4.0, tiles, 1.0);
            var fill = WallCycle.FillTime(step, tiles);
            const double hold = 6.0;

            var elapsed = fill + hold + (fill * 0.6 * 0.5);
            var partial = 0;

            for (var i = 0; i < tiles; i++)
            {
                var alpha = WallCycle.AlphaAt(elapsed, i * step, step, tiles, fill, hold);
                if (alpha > 0.001f && alpha < 0.999f) partial++;
            }

            Check.True(partial is >= 2 and <= 4, $"{partial} tiles mid-dissolve, expected about three");
        });

        runner.Add("the dissolve never brightens a tile that is still fading in", () =>
        {
            // A pathological setting on purpose: almost no hold, so the front
            // catches tiles that have only just landed.
            const int tiles = 8;
            var step = WallCycle.Step(0.5, tiles, 4.0);
            var fill = WallCycle.FillTime(step, tiles);
            var hold = WallCycle.HoldTime(0.0);

            var previous = new float[tiles];
            var seeded = false;
            var rising = new bool[tiles];

            for (var frame = 0; frame < 2000; frame++)
            {
                var elapsed = frame / 30.0;
                if (elapsed <= fill + hold) continue;

                for (var i = 0; i < tiles; i++)
                {
                    var alpha = WallCycle.AlphaAt(elapsed, i * step, step, tiles, fill, hold);

                    // Seeded from the first dissolve frame rather than from
                    // zero, or every tile "rises" once on the way in and the
                    // test reports a fault that is its own.
                    if (seeded && alpha > previous[i] + 0.0001f) rising[i] = true;
                    previous[i] = alpha;
                }

                seeded = true;
            }

            Check.True(rising.All(r => !r), "no tile brightens once the dissolve has begun");
        });

        runner.Add("a tile grows from 94% of its cell to 100%", () =>
        {
            Check.Close(0.94f, WallCycle.GrowAt(1.0, 2.0), 0.0001, "before its turn");
            Check.Close(0.94f, WallCycle.GrowAt(2.0, 2.0), 0.0001, "at its turn");
            Check.Close(1.0f, WallCycle.GrowAt(2.6, 2.0), 0.0001, "settled");
            Check.Close(1.0f, WallCycle.GrowAt(9.0, 2.0), 0.0001, "stays settled");
        });
    }

    // --- Hero + Grid --------------------------------------------------------

    private static void Hero(TestRunner runner)
    {
        runner.Group("Hero + Grid: the featured cover");

        runner.Add("on a widescreen display the height term decides the size", () =>
        {
            // 1920x1080 at the default 0.56: height gives 604.8, width 806.4.
            Check.Close(604.8f, HeroLayout.Side(1920f, 1080f, 0.56), 0.01, "16:9");
        });

        runner.Add("on a portrait display the width term stops it overflowing", () =>
        {
            // 800x1400: height gives 784, width gives 336. Without the width
            // term the cover would be nearly as wide as the screen.
            Check.Close(336f, HeroLayout.Side(800f, 1400f, 0.56), 0.01, "portrait");
        });

        runner.Add("the size setting is clamped at both ends", () =>
        {
            Check.Close(1080f * 0.2f, HeroLayout.Side(1920f, 1080f, 0.0), 0.01, "below the floor");
            Check.Close(1080f * 0.9f, HeroLayout.Side(1920f, 1080f, 5.0), 0.01, "above the ceiling");
        });

        runner.Add("the idle rotation draws from the newest eighth of the archive", () =>
        {
            // The hero is the headline, so it leans on recency harder than the
            // grid behind it, which uses the picker's 20% head.
            Check.Equal(100, HeroLayout.IdleRotationBound(800), "800 albums");
            Check.Equal(5, HeroLayout.IdleRotationBound(40), "40 albums");
            Check.Equal(3, HeroLayout.IdleRotationBound(16), "the floor of three holds");
        });

        runner.Add("a tiny archive falls back to a plain pick", () =>
        {
            Check.False(HeroLayout.RotationIsNarrowed(3), "three albums is not narrowed");
            Check.True(HeroLayout.RotationIsNarrowed(4), "four albums is");
            Check.Equal(2, HeroLayout.IdleRotationBound(2), "the bound is the whole archive");
        });

        runner.Add("the swap interval is floored at two seconds", () =>
        {
            Check.Close(12.0, HeroLayout.Interval(12.0), 0.0001, "the default");
            Check.Close(2.0, HeroLayout.Interval(0.0), 0.0001, "zero cannot thrash");
        });
    }

    // --- the wind -----------------------------------------------------------

    private static void Angles(TestRunner runner)
    {
        runner.Group("Drifting Float: the wind");

        runner.Add("a turn from 350 to 10 degrees goes the short way", () =>
        {
            var from = 350.0 * Math.PI / 180.0;
            var to = 10.0 * Math.PI / 180.0;

            var half = Wind.Lerp(from, to, 0.5);
            var degrees = ((half * 180.0 / Math.PI % 360.0) + 360.0) % 360.0;

            // Halfway is 0 degrees. The long way round would put it at 180, and
            // on screen that is the whole flock wheeling backwards for five
            // seconds.
            Check.True(degrees < 1.0 || degrees > 359.0, $"halfway is {degrees:0.0} degrees, expected 0");
        });

        runner.Add("the ends of an interpolation are the ends", () =>
        {
            Check.Close(1.0, Wind.Lerp(1.0, 2.0, 0.0), 0.0001, "t = 0");
            Check.Close(0.0, Wind.Distance(Wind.Lerp(1.0, 2.0, 1.0), 2.0), 0.0001, "t = 1");
        });

        runner.Add("angular distance never exceeds half a turn", () =>
        {
            var random = new Random(5150);
            for (var i = 0; i < 500; i++)
            {
                var a = random.NextDouble() * Wind.Tau;
                var b = random.NextDouble() * Wind.Tau;
                var d = Wind.Distance(a, b);
                Check.True(d is >= 0 and <= Math.PI + 0.0001, $"distance {d} out of range");
            }
        });

        runner.Add("a new direction is always meaningfully different", () =>
        {
            var random = new Random(31337);
            var current = 1.0;
            var small = 0;

            for (var i = 0; i < 400; i++)
            {
                var next = Wind.PickNewDirection(random, current);
                if (Wind.Distance(next, current) <= 0.6) small++;
                current = next;
            }

            // Twelve tries at about an 81% chance each, so genuine failures are
            // vanishingly rare. More than a couple here means the rule is gone.
            Check.True(small <= 2, $"{small} turns of under 34 degrees in 400");
        });

        runner.Add("a full reversal is allowed, not filtered out", () =>
        {
            var random = new Random(9001);
            var reversals = 0;

            for (var i = 0; i < 400; i++)
            {
                if (Wind.Distance(Wind.PickNewDirection(random, 0.0), 0.0) > 2.8) reversals++;
            }

            Check.True(reversals > 20, $"only {reversals} near-reversals in 400, expected many");
        });
    }

    private static void Turning(TestRunner runner)
    {
        runner.Group("Drifting Float: how each cover turns");

        runner.Add("a heavy cover turns early and quickly, a light one late and slowly", () =>
        {
            var heavy = Wind.TurnOf(0.5, agility: 1.0);
            var light = Wind.TurnOf(0.5, agility: 0.0);

            Check.True(heavy.Local > light.Local,
                $"at the midpoint the heavy cover is at {heavy.Local:0.00} and the light one at {light.Local:0.00}");
            Check.Close(1.0f, Wind.TurnOf(0.5, 1.0).Local, 0.0001, "the heavy one is finished by the midpoint");
        });

        runner.Add("the lightest cover comes to a complete stop mid-turn", () =>
        {
            // agility 0: begins at 0.30, spans 0.80, so its own halfway point is
            // at u = 0.70.
            var (local, pace) = Wind.TurnOf(0.70, agility: 0.0);

            Check.Close(0.5f, local, 0.0001, "halfway through its own turn");
            Check.Close(0.0, pace, 0.0001, "stopped");
        });

        runner.Add("the heaviest cover barely slows", () =>
        {
            var slowest = 1.0;
            for (var step = 0; step <= 100; step++)
            {
                slowest = Math.Min(slowest, Wind.TurnOf(step / 100.0, agility: 1.0).Pace);
            }

            Check.Close(0.82, slowest, 0.0001, "the floor for an agile cover");
        });

        runner.Add("pace approaches a standstill smoothly, never as a step", () =>
        {
            // The dip is a raised cosine rather than a step, so there is no
            // moment of a cover being parked.
            var previous = 1.0;
            for (var step = 1; step <= 200; step++)
            {
                var pace = Wind.TurnOf(step / 200.0, agility: 0.0).Pace;
                Check.True(Math.Abs(pace - previous) < 0.06,
                    $"pace jumped from {previous:0.000} to {pace:0.000}");
                previous = pace;
            }
        });

        runner.Add("the laggard is fractionally short when the window closes", () =>
        {
            // Documented, invisible, and deliberately preserved. At u = 1 the
            // least agile cover has turned about 99.2% of the way and the
            // remainder is assigned outright. This is a test rather than a
            // comment because the temptation to tidy it away is exactly what
            // would change every other timing in the style.
            var local = Wind.TurnOf(1.0, agility: 0.0).Local;
            var reached = Ease.InOut(local);

            Check.True(reached is > 0.985f and < 1f, $"reached {reached:0.0000} of the turn");
        });

        runner.Add("every cover starts at full pace, and only the laggard is still slow at the end", () =>
        {
            foreach (var agility in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
            {
                Check.Close(1.0, Wind.TurnOf(0.0, agility).Pace, 0.0001, $"agility {agility} at the start");
            }

            // The agile ones are done well before the window closes.
            Check.Close(1.0, Wind.TurnOf(1.0, 1.0).Pace, 0.0001, "the heaviest cover at the end");
            Check.Close(1.0, Wind.TurnOf(1.0, 0.5).Pace, 0.0001, "a middling cover at the end");

            // The lightest is not, for the same reason its angle is fractionally
            // short: its window runs to u = 1.10 and is cut off at 1.0. It is
            // still at 85% of its pace when the turn is declared over and it
            // snaps back to full. Recorded here so the number is known rather
            // than discovered; it is one frame of a cover 12% of the screen
            // wide gaining about a pixel, and nobody has ever seen it.
            Check.Close(0.854, Wind.TurnOf(1.0, 0.0).Pace, 0.001, "the lightest cover at the end");
        });
    }

    private static void Spawning(TestRunner runner)
    {
        runner.Group("Drifting Float: where covers come in");

        runner.Add("a cover always spawns fully off screen, at any angle", () =>
        {
            const float width = 1920f;
            const float height = 1080f;
            const float side = 260f;
            var reach = DriftMath.Reach(width, height);

            for (var degrees = 0; degrees < 360; degrees += 5)
            {
                var angle = degrees * Math.PI / 180.0;

                foreach (var along in new[] { -1.0, -0.5, 0.0, 0.5, 1.0 })
                {
                    var (x, y) = Wind.SpawnUpwind(width, height, angle, side, along);
                    var distance = Math.Sqrt(
                        Math.Pow(x - (width / 2f), 2) + Math.Pow(y - (height / 2f), 2));

                    Check.True(distance >= reach,
                        $"spawned {distance:0} from centre at {degrees} degrees, inside the {reach:0} reach");
                }
            }
        });

        runner.Add("a cover spawns against the wind, not with it", () =>
        {
            // Blowing due east, so it has to enter from the west.
            var (x, _) = Wind.SpawnUpwind(1920f, 1080f, 0.0, 200f, 0.0);
            Check.True(x < 0, $"spawned at x = {x:0}, which is not upwind of due east");

            var (_, y) = Wind.SpawnUpwind(1920f, 1080f, Math.PI / 2.0, 200f, 0.0);
            Check.True(y < 0, $"spawned at y = {y:0}, which is not upwind");
        });
    }

    private static void Blur(TestRunner runner)
    {
        runner.Group("Drifting Float: the blurred backdrop");

        runner.Add("the blur radius matches the specification's worked values", () =>
        {
            Check.Equal(2, DriftMath.BlurRadius(0.0), "sharp");
            Check.Equal(10, DriftMath.BlurRadius(0.3), "the default");
            Check.Equal(28, DriftMath.BlurRadius(1.0), "a single wash of colour");
        });

        runner.Add("every blur radius is even, so the cache cannot fill with near-copies", () =>
        {
            // The radius is part of the cache key. A continuous slider would
            // otherwise store a separate blurred bitmap for every pixel of
            // difference, none of which the eye can tell apart.
            for (var step = 0; step <= 1000; step++)
            {
                var radius = DriftMath.BlurRadius(step / 1000.0);
                Check.True(radius % 2 == 0, $"radius {radius} at setting {step / 1000.0:0.000} is odd");
                Check.True(radius >= 2, $"radius {radius} is below the floor");
            }
        });

        runner.Add("the blur setting is clamped rather than extrapolated", () =>
        {
            Check.Equal(2, DriftMath.BlurRadius(-3.0), "below zero");
            Check.Equal(28, DriftMath.BlurRadius(9.0), "above one");
        });
    }

    private static void Drift(TestRunner runner)
    {
        runner.Group("Drifting Float: sizes and edges");

        runner.Add("a cover is solid inside the frame and gone one side beyond it", () =>
        {
            const float width = 1920f;
            const float height = 1080f;
            const float side = 200f;

            Check.Close(1f, DriftMath.EdgeFade(960f, 540f, width, height, side), 0.0001, "dead centre");
            Check.Close(1f, DriftMath.EdgeFade(10f, 540f, width, height, side), 0.0001, "near the left edge");
            Check.Close(1f, DriftMath.EdgeFade(1920f, 540f, width, height, side), 0.0001, "exactly on the edge");
            Check.Close(0.5f, DriftMath.EdgeFade(2020f, 540f, width, height, side), 0.0001, "half a side beyond");
            Check.Close(0f, DriftMath.EdgeFade(2120f, 540f, width, height, side), 0.0001, "a full side beyond");
        });

        runner.Add("the edge fade behaves the same on every edge", () =>
        {
            const float width = 1920f;
            const float height = 1080f;
            const float side = 200f;

            var left = DriftMath.EdgeFade(-100f, 540f, width, height, side);
            var right = DriftMath.EdgeFade(2020f, 540f, width, height, side);
            var top = DriftMath.EdgeFade(960f, -100f, width, height, side);
            var bottom = DriftMath.EdgeFade(960f, 1180f, width, height, side);

            // This is the one an x-only version got wrong: covers went
            // transparent halfway up the screen whenever the wind blew
            // vertically.
            Check.Close(left, right, 0.0001, "left matches right");
            Check.Close(top, bottom, 0.0001, "top matches bottom");
            Check.Close(left, top, 0.0001, "horizontal matches vertical");
        });

        runner.Add("leaving across a corner fades on the diagonal", () =>
        {
            var corner = DriftMath.EdgeFade(2020f, 1180f, 1920f, 1080f, 200f);
            var edge = DriftMath.EdgeFade(2020f, 540f, 1920f, 1080f, 200f);

            Check.True(corner < edge, "a corner exit is further out than an edge exit at the same overshoot");
            Check.Close(1f - (MathF.Sqrt(20000f) / 200f), corner, 0.001, "the Euclidean overshoot");
        });

        runner.Add("near covers are bigger and much faster than far ones", () =>
        {
            var far = DriftMath.SizeForDepth(1080f, 0.0);
            var near = DriftMath.SizeForDepth(1080f, 1.0);

            Check.Close(1080f * 0.12f, far.Side, 0.01, "the far size");
            Check.Close(1080f * 0.32f, near.Side, 0.01, "the near size");

            // The size band is narrow on purpose and the speed band is not:
            // parallax is what sells the depth, so a near cover has to visibly
            // outrun a far one by more than it outsizes it.
            var sizeRatio = near.Side / far.Side;
            var speedRatio = near.BaseSpeed / far.BaseSpeed;

            Check.True(speedRatio > sizeRatio * 2,
                $"speed varies {speedRatio:0.0}x against size {sizeRatio:0.0}x, not enough parallax");
        });

        runner.Add("distant covers sit back into the dark", () =>
        {
            Check.Close(0.55f, DriftMath.DepthAlpha(0.0), 0.0001, "far");
            Check.Close(1.0f, DriftMath.DepthAlpha(1.0), 0.0001, "near");
        });
    }
}

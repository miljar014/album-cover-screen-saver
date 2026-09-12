namespace AlbumCoverScreenSaver.Shared.Tests;

/// <summary>
/// The fake perspective of Cover Flow.
/// </summary>
/// <remarks>
/// There is no 3D anywhere in this style, only four numbers per card. Get one of
/// them slightly wrong and the result still looks like a carousel, just a subtly
/// unconvincing one, which is not something you can catch by watching. The
/// specification prints a table of expected values at each whole slot; that
/// table is the test.
/// </remarks>
public static class CoverFlowTests
{
    public static void Register(TestRunner runner)
    {
        Table(runner);
        Behaviour(runner);
        Timing(runner);
    }

    private static void Table(TestRunner runner)
    {
        runner.Group("Cover Flow: the projection table");

        // Straight from the specification. Distance, squash, scale, alpha, and
        // how far from centre the card sits in multiples of its own side.
        (float Distance, float Squash, float Scale, float Alpha, float Offset)[] expected =
        [
            (0f, 1.000f, 1.000f, 1.00f, 0f),
            (1f, 0.868f, 0.855f, 0.84f, 0.34f),
            (2f, 0.506f, 0.746f, 0.68f, 0.64f),
            (3f, 0.315f, 0.662f, 0.52f, 0.94f),
            (4f, 0.315f, 0.595f, 0.36f, 1.24f),
            (5f, 0.315f, 0.541f, 0.20f, 1.54f),
        ];

        runner.Add("every whole slot matches the published table", () =>
        {
            foreach (var row in expected)
            {
                var card = CoverFlowMath.Project(row.Distance);

                Check.Close(row.Squash, card.Squash, 0.002, $"squash at {row.Distance}");
                Check.Close(row.Scale, card.Scale, 0.002, $"scale at {row.Distance}");
                Check.Close(row.Alpha, card.Alpha, 0.002, $"alpha at {row.Distance}");
                Check.Close(row.Offset, card.OffsetX, 0.002, $"offset at {row.Distance}");
            }
        });

        runner.Add("the left half mirrors the right", () =>
        {
            for (var d = 1; d <= 5; d++)
            {
                var right = CoverFlowMath.Project(d);
                var left = CoverFlowMath.Project(-d);

                Check.Close(right.Squash, left.Squash, 0.0001, $"squash at {d}");
                Check.Close(right.Scale, left.Scale, 0.0001, $"scale at {d}");
                Check.Close(right.Alpha, left.Alpha, 0.0001, $"alpha at {d}");
                Check.Close(-right.OffsetX, left.OffsetX, 0.0001, $"offset at {d}");
                Check.Close(-right.Shear, left.Shear, 0.0001, $"shear at {d}");
            }
        });

        runner.Add("the turn stops at about 72 degrees", () =>
        {
            // Past that a cover stops reading as a cover, so the squash is
            // capped rather than being allowed to go to a hairline.
            Check.Close(
                CoverFlowMath.Project(3f).Squash, CoverFlowMath.Project(5f).Squash, 0.0001,
                "capped from three slots out");

            Check.True(CoverFlowMath.Project(5f).Squash > 0.3f, "and never becomes an edge");
        });
    }

    private static void Behaviour(TestRunner runner)
    {
        runner.Group("Cover Flow: what the projection guarantees");

        runner.Add("the centre card is undistorted", () =>
        {
            var card = CoverFlowMath.Project(0f);

            Check.Close(1.0, card.Squash, 0.0001, "square on");
            Check.Close(1.0, card.Scale, 0.0001, "full size");
            Check.Close(1.0, card.Alpha, 0.0001, "opaque");
            Check.Close(0.0, card.Shear, 0.0001, "not sheared");
            Check.Close(0.0, card.Haze, 0.0001, "and no haze over it");
        });

        runner.Add("cards get smaller, fainter and hazier further out", () =>
        {
            var previous = CoverFlowMath.Project(0f);

            for (var step = 1; step <= 50; step++)
            {
                var card = CoverFlowMath.Project(step * 0.1f);

                Check.True(card.Scale <= previous.Scale + 0.0001f, $"scale grew at {step * 0.1f}");
                Check.True(card.Alpha <= previous.Alpha + 0.0001f, $"alpha rose at {step * 0.1f}");
                Check.True(card.Haze >= previous.Haze - 0.0001f, $"haze fell at {step * 0.1f}");
                previous = card;
            }
        });

        runner.Add("the shear tips the two sides toward each other", () =>
        {
            // The cue the eye actually reads as turning. A card on the right has
            // its far edge pushed down, one on the left pushed up, and the signs
            // must be opposite or the row looks like it is falling over.
            Check.True(CoverFlowMath.Project(3f).Shear < 0, "the right side");
            Check.True(CoverFlowMath.Project(-3f).Shear > 0, "the left side");
        });

        runner.Add("the depth haze reaches its ceiling and stops", () =>
        {
            Check.Close(0.55, CoverFlowMath.Project(5f).Haze, 0.0001, "at the far edge");
            Check.Close(0.55, CoverFlowMath.Project(20f).Haze, 0.0001, "and no further");
        });

        runner.Add("the projection never fades a card out, so the renderer must stop asking", () =>
        {
            // Everything is capped at five slots out, alpha included, so the
            // furthest card is a fifth opaque and stays there however far away
            // it nominally is. Nothing here will ever return zero.
            //
            // That makes the visible range the renderer's responsibility, not
            // the maths'. Draw the whole ring and a forty-album carousel is
            // forty covers stacked on the horizon at 20% each.
            Check.Close(0.20, CoverFlowMath.Project(5f).Alpha, 0.0001, "at the edge of the range");
            Check.Close(0.20, CoverFlowMath.Project(6.25f).Alpha, 0.0001, "past it");
            Check.Close(0.20, CoverFlowMath.Project(40f).Alpha, 0.0001, "and far past it");
        });

        runner.Add("the known discontinuity at one slot out is still there", () =>
        {
            // Deliberate. The macOS build jumps here, and matching it frame for
            // frame is worth more than smoothing it. If this test ever fails,
            // someone has "fixed" it, and the two platforms now move
            // differently.
            var approaching = CoverFlowMath.Project(0.999f).OffsetX;
            var arrived = CoverFlowMath.Project(1f).OffsetX;

            Check.True(Math.Abs(approaching - arrived) > 0.3f,
                $"the jump has gone: {approaching:0.000} then {arrived:0.000}");
        });
    }

    private static void Timing(TestRunner runner)
    {
        runner.Group("Cover Flow: the ring and its pace");

        runner.Add("the carousel holds each record for a share of the feature time", () =>
        {
            Check.Close(3.75, CoverFlowMath.Dwell(30), 0.0001, "the default");
            Check.Close(2.5, CoverFlowMath.Dwell(8), 0.0001, "floored so it cannot blur");
            Check.Close(2.5, CoverFlowMath.Dwell(0), 0.0001, "and a zero cannot stop it");
        });

        runner.Add("the ring is clamped to a sensible number of records", () =>
        {
            Check.Equal(16, CoverFlowMath.Count(16), "the default");
            Check.Equal(5, CoverFlowMath.Count(1), "never fewer than the visible five");
            Check.Equal(60, CoverFlowMath.Count(500), "and never absurdly many");
        });

        runner.Add("a full turn of the ring takes the time it should", () =>
        {
            // Sixteen records at 3.75 seconds each is a minute. Worth pinning:
            // it is the difference between a carousel and a slideshow.
            var seconds = CoverFlowMath.Count(16) * CoverFlowMath.Dwell(30);
            Check.Close(60.0, seconds, 0.0001, "a minute for a full turn");
        });
    }
}

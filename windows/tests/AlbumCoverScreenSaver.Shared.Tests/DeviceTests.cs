namespace AlbumCoverScreenSaver.Shared.Tests;

/// <summary>
/// The last three devices: the jukebox's neon and bubbles, the cassette's reels,
/// and the turntable's arm and its handover rule.
/// </summary>
public static class DeviceTests
{
    public static void Register(TestRunner runner)
    {
        Neon(runner);
        Reels(runner);
        Arm(runner);
        Handover(runner);
    }

    private static void Neon(TestRunner runner)
    {
        runner.Group("Neon Jukebox: the tubes");

        runner.Add("the glow goes wide and faint to narrow and bright", () =>
        {
            var passes = Jukebox.Passes;

            for (var i = 1; i < passes.Length; i++)
            {
                Check.True(passes[i].Width < passes[i - 1].Width, $"pass {i} is not narrower");
                Check.True(passes[i].Alpha > passes[i - 1].Alpha || passes[i].Core,
                    $"pass {i} is not brighter");
            }

            Check.Equal(5, passes.Length, "five strokes");
        });

        runner.Add("only the last pass is the white core", () =>
        {
            // The white-hot core inside a coloured halo is the whole of what
            // makes it read as a gas tube. In the tube's own colour it is just a
            // fat coloured line.
            for (var i = 0; i < Jukebox.Passes.Length - 1; i++)
            {
                Check.False(Jukebox.Passes[i].Core, $"pass {i} should not be the core");
            }

            Check.True(Jukebox.Passes[^1].Core, "the last pass is");
        });

        runner.Add("the two tubes breathe against each other", () =>
        {
            // Both on the same pulse would give one throbbing blob rather than
            // an arch. The inner one is also allowed past full brightness.
            for (var frame = 0; frame < 200; frame++)
            {
                var phase = frame / 30.0;
                var outer = Jukebox.Pulse(phase);
                var inner = Jukebox.CounterPulse(outer);

                Check.True(outer is >= 0.499f and <= 1.001f, $"the outer pulse is {outer}");
                Check.True(inner is >= 0.749f and <= 1.251f, $"the inner pulse is {inner}");
                Check.Close(1.75, outer + inner, 0.0001, "they do not sum to a constant");
            }
        });

        runner.Add("the breath is slow enough to read as a tube warming", () =>
        {
            // A little under five seconds. Any quicker and it reads as a fault
            // rather than as neon.
            var period = Math.PI * 2 / 1.3;
            Check.Close(4.83, period, 0.01, "the period");

            Check.Close(
                Jukebox.Pulse(0), Jukebox.Pulse(period), 0.0001, "it comes back round");
        });

        runner.Add("bubbles rise, and come back as new bubbles", () =>
        {
            var random = new Random(7);
            var bubble = Jukebox.Spawn(random, 0, 0.98f);

            var stepped = bubble;
            for (var i = 0; i < 30; i++) stepped = Jukebox.Step(stepped, 3, i / 30.0, 1.0 / 30.0, random);

            Check.True(stepped.Up < 0.5f, "it should have wrapped back to the bottom");
            Check.Equal(0, stepped.Tube, "and stayed in its own tube");
        });

        runner.Add("a bubble never leaves its tube or grows out of scale", () =>
        {
            var random = new Random(11);
            var bubble = Jukebox.Spawn(random, 1, 0f);

            for (var frame = 0; frame < 30 * 60; frame++)
            {
                bubble = Jukebox.Step(bubble, 5, frame / 30.0, 1.0 / 30.0, random);

                Check.Equal(1, bubble.Tube, $"changed tube at frame {frame}");
                Check.True(bubble.Up is >= 0f and <= 1.0001f, $"up is {bubble.Up}");
                Check.True(bubble.Size is >= 0.18f and <= 0.5f, $"size is {bubble.Size}");
                Check.True(bubble.Tint is >= 0 and < 6, $"tint is {bubble.Tint}");
            }
        });

        runner.Add("bubbles wobble out of step with each other", () =>
        {
            // All of them on the same sine would read as a wave rather than as a
            // tube of bubbles. The offset comes from each one's own place in the
            // list.
            var random = new Random(3);
            var start = Jukebox.Spawn(random, 0, 0.5f) with { Across = 0f, Speed = 0.1f };

            var first = Jukebox.Step(start, 0, 1.0, 1.0 / 30.0, random);
            var second = Jukebox.Step(start, 7, 1.0, 1.0 / 30.0, random);

            Check.True(
                Math.Abs(first.Across - second.Across) > 0.0001f,
                "two bubbles moved sideways by exactly the same amount");
        });

        runner.Add("the selection strips stay on the screen", () =>
        {
            // The specification stacks five fixed-height strips downward from
            // under the display window, which sits inside an arch half the
            // height of the screen. On an ordinary screen the fifth is off the
            // bottom edge and the fourth is half off it.
            foreach (var height in new[] { 720f, 1080f, 1290f, 1440f })
            {
                foreach (var startFraction in new[] { 0.62f, 0.74f, 0.81f, 0.88f })
                {
                    var top = height * startFraction;
                    var (count, rowHeight) = Jukebox.StripRows(height, top);

                    Check.True(
                        top + (count * rowHeight) <= height * Jukebox.StripFoot + 0.01f,
                        $"the strips run past the foot at {height:0} starting at {startFraction:0.00}");

                    Check.True(count <= Jukebox.Strips, "more strips than there are records for");
                }
            }
        });

        runner.Add("strips are squeezed before any of them are dropped", () =>
        {
            // Five slightly shorter strips still read as a list of five
            // records. Dropping one to keep the others full height loses an
            // album for no reason.
            const float height = 1290f;
            var (count, rowHeight) = Jukebox.StripRows(height, height * 0.81f);

            Check.Equal(5, count, "all five are still listed");
            Check.True(rowHeight < height * Jukebox.StripHeight, "and they were squeezed to do it");
            Check.True(rowHeight >= height * Jukebox.StripSmallest, "but not past readable");
        });

        runner.Add("a screen with room gets the height the design asks for", () =>
        {
            const float height = 1290f;
            var (count, rowHeight) = Jukebox.StripRows(height, height * 0.55f);

            Check.Equal(5, count, "all five");
            Check.Close(height * Jukebox.StripHeight, rowHeight, 0.01, "at full height");
        });

        runner.Add("fewer strips beats five unreadable ones", () =>
        {
            const float height = 1290f;
            var (count, rowHeight) = Jukebox.StripRows(height, height * 0.90f);

            Check.True(count < 5, $"{count} strips were kept where there is room for fewer");
            Check.Close(height * Jukebox.StripSmallest, rowHeight, 0.01, "at the smallest readable size");
            Check.True(count > 0, "and at least one is still shown");
        });

        runner.Add("only the top strip can claim to be playing", () =>
        {
            // The archive is newest first, so the live album is at the top of it
            // if it is there at all. A strip further down saying NOW PLAYING
            // would be claiming something that cannot be true.
            Check.True(Jukebox.IsNowPlaying(0, live: 0, featured: 0), "the top strip");
            Check.False(Jukebox.IsNowPlaying(1, live: 1, featured: 1), "the second");
            Check.False(Jukebox.IsNowPlaying(0, live: null, featured: 0), "with nothing playing");
            Check.False(Jukebox.IsNowPlaying(0, live: 4, featured: 0), "or a different album playing");
        });
    }

    private static void Reels(TestRunner runner)
    {
        runner.Group("Cassette Deck: the reels");

        runner.Add("tape moves from one pack to the other and nothing is lost", () =>
        {
            // Wound tape takes up area, not radius, and the total is conserved.
            // That is the whole of the geometry.
            const float max = 100f;
            var hub = TapeDeck.HubRadius(max);

            foreach (var t in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
            {
                var reels = TapeDeck.Wind(max, hub, t, 210);

                var area = (reels.SupplyRadius * reels.SupplyRadius)
                           + (reels.TakeupRadius * reels.TakeupRadius);

                Check.Close(
                    (max * max) + (hub * hub), area, 0.01,
                    $"tape appeared or vanished at {t:0.00}");
            }
        });

        runner.Add("the packs start and end where they should", () =>
        {
            const float max = 100f;
            var hub = TapeDeck.HubRadius(max);

            var start = TapeDeck.Wind(max, hub, 0, 210);
            Check.Close(max, start.SupplyRadius, 0.01, "the supply pack starts full");
            Check.Close(hub, start.TakeupRadius, 0.01, "and the take-up bare");

            var end = TapeDeck.Wind(max, hub, 1, 210);
            Check.Close(hub, end.SupplyRadius, 0.01, "the supply pack ends bare");
            Check.Close(max, end.TakeupRadius, 0.01, "and the take-up full");
        });

        runner.Add("the take-up pack grows fast then slows, and the supply does the reverse", () =>
        {
            // The asymmetry is the entire tell that this is real geometry rather
            // than two circles being slid between two sizes. Halfway through the
            // track the take-up pack is already well past halfway in radius.
            const float max = 100f;
            var hub = TapeDeck.HubRadius(max);

            var half = TapeDeck.Wind(max, hub, 0.5, 210);
            var midpoint = (max + hub) / 2f;

            Check.True(
                half.TakeupRadius > midpoint,
                $"the take-up pack is {half.TakeupRadius:0.0} and a straight line would give {midpoint:0.0}");

            Check.True(half.SupplyRadius > midpoint, "and the supply pack has barely started emptying");
        });

        runner.Add("both reels always turn forwards", () =>
        {
            const float max = 100f;
            var hub = TapeDeck.HubRadius(max);

            var previousSupply = -1f;
            var previousTakeup = -1f;

            for (var step = 0; step <= 100; step++)
            {
                var reels = TapeDeck.Wind(max, hub, step / 100.0, 210);

                Check.True(reels.SupplyAngle >= previousSupply, $"the supply reel went back at {step}");
                Check.True(reels.TakeupAngle >= previousTakeup, $"the take-up reel went back at {step}");

                previousSupply = reels.SupplyAngle;
                previousTakeup = reels.TakeupAngle;
            }
        });

        runner.Add("the emptying reel outruns the full one", () =>
        {
            // Because it has the smaller radius, and each wrap adds one
            // thickness of tape. Neither is on a clock of its own.
            const float max = 100f;
            var hub = TapeDeck.HubRadius(max);

            var a = TapeDeck.Wind(max, hub, 0.80, 210);
            var b = TapeDeck.Wind(max, hub, 0.82, 210);

            var supplyTurned = a.SupplyAngle - b.SupplyAngle;
            var takeupTurned = b.TakeupAngle - a.TakeupAngle;

            Check.True(
                Math.Abs(supplyTurned) > Math.Abs(takeupTurned),
                "near the end of the track the empty supply reel should be spinning faster");
        });

        runner.Add("a deck runs at much the same pace whatever the track's length", () =>
        {
            // Scaled by the song's own duration, so a ninety second interlude
            // and an eight minute closer both wind at a watchable rate.
            const float max = 100f;
            var hub = TapeDeck.HubRadius(max);

            foreach (var span in new[] { 90.0, 210.0, 480.0 })
            {
                var reels = TapeDeck.Wind(max, hub, 1.0, span);
                var turns = reels.TakeupAngle / (Math.PI * 2);
                var perSecond = turns / span;

                Check.True(
                    perSecond is > 0.08 and < 0.35,
                    $"a {span:0}s track winds at {perSecond:0.000} turns a second");
            }
        });

        runner.Add("with nothing playing the tape winds on its own long cycle", () =>
        {
            // Deliberately not the featured-album timer. Winding a whole
            // cassette in the thirty seconds between album changes would look
            // like a rewind rather than a play.
            var (progress, span) = TapeDeck.Clock(false, 0, 0, phase: 105);

            Check.Close(210.0, span, 0.0001, "the idle cycle");
            Check.Close(0.5, progress, 0.0001, "halfway through it");

            var (wrapped, _) = TapeDeck.Clock(false, 0, 0, phase: 211);
            Check.True(wrapped < 0.1, "and it comes round again rather than running off the end");
        });

        runner.Add("a live track is followed on its own clock", () =>
        {
            var (progress, span) = TapeDeck.Clock(true, 240, 0.25, phase: 999);

            Check.Close(0.25, progress, 0.0001, "a quarter of the way in");
            Check.Close(240.0, span, 0.0001, "of a four minute song");
        });

        runner.Add("a live signal with no duration falls back rather than dividing by nothing", () =>
        {
            var (_, span) = TapeDeck.Clock(true, 0, 0.5, phase: 105);
            Check.Close(210.0, span, 0.0001, "the idle cycle");
        });

        runner.Add("the tape leaves each pack on the outside", () =>
        {
            // A true tangent, so the exposed span shifts by itself as one pack
            // empties into the other. Taking the wrong solution runs the tape
            // through the middle of the reel.
            var (leftX, _) = TapeDeck.Tangent(0f, 100f, 50f, 50f, 20f, takeLeft: true);
            var (rightX, _) = TapeDeck.Tangent(100f, 100f, 50f, 50f, 20f, takeLeft: false);

            Check.True(leftX < 50f, "the supply tangent is on the left of its hub");
            Check.True(rightX > 50f, "and the take-up tangent on the right of its");
        });

        runner.Add("a tangent point is always on the pack it belongs to", () =>
        {
            foreach (var radius in new[] { 5f, 20f, 48f })
            {
                var (x, y) = TapeDeck.Tangent(0f, 100f, 50f, 50f, radius, takeLeft: true);

                var distance = Math.Sqrt(((x - 50f) * (x - 50f)) + ((y - 50f) * (y - 50f)));
                Check.Close(radius, distance, 0.01, $"the point is not on the pack at r={radius}");
            }
        });

        runner.Add("only the play key goes down, and only while something is playing", () =>
        {
            // That one twelve per cent of a key's height is the whole of what
            // makes the panel read as responding to the music rather than as a
            // printed decal.
            Check.True(TapeDeck.IsPressed(1, running: true), "play while playing");
            Check.False(TapeDeck.IsPressed(1, running: false), "play while stopped");
            Check.False(TapeDeck.IsPressed(0, running: true), "rewind is always up");
            Check.False(TapeDeck.IsPressed(2, running: true), "and so is fast forward");

            Check.Close(12.0, TapeDeck.KeyDrop(1, true, 100f), 0.0001, "how far it rides down");
            Check.Close(0.0, TapeDeck.KeyDrop(0, true, 100f), 0.0001, "and the others do not");
        });
    }

    private static void Arm(TestRunner runner)
    {
        runner.Group("Record Player: the arm");

        runner.Add("the stylus crosses a modest band, not the whole record", () =>
        {
            // Lead-in groove to run-out. A real arm does not travel from the rim
            // to the spindle, and overstating it makes the arm look like it is
            // racing the song.
            Check.Close(94.0, Turntable.StylusRadius(100f, 0f), 0.01, "the lead-in groove");
            Check.Close(50.0, Turntable.StylusRadius(100f, 1f), 0.01, "and the run-out");
        });

        runner.Add("the arm tracks steadily inward as the song plays", () =>
        {
            var previous = float.MaxValue;

            for (var step = 0; step <= 100; step++)
            {
                var r = Turntable.StylusRadius(100f, step / 100f);
                Check.True(r <= previous, $"the arm went back out at {step}");
                previous = r;
            }
        });

        runner.Add("the tip lands where the arm can actually reach", () =>
        {
            // The law of cosines, because the tip has two constraints at once:
            // an arm's length from the pivot, and a given distance from the
            // middle of the platter.
            const float radius = 200f;
            const float cx = 400f;
            const float cy = 300f;

            var pivotX = cx + (radius * 1.45f);
            var pivotY = cy + (radius * 0.75f);
            var armLength = radius * 1.35f;

            foreach (var progress in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
            {
                var want = Turntable.TipRadius(radius, progress, 0f);
                var angle = Turntable.ArmAngle(pivotX, pivotY, cx, cy, armLength, want);

                var tipX = pivotX + (float)(Math.Cos(angle) * armLength);
                var tipY = pivotY + (float)(Math.Sin(angle) * armLength);

                var fromCentre = Math.Sqrt(((tipX - cx) * (tipX - cx)) + ((tipY - cy) * (tipY - cy)));

                Check.Close(want, fromCentre, 0.01, $"the tip is off the groove at {progress:0.00}");
            }
        });

        runner.Add("the arm lifts, holds, and lowers", () =>
        {
            // The pace of a real changer, and eased at both ends: a linear lift
            // looks like a robot arm rather than a counterweighted one.
            Check.Close(0.0, Turntable.ArmLift(0), 0.0001, "down on the record");
            Check.Close(1.0, Turntable.ArmLift(0.5), 0.0001, "up by half a second");
            Check.Close(1.0, Turntable.ArmLift(0.9), 0.0001, "and still up at nine tenths");
            Check.Close(0.0, Turntable.ArmLift(1.9), 0.0001, "back down as the change ends");

            Check.True(Turntable.ArmLift(0.25) is > 0.1f and < 0.9f, "and moving in between");
        });

        runner.Add("the record is swapped while the arm is clear of it", () =>
        {
            // Which is the only way it reads as a record being changed rather
            // than as one dissolving into another.
            Check.Close(0.0, Turntable.Fade(0.3), 0.0001, "the old record is still fully there");

            for (var t = 0.55; t < 1.0; t += 0.05)
            {
                Check.Close(1.0, Turntable.ArmLift(t), 0.0001, $"the arm is not clear at {t:0.00}");
            }

            Check.Close(1.0, Turntable.Fade(1.0), 0.0001, "and the new one is fully in by then");
        });

        runner.Add("the platter turns slowly enough to be watched", () =>
        {
            // Nine revolutions a minute rather than thirty three and a third.
            // Filling a whole screen a real speed reads as frantic.
            var perFrame = Turntable.SpinStep(9, 1.0 / 30.0);
            var perSecond = perFrame * 30 / (Math.PI * 2);

            Check.Close(0.15, perSecond, 0.001, "revolutions a second");
            Check.True(Turntable.SpinStep(0, 1.0 / 30.0) > 0, "and a speed of zero is floored");
        });

        runner.Add("the arm keeps working with nothing playing", () =>
        {
            // It has to, because it must keep going through the grace period and
            // while following live playback is switched off.
            Check.Close(
                0.5, Turntable.PlayProgress(false, 0, phase: 90, startedAt: 0, secondsPerRecord: 180),
                0.0001, "halfway through a record on the timer");

            Check.Close(
                0.25, Turntable.PlayProgress(true, 0.25, phase: 999, startedAt: 0, secondsPerRecord: 180),
                0.0001, "and following the track when there is one");
        });

        runner.Add("a very short seconds-per-record is floored", () =>
        {
            Check.True(
                Turntable.PlayProgress(false, 0, phase: 5, startedAt: 0, secondsPerRecord: 1) < 1f,
                "a one second record crossed the whole side in a second");
        });
    }

    private static void Handover(TestRunner runner)
    {
        runner.Group("Record Player: handing over when the music stops");

        runner.Add("another style is shown once the music has been off for twenty seconds", () =>
        {
            // A turntable with nothing on it is a dead screen.
            Check.Equal(
                CollageMode.Vinyl,
                Turntable.ActiveMode(CollageMode.Vinyl, phase: 10, lastLiveAt: 0, CollageMode.Mosaic),
                "ten seconds in, still the deck");

            Check.Equal(
                CollageMode.Vinyl,
                Turntable.ActiveMode(CollageMode.Vinyl, phase: 19.9, lastLiveAt: 0, CollageMode.Mosaic),
                "and at nineteen and a bit");

            // And it hands over to the style the user chose, whatever that is,
            // rather than to a hard-coded one.
            Check.Equal(
                CollageMode.Drift,
                Turntable.ActiveMode(CollageMode.Vinyl, phase: 20, lastLiveAt: 0, CollageMode.Drift),
                "but not at twenty");

            Check.Equal(
                CollageMode.Gallery,
                Turntable.ActiveMode(CollageMode.Vinyl, phase: 20, lastLiveAt: 0, CollageMode.Gallery),
                "and the choice is honoured");
        });

        runner.Add("skipping a track does not yank the visual away and back", () =>
        {
            // The whole reason the grace period exists. A gap between songs must
            // not throw the style away for a second and then restore it.
            for (var gap = 0.0; gap < Turntable.Grace; gap += 0.5)
            {
                Check.Equal(
                    CollageMode.Vinyl,
                    Turntable.ActiveMode(CollageMode.Vinyl, 100 + gap, 100, CollageMode.Mosaic),
                    $"a {gap:0.0}s gap swapped the style");
            }
        });

        runner.Add("with nothing ever playing it opens straight into the fallback", () =>
        {
            // The clock starts a long way in the past rather than at zero, so
            // there is no twenty seconds of idle turntable when the saver
            // starts.
            Check.Equal(
                CollageMode.Gallery,
                Turntable.ActiveMode(CollageMode.Vinyl, phase: 0, lastLiveAt: -1000, CollageMode.Gallery),
                "the first frame");
        });

        runner.Add("every other style is left alone", () =>
        {
            // The rule is Record Player's alone. Nothing else hands its screen
            // to another style, whatever the music is doing.
            foreach (var mode in CollageModes.All)
            {
                if (mode == CollageMode.Vinyl) continue;

                Check.Equal(
                    mode,
                    Turntable.ActiveMode(mode, phase: 9999, lastLiveAt: -1000, CollageMode.Mosaic),
                    $"{mode.Title()} was swapped out");
            }
        });

        runner.Add("the fallback list cannot offer Record Player as its own fallback", () =>
        {
            // Which would be a style handing over to itself and nothing
            // happening at all.
            Check.False(
                SettingsSchema.FallbackStyles().Contains(CollageMode.Vinyl),
                "Record Player is offered as its own fallback");

            Check.Equal(
                CollageModes.All.Count - 1, SettingsSchema.FallbackStyles().Count,
                "and everything else is offered");
        });

        runner.Add("the scrim has no edge to see", () =>
        {
            // The specification's version steps from nothing to thirty five per
            // cent in no distance, which draws a hard vertical line down the
            // full height of the screen at 46% across. It is plainly visible on
            // a screenshot once you know to look for it.
            var previous = 0f;

            for (var step = 0; step <= 1000; step++)
            {
                var across = step / 1000f;
                var alpha = Turntable.ScrimAlpha(across);

                Check.True(alpha >= previous, $"the scrim got lighter at {across:0.000}");
                Check.True(alpha - previous < 0.02f, $"a step of {alpha - previous:0.000} at {across:0.000}");

                previous = alpha;
            }
        });

        runner.Add("the scrim still matches the specification where it matters", () =>
        {
            // Everything from the old left edge rightward is unchanged. Only the
            // approach to it is different.
            Check.Close(0.0, Turntable.ScrimAlpha(0.3f), 0.0001, "nothing over the deck");
            Check.Close(0.0, Turntable.ScrimAlpha(Turntable.ScrimStart), 0.0001, "nothing where it begins");

            Check.Close(
                Turntable.ScrimAtSeam, Turntable.ScrimAlpha(Turntable.ScrimSeam), 0.0001,
                "the specification's own value at its own left edge");

            Check.Close(
                (Turntable.ScrimAtSeam + Turntable.ScrimAtEdge) / 2f,
                Turntable.ScrimAlpha((Turntable.ScrimSeam + 1f) / 2f), 0.0001,
                "and halfway along it");

            Check.Close(Turntable.ScrimAtEdge, Turntable.ScrimAlpha(1f), 0.0001, "darkest at the edge");
        });

        runner.Add("the scrim never blacks out the type it sits behind", () =>
        {
            Check.True(Turntable.ScrimAlpha(1f) < 1f, "the right edge is opaque");
        });

        runner.Add("the table keeps what is on it at the cap", () =>
        {
            Check.Equal(26, Turntable.SleeveCap(26, false), "the default");
            Check.Equal(5, Turntable.SleeveCap(26, true), "preview keeps it small");
            Check.Equal(0, Turntable.SleeveCap(0, false), "a cap of nothing clears the table");
            Check.Equal(40, Turntable.SleeveCap(500, false), "and it cannot be filled forever");
        });
    }
}

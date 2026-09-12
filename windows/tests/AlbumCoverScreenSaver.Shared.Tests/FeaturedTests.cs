namespace AlbumCoverScreenSaver.Shared.Tests;

/// <summary>
/// Which album a single-album style shows, and when it changes.
/// </summary>
/// <remarks>
/// Fourteen of the fifteen styles still to build stand on this one rule, so a
/// mistake here would be a mistake in all fourteen at once, and it is the kind
/// that hides: an idle timer that restarts at the wrong moment, or a rotation
/// that quietly keeps running under live playback, looks perfectly normal until
/// you sit and time it.
/// </remarks>
public static class FeaturedTests
{
    private const double Frame = 1.0 / 30.0;

    private static Featured New(int seed = 4242, double bias = 0.4) =>
        new(new AlbumPicker(seed), bias);

    /// <summary>Runs the clock forward, handing the same live index to every frame.</summary>
    private static double Run(Featured featured, double from, double seconds, int? live, double idleSpan, int albums)
    {
        var phase = from;
        var frames = (int)Math.Round(seconds / Frame);

        for (var i = 0; i < frames; i++)
        {
            phase += Frame;
            featured.Advance(phase, live, idleSpan, albums);
        }

        return phase;
    }

    public static void Register(TestRunner runner)
    {
        Live(runner);
        Idle(runner);
        Fades(runner);
        Edges(runner);
    }

    private static void Live(TestRunner runner)
    {
        runner.Group("Featured album: following the music");

        runner.Add("it switches to whatever is playing", () =>
        {
            var featured = New();
            featured.Reset(0, 3);

            featured.Advance(1.0, live: 7, idleSpan: 30, albumCount: 20);

            Check.Equal(7, featured.Index, "now showing the live album");
            Check.Equal(3, featured.PreviousIndex, "and remembers what it replaced");
            Check.True(featured.IsChanging, "with a change under way");
        });

        runner.Add("playing the same album again changes nothing", () =>
        {
            // An album is several tracks long. Each one must not restart the
            // crossfade, or the cover pulses every three minutes.
            var featured = New();
            featured.Reset(0, 5);

            Run(featured, 0, 10, live: 5, idleSpan: 30, albums: 20);

            Check.Equal(5, featured.Index, "unchanged");
            Check.False(featured.IsChanging, "and no change was started");
        });

        runner.Add("the rotation does not run underneath live playback", () =>
        {
            // The whole point of following the music: three minutes of one
            // album is three minutes of that cover, not five rotations.
            var featured = New();
            featured.Reset(0, 5);

            Run(featured, 0, 180, live: 5, idleSpan: 8, albums: 40);

            Check.Equal(5, featured.Index, "still on the album that is playing");
        });

        runner.Add("a change cannot be interrupted by another one", () =>
        {
            // Skipping through tracks quickly must not stack up half-finished
            // crossfades. The index freezes until the current one lands.
            var featured = New();
            featured.Reset(0, 1);

            featured.Advance(1.0, live: 2, idleSpan: 30, albumCount: 20);
            Check.Equal(2, featured.Index, "the first change started");

            featured.Advance(1.3, live: 9, idleSpan: 30, albumCount: 20);
            Check.Equal(2, featured.Index, "the second was ignored mid-change");

            // Once it lands, the next frame can act on what is playing now.
            featured.Advance(1.0 + Featured.ChangeDuration + Frame, 9, 30, 20);
            featured.Advance(1.0 + Featured.ChangeDuration + (Frame * 2), 9, 30, 20);

            Check.Equal(9, featured.Index, "and is picked up afterwards");
        });

        runner.Add("when the music stops the rotation waits a full span", () =>
        {
            // Not an immediate jump. Pausing to answer the door should not
            // restart the whole visual.
            var featured = New();
            featured.Reset(0, 4);

            var phase = Run(featured, 0, 5, live: 4, idleSpan: 20, albums: 30);
            phase = Run(featured, phase, 15, live: null, idleSpan: 20, albums: 30);

            Check.Equal(4, featured.Index, "still on it fifteen seconds after the music stopped");

            Run(featured, phase, 10, live: null, idleSpan: 20, albums: 30);
            Check.NotEqual(4, featured.Index, "and moves on once the span is up");
        });
    }

    private static void Idle(TestRunner runner)
    {
        runner.Group("Featured album: rotating on its own");

        runner.Add("it rotates at about the interval it was given", () =>
        {
            var featured = New();
            featured.Reset(0, 0);

            var changes = 0;
            var previous = featured.Index;
            var phase = 0.0;

            for (var i = 0; i < 30 * 120; i++)
            {
                phase += Frame;
                featured.Advance(phase, null, idleSpan: 20, albumCount: 40);
                if (featured.Index != previous)
                {
                    changes++;
                    previous = featured.Index;
                }
            }

            // Two minutes at twenty seconds a turn, plus 1.35s of change each
            // time, so five or six.
            Check.True(changes is >= 4 and <= 7, $"{changes} changes in two minutes at a 20 second span");
        });

        runner.Add("a short interval is floored at five seconds", () =>
        {
            var featured = New();
            featured.Reset(0, 0);

            var changes = 0;
            var previous = featured.Index;
            var phase = 0.0;

            for (var i = 0; i < 30 * 60; i++)
            {
                phase += Frame;
                featured.Advance(phase, null, idleSpan: 0, albumCount: 40);
                if (featured.Index != previous)
                {
                    changes++;
                    previous = featured.Index;
                }
            }

            // Floored at five, plus 1.35 of change, so about nine a minute. An
            // unfloored zero would be a change every frame.
            Check.True(changes is >= 6 and <= 11, $"{changes} changes in a minute at a zero span");
        });

        runner.Add("the idle clock restarts when a change lands, not when it starts", () =>
        {
            // Otherwise the first 1.35 seconds of every interval is spent
            // finishing the previous change, and the visible hold is shorter
            // than the number in the settings window.
            var featured = New();
            featured.Reset(0, 0);

            var phase = 0.0;
            var landedAt = -1.0;
            var landedOn = -1;
            var changedAgainAt = -1.0;

            // Driven a frame at a time and watched, rather than run in blocks
            // and sampled: a block boundary that happens to fall just after a
            // rotation makes this test lie in either direction.
            for (var i = 0; i < 30 * 60; i++)
            {
                var wasChanging = featured.IsChanging;

                phase += Frame;
                featured.Advance(phase, null, idleSpan: 10, albumCount: 40);

                if (wasChanging && !featured.IsChanging && landedAt < 0)
                {
                    landedAt = phase;
                    landedOn = featured.Index;
                    continue;
                }

                if (landedAt >= 0 && featured.Index != landedOn)
                {
                    changedAgainAt = phase;
                    break;
                }
            }

            Check.True(landedAt >= 0, "a change landed at all");
            Check.True(changedAgainAt >= 0, "and another one followed");

            // A full span, not a span minus the 1.35 seconds the last change
            // took. Otherwise the visible hold is shorter than the number in
            // the settings window, every single time.
            var held = changedAgainAt - landedAt;
            Check.True(held >= 10 - 0.05, $"held {held:0.00}s after landing, expected at least 10");
        });

        runner.Add("it does not pick the album it is already showing", () =>
        {
            var featured = New();
            featured.Reset(0, 0);

            var phase = 0.0;
            var repeats = 0;

            for (var turn = 0; turn < 40; turn++)
            {
                var before = featured.Index;
                phase = Run(featured, phase, 12, null, idleSpan: 6, albums: 30);
                if (featured.Index == before) repeats++;
            }

            Check.True(repeats <= 2, $"{repeats} of 40 rotations landed on the same album");
        });
    }

    private static void Fades(TestRunner runner)
    {
        runner.Group("Featured album: the change itself");

        runner.Add("the fade runs from nothing to everything across 1.35 seconds", () =>
        {
            var featured = New();
            featured.Reset(0, 1);
            featured.Advance(1.0, live: 2, idleSpan: 30, albumCount: 20);

            Check.Close(0.0, featured.FadeRaw(1.0), 0.0001, "at the start");
            Check.Close(0.5, featured.FadeRaw(1.0 + (Featured.ChangeDuration / 2)), 0.0001, "halfway");
            Check.Close(1.0, featured.FadeRaw(1.0 + Featured.ChangeDuration), 0.0001, "at the end");
        });

        runner.Add("a settled album is fully faded in, not stuck mid-change", () =>
        {
            var featured = New();
            featured.Reset(0, 3);

            Check.Close(1.0, featured.FadeRaw(0), 0.0001, "raw");
            Check.Close(1.0, featured.Fade(0), 0.0001, "eased");
        });

        runner.Add("the eased fade never overshoots or goes backwards", () =>
        {
            var featured = New();
            featured.Reset(0, 1);
            featured.Advance(1.0, live: 2, idleSpan: 30, albumCount: 20);

            var previous = -1f;
            for (var step = 0; step <= 100; step++)
            {
                var value = featured.Fade(1.0 + (Featured.ChangeDuration * step / 100.0));

                Check.True(value is >= 0f and <= 1f, $"fade {value} out of range");
                Check.True(value >= previous - 0.0001f, "fade went backwards");
                previous = value;
            }
        });
    }

    private static void Edges(TestRunner runner)
    {
        runner.Group("Featured album: how long it has been up");

        runner.Add("the clock for how long an album has been up runs while music plays", () =>
        {
            // This is the whole reason it is a separate field from the idle
            // clock. The idle clock is held at the current moment for every
            // frame the music is on, so an age measured from it would read zero
            // for as long as a record was playing. CRT Terminal types its
            // readout out of this one, and on the idle clock it would never
            // finish a line while you were listening.
            var featured = New();
            featured.Reset(0, 3);

            var phase = Run(featured, 0, 10, live: 3, idleSpan: 30, albums: 20);

            Check.Equal(3, featured.Index, "still the album that is playing");
            Check.True(
                phase - featured.ShownSince > 9.0,
                $"it has been up {phase - featured.ShownSince:0.0}s and should be about ten");
        });

        runner.Add("it restarts when a change begins, not when it lands", () =>
        {
            // A deliberate departure from the specification, which resets on
            // completion and therefore shows the incoming album's words already
            // fully typed for a second and a third before wiping and typing
            // them again.
            var featured = New();
            featured.Reset(0, 3);

            featured.Advance(5, 7, 30, albumCount: 20);

            Check.Equal(7, featured.Index, "the change has begun");
            Check.True(featured.IsChanging, "and is still in flight");
            Check.Close(5.0, featured.ShownSince, 0.0001, "the clock restarted with it");
        });

        runner.Add("it is not disturbed by a change landing", () =>
        {
            var featured = New();
            featured.Reset(0, 3);

            featured.Advance(5, 7, 30, albumCount: 20);
            Run(featured, 5, 3, live: 7, idleSpan: 30, albums: 20);

            Check.False(featured.IsChanging, "the change has landed");
            Check.Close(5.0, featured.ShownSince, 0.0001, "and the clock still runs from when it began");
        });

        runner.Group("Featured album: the awkward cases");

        runner.Add("an empty archive is left alone rather than crashed on", () =>
        {
            var featured = New();
            featured.Advance(10, null, 20, albumCount: 0);
            featured.Advance(40, 3, 20, albumCount: 0);

            Check.Equal(0, featured.Index, "unchanged");
        });

        runner.Add("a single album has nowhere to rotate to", () =>
        {
            var featured = New();
            featured.Reset(0, 0);

            Run(featured, 0, 60, null, idleSpan: 5, albums: 1);
            Check.Equal(0, featured.Index, "still the only album there is");
        });

        runner.Add("an index left stale by a shrinking archive is brought back in range", () =>
        {
            // Indices are positions in a list that is rebuilt on every reload.
            // A style holding one across a reload is the normal case, not an
            // error, and it must not read off the end.
            var featured = New();
            featured.Reset(0, 57);

            featured.Advance(1, null, 20, albumCount: 10);

            Check.True(featured.Index < 10, $"index {featured.Index} is inside the archive");
            Check.True(featured.PreviousIndex < 10, $"previous index {featured.PreviousIndex} is too");
        });
    }
}

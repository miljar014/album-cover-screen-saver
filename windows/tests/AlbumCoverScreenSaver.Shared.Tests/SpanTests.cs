namespace AlbumCoverScreenSaver.Shared.Tests;

/// <summary>
/// One composition spread across every screen.
/// </summary>
/// <remarks>
/// The thing that makes this work rather than being the spanning window the
/// design ruled out: there is still one window per screen, each at its own
/// scaling, and each is told it is looking at part of something larger. These
/// tests are about the arithmetic of "which part".
/// </remarks>
public static class SpanTests
{
    private static (int, int, int, int) Rect(int x, int y, int w, int h) => (x, y, w, h);

    public static void Register(TestRunner runner)
    {
        Modes(runner);
        Canvas(runner);
        When(runner);
    }

    private static void Modes(TestRunner runner)
    {
        runner.Group("Screens: the three relationships");

        runner.Add("only one of the three is allowed to say span", () =>
        {
            // Windows uses "span" and "stretch" for wallpaper cut across the
            // bezels. The mode that shows a complete picture per screen must not
            // borrow either word, or somebody picks it expecting one thing and
            // gets another.
            Check.False(
                MultiMonitorMode.Linked.Title().Contains("across", StringComparison.OrdinalIgnoreCase),
                "the together mode claims to span");

            Check.False(
                MultiMonitorMode.Linked.Title().Contains("stretch", StringComparison.OrdinalIgnoreCase),
                "the together mode says stretch");

            Check.True(
                MultiMonitorMode.Span.Title().Contains("across", StringComparison.OrdinalIgnoreCase),
                "the mode that does span should say so");
        });

        runner.Add("the wire values round trip", () =>
        {
            foreach (var mode in MultiMonitorModes.All)
            {
                Check.Equal(
                    mode, MultiMonitorModes.ParseOr(mode.Id(), MultiMonitorMode.Linked),
                    mode.Title());
            }
        });

        runner.Add("an unknown mode falls back rather than failing", () =>
        {
            // Which is also what an older build does when it reads "span", and
            // that is the right answer: it cannot spread a picture it has no
            // code for.
            foreach (var stored in new string?[] { null, "", "  ", "stretch", "mosaic" })
            {
                Check.Equal(
                    MultiMonitorMode.Separate,
                    MultiMonitorModes.ParseOr(stored, MultiMonitorMode.Separate),
                    $"stored {stored ?? "null"}");
            }
        });

        runner.Add("only fields of covers may be spread", () =>
        {
            // Everything else centres on one subject, and spread across two
            // screens that subject's middle lands in the gap between the
            // monitors while the other screen shows an empty plinth.
            foreach (var mode in new[]
                     {
                         CollageMode.Mosaic, CollageMode.Wall, CollageMode.Drift,
                         CollageMode.Gallery, CollageMode.Ambient, CollageMode.Starfield,
                     })
            {
                Check.True(SpanLayout.CanSpan(mode), $"{mode.Title()} should be spreadable");
            }

            foreach (var mode in new[]
                     {
                         CollageMode.Vinyl, CollageMode.Cassette, CollageMode.CdPlayer,
                         CollageMode.Jukebox, CollageMode.Crt, CollageMode.Hero,
                         CollageMode.Newsstand, CollageMode.Zoetrope, CollageMode.CoverFlow,
                     })
            {
                Check.False(SpanLayout.CanSpan(mode), $"{mode.Title()} should not be");
            }

            Check.Equal(6, SpanLayout.Spannable().Count, "six styles, and the list agrees");
        });
    }

    private static void Canvas(TestRunner runner)
    {
        runner.Group("Screens: what each one is looking at");

        runner.Add("one screen is simply itself", () =>
        {
            var canvas = SpanLayout.For(Rect(0, 0, 1920, 1080), [Rect(0, 0, 1920, 1080)], 1f);

            Check.Close(1920.0, canvas.Width, 0.01, "its own width");
            Check.Close(1080.0, canvas.Height, 0.01, "its own height");
            Check.True(canvas.IsSingle, "and no offset");
        });

        runner.Add("two side by side see one canvas twice as wide", () =>
        {
            var all = new[] { Rect(0, 0, 1920, 1080), Rect(1920, 0, 1920, 1080) };

            var left = SpanLayout.For(all[0], all, 1f);
            var right = SpanLayout.For(all[1], all, 1f);

            Check.Close(3840.0, left.Width, 0.01, "the whole arrangement");
            Check.Close(3840.0, right.Width, 0.01, "seen the same from both");

            Check.Close(0.0, left.OffsetX, 0.01, "the left screen starts at the left");
            Check.Close(1920.0, right.OffsetX, 0.01, "and the right one halfway along");
        });

        runner.Add("a screen above and to the left gets a negative origin handled", () =>
        {
            // Windows puts the primary screen at the origin, so a monitor placed
            // left of it has negative coordinates. The canvas has to start at
            // the leftmost edge, not at zero, or half the arrangement is off it.
            var all = new[] { Rect(0, 0, 1920, 1080), Rect(-1280, -120, 1280, 720) };

            var main = SpanLayout.For(all[0], all, 1f);
            var other = SpanLayout.For(all[1], all, 1f);

            Check.Close(3200.0, main.Width, 0.01, "the full width");
            Check.Close(1280.0, main.OffsetX, 0.01, "the main screen is 1280 along");
            Check.Close(0.0, other.OffsetX, 0.01, "and the other one starts the canvas");
            Check.Close(0.0, other.OffsetY, 0.01, "including at the top");
            Check.Close(120.0, main.OffsetY, 0.01, "with the main screen pushed down");
        });

        runner.Add("two screens at different scaling still line up physically", () =>
        {
            // The whole reason this is not a spanning window. Each screen
            // converts the same arrangement into its own points, so the canvases
            // differ in point size while a cover at a given place on the
            // arrangement lands at the same spot on the desk.
            var all = new[] { Rect(0, 0, 3840, 2160), Rect(3840, 0, 1920, 1080) };

            var big = SpanLayout.For(all[0], all, 2f);
            var small = SpanLayout.For(all[1], all, 1f);

            Check.Close(2880.0, big.Width, 0.01, "5760 pixels at 200% is 2880 points");
            Check.Close(5760.0, small.Width, 0.01, "and 5760 points at 100%");

            // The seam between them: 3840 device pixels along the arrangement.
            Check.Close(1920.0, big.OffsetX + (3840 / 2f) - big.OffsetX, 0.01, "in the big screen's points");
            Check.Close(3840.0, small.OffsetX, 0.01, "and where the small screen begins");

            // Which is the same physical place seen from either side.
            Check.Close(
                small.OffsetX, (big.OffsetX + 1920f) * 2f, 0.01,
                "the two screens disagree about where the seam is");
        });

        runner.Add("a screen only ever looks at a part it actually covers", () =>
        {
            foreach (var scale in new[] { 1f, 1.5f, 2f })
            {
                var all = new[] { Rect(0, 0, 1920, 1080), Rect(1920, 200, 2560, 1440) };

                foreach (var screen in all)
                {
                    var canvas = SpanLayout.For(screen, all, scale);

                    Check.True(canvas.OffsetX >= -0.01f, "looking off the left of the canvas");
                    Check.True(canvas.OffsetY >= -0.01f, "off the top");

                    Check.True(
                        canvas.OffsetX + (screen.Item3 / scale) <= canvas.Width + 0.01f,
                        "off the right");

                    Check.True(
                        canvas.OffsetY + (screen.Item4 / scale) <= canvas.Height + 0.01f,
                        "off the bottom");
                }
            }
        });

        runner.Add("nonsense falls back to the screen on its own", () =>
        {
            // A screen drawing an ordinary composition is a far better failure
            // than a screen drawing nothing.
            var screen = Rect(0, 0, 1920, 1080);

            Check.True(SpanLayout.For(screen, [], 1f).IsSingle, "no arrangement at all");
            Check.True(
                SpanLayout.For(screen, [Rect(0, 0, 0, 0), Rect(0, 0, 0, 0)], 1f).IsSingle,
                "screens of no size");

            Check.Close(1920.0, SpanLayout.For(screen, [screen], 0f).Width, 0.01, "a scale of zero");
        });
    }

    private static void When(TestRunner runner)
    {
        runner.Group("Screens: when the picture is spread");

        runner.Add("all four conditions have to hold", () =>
        {
            Check.True(
                SpanLayout.ShouldSpan(MultiMonitorMode.Span, CollageMode.Mosaic, 2, true),
                "the case it is for");

            Check.False(
                SpanLayout.ShouldSpan(MultiMonitorMode.Linked, CollageMode.Mosaic, 2, true),
                "a different relationship");

            Check.False(
                SpanLayout.ShouldSpan(MultiMonitorMode.Span, CollageMode.Mosaic, 1, true),
                "one screen, which is just a screen");

            Check.False(
                SpanLayout.ShouldSpan(MultiMonitorMode.Span, CollageMode.Vinyl, 2, true),
                "a style with one subject in the middle");

            Check.False(
                SpanLayout.ShouldSpan(MultiMonitorMode.Span, CollageMode.Mosaic, 2, false),
                "a screen set to its own style is not part of the shared picture");
        });

        runner.Add("a screen with its own style keeps it while the others spread", () =>
        {
            // Taking one screen out of the set is exactly what the per-screen
            // dropdown is for, and it has to keep working in this mode.
            var settings = new Settings { Mode = CollageMode.Mosaic, MultiMonitor = MultiMonitorMode.Span };
            settings.Displays["mine"] = new DisplaySetting { Mode = "crt" };

            var follower = settings.ForDisplay("theirs");
            var loner = settings.ForDisplay("mine");

            Check.True(
                SpanLayout.ShouldSpan(settings.MultiMonitor, CollageMode.Mosaic, 2, follower.IsInherit),
                "the screen following the main style joins in");

            Check.False(
                SpanLayout.ShouldSpan(settings.MultiMonitor, CollageMode.Mosaic, 2, loner.IsInherit),
                "and the one with its own style does not");
        });
    }
}

namespace AlbumCoverScreenSaver.Shared.Tests;

/// <summary>
/// CD Player: the disc, the rainbow, and where the jewel cases may land.
/// </summary>
public static class CdPlayerTests
{
    public static void Register(TestRunner runner)
    {
        Disc(runner);
        Progress(runner);
    }

    private static void Disc(TestRunner runner)
    {
        runner.Group("CD Player: the disc");

        runner.Add("the whole object scales off one radius", () =>
        {
            // Every measurement is a multiple of the radius, so the player holds
            // its proportions from a laptop screen to a wall. The test is that
            // the pieces still nest in the right order at any size.
            foreach (var (width, height) in new[] { (1280f, 720f), (1920f, 1080f), (3440f, 1440f) })
            {
                var r = CdPlayer.Radius(width, height);
                var (clear, hub, hole) = CdPlayer.Middle(r);

                Check.True(hole < hub, $"the hole is not inside the hub at {width:0}x{height:0}");
                Check.True(hub < clear, "the hub is not inside the clear ring");
                Check.True(clear < CdPlayer.LabelRadius(r), "the clear ring is not inside the label");
                Check.True(CdPlayer.LabelRadius(r) < r, "the label is not inside the disc");
                Check.True(r < CdPlayer.DiscWell(r), "the disc does not sit in its well");
                Check.True(CdPlayer.DiscWell(r) < CdPlayer.LidRim(r), "the well is outside the rim");
                Check.True(CdPlayer.LidRim(r) * 2f < CdPlayer.BodySide(r), "the rim overhangs the body");
            }
        });

        runner.Add("the player fits on the screen it is drawn on", () =>
        {
            foreach (var (width, height) in new[] { (1280f, 720f), (1920f, 1080f), (1080f, 1920f) })
            {
                var r = CdPlayer.Radius(width, height);
                var side = CdPlayer.BodySide(r);
                var (cx, cy) = CdPlayer.Centre(width, height);

                Check.True(side <= width, $"too wide at {width:0}x{height:0}");
                Check.True(cx - (side / 2f) >= 0, "off the left");
                Check.True(cx + (side / 2f) <= width, "off the right");
                Check.True(cy - (side / 2f) >= 0, $"off the top at {width:0}x{height:0}");
            }
        });

        runner.Add("the disc turns clockwise and takes about five and a half seconds", () =>
        {
            // Eleven rpm. Slow enough to read as a disc rather than a fan, and
            // it never stops, because a disc spinning down when the music ended
            // would look like the saver had crashed.
            Check.True(CdPlayer.Spin(1) > 0, "the disc turns clockwise on screen");

            var revolution = Math.PI * 2 / CdPlayer.SpinRate;
            Check.Close(5.46, revolution, 0.01, "one turn");

            Check.Close(
                Math.PI * 2, CdPlayer.Spin(revolution) - CdPlayer.Spin(0), 0.0001,
                "exactly one revolution");
        });

        runner.Add("the disc keeps turning with nothing playing", () =>
        {
            // The point of the free clock. There is no argument here about live
            // or idle because the function takes neither.
            Check.True(CdPlayer.Spin(120) > CdPlayer.Spin(119), "two minutes in and still going");
        });

        runner.Add("the rainbow arcs overlap into a continuous wheel", () =>
        {
            // Spaced 5.625 degrees apart and sweeping six, so each runs into the
            // next. Without the overlap you get sixty four stripes instead of a
            // rainbow.
            var spacing = CdPlayer.ArcStart(1) - CdPlayer.ArcStart(0);

            Check.Close(5.625, spacing, 0.0001, "the spacing");
            Check.True(CdPlayer.ArcSweep > spacing, "the arcs do not overlap");

            Check.Close(0.0, CdPlayer.ArcHue(0), 0.0001, "the wheel starts at red");
            Check.True(CdPlayer.ArcHue(CdPlayer.Arcs - 1) < 1.0, "and stops short of coming back to it");
        });

        runner.Add("a stroked arc becomes a band, not a line", () =>
        {
            // Drawn at 0.74 of the radius and stroked at 0.44 of it, so the band
            // covers roughly half the disc's face. Reproducing it as a filled
            // wedge instead gives a straight edge where this gives a round one.
            const float r = 100f;

            var ring = CdPlayer.ArcRadius(r);
            var half = CdPlayer.ArcWidth(r) / 2f;

            Check.Close(52.0, ring - half, 0.01, "the inner edge of the band");
            Check.Close(96.0, ring + half, 0.01, "and the outer");
            Check.True(ring + half < r, "the band stays inside the disc");
            Check.True(ring - half > CdPlayer.LabelRadius(r), "and clear of the printed label");
        });

        runner.Add("the credits stay on the screen", () =>
        {
            // The specification puts them three per cent of the height below the
            // body and leaves them there. The body is ninety one per cent of the
            // height on an ordinary screen, so that is off the bottom edge, and
            // it is off a sixteen by ten one too. They are pushed up until they
            // fit, which puts them over the bare lower edge of the body.
            foreach (var (width, height) in new[] { (1920f, 1080f), (1920f, 1200f), (3440f, 1440f) })
            {
                var r = CdPlayer.Radius(width, height);
                var (_, cy) = CdPlayer.Centre(width, height);
                var bodyBottom = cy + (CdPlayer.BodySide(r) / 2f);

                var block = height * 0.08f;
                var top = CdPlayer.CreditsTop(height, bodyBottom, block);

                Check.True(
                    top + block <= height,
                    $"the credits run off the bottom at {width:0}x{height:0}");

                Check.True(top > height * 0.5f, "and they have not climbed onto the disc");
            }
        });

        runner.Add("a page with room keeps the gap the specification asks for", () =>
        {
            // A tall narrow screen has the body nowhere near the floor, and then
            // the rule must keep out of the way.
            const float height = 1920f;
            var r = CdPlayer.Radius(1080f, height);
            var (_, cy) = CdPlayer.Centre(1080f, height);
            var bodyBottom = cy + (CdPlayer.BodySide(r) / 2f);

            Check.Close(
                bodyBottom + (height * CdPlayer.CreditsGap),
                CdPlayer.CreditsTop(height, bodyBottom, height * 0.08f),
                0.01, "the gap below the body");
        });

        runner.Add("the jewel cases keep away from the player", () =>
        {
            // The keep-out circle has to clear the body, or a case lands on top
            // of the thing the style is about.
            foreach (var (width, height) in new[] { (1280f, 720f), (1920f, 1080f), (3440f, 1440f) })
            {
                var (_, _, keepOut) = CdPlayer.KeepOut(width, height);
                var r = CdPlayer.Radius(width, height);

                Check.True(
                    keepOut > CdPlayer.LidRim(r),
                    $"the keep-out circle is inside the lid rim at {width:0}x{height:0}");
            }
        });
    }

    private static void Progress(TestRunner runner)
    {
        runner.Group("CD Player: the progress bar");

        runner.Add("it follows the track when something is playing", () =>
        {
            var featured = new Featured(new AlbumPicker(11), 0.4);
            featured.Reset(0, 2);

            Check.Close(0.37, featured.Progress(3, 30, live: 0.37), 0.0001, "the real position");
            Check.Close(0.0, featured.Progress(3, 30, live: 0.0), 0.0001, "the start of a track");
            Check.Close(1.0, featured.Progress(3, 30, live: 1.0), 0.0001, "and the end of one");
        });

        runner.Add("it sweeps over the idle span when nothing is", () =>
        {
            // A bar frozen at nothing would read as a stalled screen rather than
            // an idle one.
            var featured = new Featured(new AlbumPicker(11), 0.4);
            featured.Reset(0, 2);

            Check.Close(0.0, featured.Progress(0, 30, null), 0.0001, "empty at the start");
            Check.Close(0.5, featured.Progress(15, 30, null), 0.0001, "half way");
            Check.Close(1.0, featured.Progress(30, 30, null), 0.0001, "and full at the end");
        });

        runner.Add("it never runs past the end of the bar", () =>
        {
            var featured = new Featured(new AlbumPicker(11), 0.4);
            featured.Reset(0, 2);

            Check.Close(1.0, featured.Progress(300, 30, null), 0.0001, "long past the span");
            Check.Close(1.0, featured.Progress(3, 30, live: 4.0), 0.0001, "and on a nonsense position");
            Check.Close(0.0, featured.Progress(3, 30, live: -1.0), 0.0001, "or a negative one");
        });

        runner.Add("a very short idle span still sweeps at a readable pace", () =>
        {
            // The same floor the rotation itself uses. A one second sweep would
            // be a flicker, not a progress bar.
            var featured = new Featured(new AlbumPicker(11), 0.4);
            featured.Reset(0, 2);

            Check.True(
                featured.Progress(1, 1, null) < 1.0,
                "a one second span swept the whole bar in a second");
        });

        runner.Add("it resets when the album changes", () =>
        {
            var featured = new Featured(new AlbumPicker(11), 0.4);
            featured.Reset(0, 2);

            Check.Close(0.5, featured.Progress(15, 30, null), 0.0001, "half way through the first");

            featured.Advance(15, 7, 30, albumCount: 20);

            Check.Close(0.0, featured.Progress(15, 30, null), 0.0001, "and back to nothing on the next");
        });
    }
}

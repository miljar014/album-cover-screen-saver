namespace AlbumCoverScreenSaver.Shared.Tests;

/// <summary>
/// CRT Terminal: the phosphor dots, and the readout typing itself out.
/// </summary>
public static class CrtTests
{
    public static void Register(TestRunner runner)
    {
        Dots(runner);
        Typing(runner);
    }

    private static void Dots(TestRunner runner)
    {
        runner.Group("CRT Terminal: the tube");

        runner.Add("light is light, which is the opposite of the newspaper", () =>
        {
            // Newsstand prints ink, so a dark cell gets the big dot. Here the
            // dots are phosphor, so a bright cell does. Same grid, opposite
            // reading, and the two mappings live a few lines apart.
            var bright = CrtTerminal.DotFor(0.9f, 10f, 1f);
            var dim = CrtTerminal.DotFor(0.2f, 10f, 1f);

            Check.True(bright.Diameter > dim.Diameter, "bright cells light bigger dots");
            Check.True(bright.Alpha > dim.Alpha, "and brighter ones");

            Check.False(CrtTerminal.DotFor(0.0f, 10f, 1f).Lit, "black stays black");
            Check.False(CrtTerminal.DotFor(0.06f, 10f, 1f).Lit, "and so does very nearly black");
            Check.True(CrtTerminal.DotFor(0.07f, 10f, 1f).Lit, "but just above it lights");
        });

        runner.Add("both the size and the brightness carry the value", () =>
        {
            // This is what separates it from the Newsstand mapping, where only
            // the size does. Losing either term flattens the picture.
            var full = CrtTerminal.DotFor(1f, 10f, 1f);

            Check.Close(10.8, full.Diameter, 0.0001, "a full dot overruns its own cell");
            Check.Close(1.0, full.Alpha, 0.0001, "and is fully lit");

            var faint = CrtTerminal.DotFor(0.07f, 10f, 1f);
            Check.Close(3.546, faint.Diameter, 0.001, "the faintest printed dot");
            Check.Close(0.3025, faint.Alpha, 0.0001, "is dim but not invisible");
        });

        runner.Add("a change fades the whole picture out, with no cover behind it", () =>
        {
            // The style has no crossfade: the outgoing cover is not drawn at
            // all, so the picture dims to nothing and the new one rises.
            Check.Close(0.0, CrtTerminal.DotFor(1f, 10f, 0f).Alpha, 0.0001, "at the bottom of a change");
            Check.Close(0.5, CrtTerminal.DotFor(1f, 10f, 0.5f).Alpha, 0.0001, "and halfway through it");

            Check.Close(
                10.8, CrtTerminal.DotFor(1f, 10f, 0f).Diameter, 0.0001,
                "the dots keep their size while they fade");
        });

        runner.Add("the portrait fits both a wide screen and a tall one", () =>
        {
            foreach (var (width, height) in new[] { (1920f, 1080f), (1080f, 1920f), (3440f, 1440f) })
            {
                var side = CrtTerminal.PortraitSide(width, height);

                Check.True(side <= height * 0.52f + 0.01f, $"too tall at {width:0}x{height:0}");
                Check.True(side <= width * 0.34f + 0.01f, $"too wide at {width:0}x{height:0}");
                Check.True(side > 0, "and it exists");
            }
        });

        runner.Add("the readout column never runs off the right of the screen", () =>
        {
            foreach (var (width, height) in new[] { (1280f, 720f), (1920f, 1080f), (3440f, 1440f) })
            {
                var side = CrtTerminal.PortraitSide(width, height);
                var left = CrtTerminal.ReadoutLeft(width, side);
                var columnWidth = CrtTerminal.ReadoutWidth(width, left);

                Check.True(columnWidth > 0, $"no room for the words at {width:0}x{height:0}");
                Check.True(
                    left + columnWidth <= width - (width * 0.069f),
                    $"the column reaches the edge at {width:0}x{height:0}");

                Check.True(
                    left > (width * 0.10f) + side,
                    "the column starts right of the portrait, not on top of it");
            }
        });
    }

    private static void Typing(TestRunner runner)
    {
        runner.Group("CRT Terminal: the typing");

        runner.Add("thirty eight characters a second, counted whole", () =>
        {
            Check.Equal(0, CrtTerminal.Budget(4.0, 4.0), "nothing at the moment it starts");
            Check.Equal(38, CrtTerminal.Budget(5.0, 4.0), "a second in");
            Check.Equal(19, CrtTerminal.Budget(4.5, 4.0), "half a second in");

            // Whole characters, not a fraction of one, or the text would slide
            // rather than type.
            Check.Equal(1, CrtTerminal.Budget(4.03, 4.0), "a character at a time");

            Check.Equal(0, CrtTerminal.Budget(3.0, 4.0), "and never anything before it starts");
        });

        runner.Add("the readout is eight lines with music on and seven without", () =>
        {
            var live = CrtTerminal.Lines("Serotonin", "Nic D", "Wide Awake", live: true);
            var idle = CrtTerminal.Lines("Wide Awake", "Nic D", "", live: false);

            Check.Equal(8, live.Count, "playing");
            Check.Equal(7, idle.Count, "not playing");

            Check.Equal("> NOW PLAYING", live[3].Text, "the live dateline");
            Check.Equal("> FROM ARCHIVE", idle[3].Text, "and the idle one");

            Check.Equal("SEROTONIN", live[5].Text, "the title is shouted");
            Check.Equal("NIC D", live[6].Text, "and so is the artist");
        });

        runner.Add("nothing below an unfinished line is ever drawn", () =>
        {
            // A terminal that printed line six while line five was still going
            // would not be a terminal.
            var lines = CrtTerminal.Lines("Serotonin", "Nic D", "Wide Awake", live: true);
            var transcript = CrtTerminal.Reveal(lines, 5);

            Check.Equal(1, transcript.Lines.Count, "only the line being typed");
            Check.Equal("SPOTI", transcript.Lines[0].Text, "as far as it has got");
            Check.True(transcript.Lines[0].Partial, "and it knows it is unfinished");
            Check.False(transcript.Complete, "the block is not out yet");
        });

        runner.Add("a blank line still costs a character, so the pauses are real", () =>
        {
            // Line 0 is nineteen characters and line 1 is six, so twenty five
            // puts both out exactly. Line 2 is blank and prints with nothing
            // left, but it is still charged for, and that charge is what delays
            // the line after it. Without it the blocks would run together.
            var lines = CrtTerminal.Lines("A", "B", "", live: false);

            var spent = CrtTerminal.Reveal(lines, 25);
            Check.False(spent.Lines[2].Partial, "a blank line prints on an empty budget");

            Check.Equal("", CrtTerminal.Reveal(lines, 26).Lines[3].Text, "the next line is held back");
            Check.Equal(">", CrtTerminal.Reveal(lines, 27).Lines[3].Text, "and starts a character late");
        });

        runner.Add("a budget spent on a blank line does not run negative", () =>
        {
            // The specification subtracts one for a blank line and then takes a
            // prefix of whatever is left, which by then can be minus one
            // characters. Clamped here rather than crashing.
            var lines = CrtTerminal.Lines("A", "B", "", live: false);
            var transcript = CrtTerminal.Reveal(lines, 25);

            Check.Equal(4, transcript.Lines.Count, "it kept going past the blank line");
            Check.Equal("", transcript.Lines[3].Text, "with nothing on the next one yet");
            Check.True(transcript.Lines[3].Partial, "which is still going");
        });

        runner.Add("a long enough wait puts the whole block out", () =>
        {
            var lines = CrtTerminal.Lines("Serotonin", "Nic D", "Wide Awake", live: true);
            var transcript = CrtTerminal.Reveal(lines, 1000);

            Check.Equal(lines.Count, transcript.Lines.Count, "every line");
            Check.True(transcript.Complete, "and the block is finished");

            foreach (var shown in transcript.Lines)
            {
                Check.False(shown.Partial, $"line {shown.Index} is still going");
                Check.Equal(lines[shown.Index].Text, shown.Text, $"line {shown.Index}");
            }
        });

        runner.Add("the typing never goes backwards as time passes", () =>
        {
            var lines = CrtTerminal.Lines("Serotonin", "Nic D", "Wide Awake", live: true);
            var previous = 0;

            for (var frame = 0; frame < 30 * 4; frame++)
            {
                var budget = CrtTerminal.Budget(frame / 30.0, 0);
                var out_ = CrtTerminal.Reveal(lines, budget);
                var typed = out_.Lines.Sum(line => line.Text.Length);

                Check.True(typed >= previous, $"went backwards at frame {frame}");
                previous = typed;
            }

            Check.True(previous > 0, "and something was typed at all");
        });

        runner.Add("a whole block is out inside three seconds", () =>
        {
            // The style is a screen saver, not a puzzle. If the block took much
            // longer than this the rotation would change the album before the
            // words had finished arriving.
            var lines = CrtTerminal.Lines("Serotonin", "Nic D", "Wide Awake", live: true);

            Check.True(
                CrtTerminal.Reveal(lines, CrtTerminal.Budget(3.0, 0)).Complete,
                "three seconds is not enough for a typical block");

            Check.False(
                CrtTerminal.Reveal(lines, CrtTerminal.Budget(0.5, 0)).Complete,
                "and half a second should not be");
        });

        runner.Add("the cursor blinks once a second, half on and half off", () =>
        {
            Check.True(CrtTerminal.CursorVisible(0.0), "on at the start");
            Check.True(CrtTerminal.CursorVisible(0.49), "still on just before the half");
            Check.False(CrtTerminal.CursorVisible(0.51), "off after it");
            Check.True(CrtTerminal.CursorVisible(1.01), "and on again a second later");
        });

        runner.Add("the block stays put as lines are added to it", () =>
        {
            // Pulled up by a fixed amount per line, so an eight line block and a
            // seven line one are both centred rather than the longer one
            // growing off the bottom.
            const float height = 1080f;

            var eight = CrtTerminal.BlockTop(height, 8);
            var seven = CrtTerminal.BlockTop(height, 7);

            Check.True(eight < seven, "the longer block starts higher");
            Check.Close(height * 0.021, seven - eight, 0.01, "by exactly one line's worth");
            Check.True(eight > 0, "and neither starts off the top of the screen");
        });
    }
}

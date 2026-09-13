namespace AlbumCoverScreenSaver.Shared.Tests;

/// <summary>
/// The Screens section: naming a display, listing them, and the arrangement map.
/// </summary>
/// <remarks>
/// None of this can be seen on the build machine, which has one display. That is
/// exactly why it is written as arithmetic in the shared library rather than as
/// drawing code: the picture cannot be checked here, but every number in it can.
/// </remarks>
public static class DisplayMapTests
{
    private static DisplayCard Card(string key, int x, int y, int w, int h, bool primary = false) =>
        new(key, key, [x, y, w, h], primary, true);

    public static void Register(TestRunner runner)
    {
        Keys(runner);
        Listing(runner);
        Layout(runner);
        Choices(runner);
    }

    private static void Keys(TestRunner runner)
    {
        runner.Group("Screens: naming a display");

        runner.Add("the device path wins when there is one", () =>
        {
            const string path = @"\\?\DISPLAY#DEL4099#5&1a2b3c&0&UID4353#{guid}";

            Check.Equal(path, DisplayKeys.For(path, 1, 2, 3), "the path");
            Check.Equal(path, DisplayKeys.For("  " + path + "  ", 1, 2, 3), "trimmed");
        });

        runner.Add("an empty path falls back to the monitor's own identity", () =>
        {
            // Which is not a rare branch during development: a virtual machine
            // is a virtual target, and those are exactly the ones Windows gives
            // no device path for. On the build machine this is the only branch.
            foreach (var missing in new string?[] { null, "", "   " })
            {
                Check.Equal(
                    "edid-16553-16537-1", DisplayKeys.For(missing, 16553, 16537, 1),
                    $"from {missing ?? "null"}");
            }
        });

        runner.Add("two monitors on different ports get different keys", () =>
        {
            // Identical models differ only by connector. That is the one case
            // the specification says may swap if the cables are swapped, and it
            // is accepted; what must not happen is both getting one key and
            // sharing a setting.
            Check.True(
                DisplayKeys.For("", 16553, 16537, 0) != DisplayKeys.For("", 16553, 16537, 1),
                "two identical monitors share a key");
        });

        runner.Add("a display always ends up with some key", () =>
        {
            // A display with no key cannot be configured at all, which is worse
            // than one whose key is positional and occasionally wrong.
            Check.Equal("display-2", DisplayKeys.FromDeviceName(null, 2), "nothing at all");
            Check.Equal(@"name-\\.\DISPLAY1", DisplayKeys.FromDeviceName(@"\\.\DISPLAY1", 0), "a device name");
        });
    }

    private static void Listing(TestRunner runner)
    {
        runner.Group("Screens: which displays are listed");

        runner.Add("a display that is not plugged in is still listed", () =>
        {
            // Unplugging a monitor and plugging it back in has to restore what
            // was chosen for it. A window that forgot a display the moment it
            // was unplugged also could not be used to fix a monitor that is
            // simply switched off.
            var remembered = new Dictionary<string, DisplaySetting>
            {
                ["gone"] = new() { Mode = "crt", FriendlyName = "DELL U2720Q", LastRect = [1920, 0, 1920, 1080] },
            };

            var cards = DisplayMap.Listing([Card("here", 0, 0, 1920, 1080, primary: true)], remembered);

            Check.Equal(2, cards.Count, "both listed");
            Check.True(cards[0].Connected, "the connected one first");
            Check.False(cards[1].Connected, "and the absent one after");
            Check.Equal("DELL U2720Q", cards[1].FriendlyName, "remembered by name");
        });

        runner.Add("a connected display is not listed twice", () =>
        {
            var remembered = new Dictionary<string, DisplaySetting>
            {
                ["here"] = new() { Mode = "crt", FriendlyName = "Stale name" },
            };

            var cards = DisplayMap.Listing([Card("here", 0, 0, 1920, 1080)], remembered);

            Check.Equal(1, cards.Count, "once");
            Check.True(cards[0].Connected, "as connected");
        });

        runner.Add("an absent display with no remembered size still gets one", () =>
        {
            // It has to be drawn on the map somehow, and a rectangle of no size
            // is a rectangle nobody can click.
            var remembered = new Dictionary<string, DisplaySetting> { ["gone"] = new() };
            var cards = DisplayMap.Listing([], remembered);

            Check.Equal(1, cards.Count, "listed");
            Check.True(cards[0].Rect[2] > 0 && cards[0].Rect[3] > 0, "with a usable size");
        });

        runner.Add("nothing at all is not a crash", () =>
        {
            Check.Equal(0, DisplayMap.Listing([], new Dictionary<string, DisplaySetting>()).Count, "empty");
        });
    }

    private static void Layout(TestRunner runner)
    {
        runner.Group("Screens: the arrangement map");

        runner.Add("two side by side stay side by side", () =>
        {
            // The whole point of the picture. Scaling each rectangle to fill the
            // box independently would be easier and would destroy the only
            // information it carries.
            var tiles = DisplayMap.Layout(
                [Card("left", 0, 0, 1920, 1080), Card("right", 1920, 0, 1920, 1080)],
                400f, 200f);

            Check.Equal(2, tiles.Count, "both drawn");
            Check.True(tiles[1].X > tiles[0].X, "the right one is to the right");
            Check.Close(tiles[0].Y, tiles[1].Y, 0.01, "and level with it");
            Check.Close(tiles[0].Width, tiles[1].Width, 0.01, "same size on screen, same size in life");
        });

        runner.Add("a screen above and to the left looks above and to the left", () =>
        {
            // Negative coordinates are the normal case for a second monitor
            // placed left of the main one, and getting the offset wrong puts it
            // off the map entirely.
            var tiles = DisplayMap.Layout(
                [Card("main", 0, 0, 1920, 1080), Card("upleft", -1280, -200, 1280, 720)],
                400f, 200f);

            Check.True(tiles[1].X < tiles[0].X, "to the left");
            Check.True(tiles[1].Y < tiles[0].Y, "and above");
            Check.True(tiles[1].X >= 0, "but still on the map");
        });

        runner.Add("a bigger monitor is drawn bigger", () =>
        {
            var tiles = DisplayMap.Layout(
                [Card("4k", 0, 0, 3840, 2160), Card("hd", 3840, 0, 1920, 1080)],
                400f, 200f);

            Check.Close(2.0, tiles[0].Width / tiles[1].Width, 0.01, "twice as wide");
            Check.Close(2.0, tiles[0].Height / tiles[1].Height, 0.01, "and twice as tall");
        });

        runner.Add("everything stays inside the box", () =>
        {
            foreach (var (boxWidth, boxHeight) in new[] { (400f, 200f), (200f, 400f), (120f, 60f) })
            {
                var tiles = DisplayMap.Layout(
                    [
                        Card("a", 0, 0, 3840, 2160),
                        Card("b", 3840, 400, 1920, 1080),
                        Card("c", -1280, -720, 1280, 720),
                    ],
                    boxWidth, boxHeight);

                foreach (var tile in tiles)
                {
                    Check.True(tile.X >= -0.01f, $"{tile.Key} off the left at {boxWidth:0}x{boxHeight:0}");
                    Check.True(tile.Y >= -0.01f, $"{tile.Key} off the top");
                    Check.True(tile.X + tile.Width <= boxWidth + 0.01f, $"{tile.Key} off the right");
                    Check.True(tile.Y + tile.Height <= boxHeight + 0.01f, $"{tile.Key} off the bottom");
                }
            }
        });

        runner.Add("the arrangement is centred in the room it has", () =>
        {
            // A wide arrangement in a tall box should not sit against the top
            // edge with a hole under it.
            var tiles = DisplayMap.Layout(
                [Card("a", 0, 0, 1920, 1080), Card("b", 1920, 0, 1920, 1080)],
                400f, 400f);

            var top = tiles.Min(tile => tile.Y);
            var bottom = tiles.Max(tile => tile.Y + tile.Height);

            Check.Close(top, 400f - bottom, 0.5, "the gaps above and below differ");
        });

        runner.Add("one display fills the box on its own", () =>
        {
            var tiles = DisplayMap.Layout([Card("only", 0, 0, 1920, 1080)], 400f, 400f, padding: 10f);

            Check.Equal(1, tiles.Count, "one tile");
            Check.Close(380.0, tiles[0].Width, 0.5, "as wide as the padding allows");
        });

        runner.Add("a tiny box does not produce invisible tiles", () =>
        {
            var tiles = DisplayMap.Layout(
                [Card("a", 0, 0, 1920, 1080), Card("b", 20000, 0, 1920, 1080)],
                100f, 50f);

            foreach (var tile in tiles)
            {
                Check.True(tile.Width >= 2f && tile.Height >= 2f, $"{tile.Key} vanished");
            }
        });

        runner.Add("clicking a rectangle picks that display", () =>
        {
            var cards = new[] { Card("left", 0, 0, 1920, 1080), Card("right", 1920, 0, 1920, 1080) };
            var tiles = DisplayMap.Layout(cards, 400f, 200f);

            foreach (var tile in tiles)
            {
                var key = DisplayMap.HitTest(tiles, tile.X + (tile.Width / 2f), tile.Y + (tile.Height / 2f));
                Check.Equal(tile.Key, key, $"the middle of {tile.Key}");
            }

            Check.True(DisplayMap.HitTest(tiles, -5f, -5f) is null, "a click outside everything");
        });

        runner.Add("a box with no room produces nothing rather than nonsense", () =>
        {
            Check.Equal(0, DisplayMap.Layout([Card("a", 0, 0, 1920, 1080)], 4f, 4f).Count, "tiny");
            Check.Equal(0, DisplayMap.Layout([], 400f, 200f).Count, "no displays");
        });
    }

    private static void Choices(TestRunner runner)
    {
        runner.Group("Screens: what a display can be set to");

        runner.Add("every style is offered, with inherit first and off last", () =>
        {
            var choices = DisplayMap.Choices();

            Check.Equal(CollageModes.All.Count + 2, choices.Count, "all nineteen plus two");
            Check.Equal(DisplaySetting.Inherit, choices[0].Id, "same as main comes first");
            Check.Equal(DisplaySetting.Off, choices[^1].Id, "and off is at the bottom");
        });

        runner.Add("every id offered is one a display can actually be set to", () =>
        {
            foreach (var (id, title) in DisplayMap.Choices())
            {
                Check.True(title.Length > 0, $"{id} has no label");

                var setting = new DisplaySetting { Mode = id };

                if (id == DisplaySetting.Off)
                {
                    Check.True(setting.ResolvedMode(CollageMode.Mosaic) is null, "off is off");
                }
                else if (id == DisplaySetting.Inherit)
                {
                    Check.Equal(
                        CollageMode.Drift, setting.ResolvedMode(CollageMode.Drift), "inherit follows the main style");
                }
                else
                {
                    Check.True(
                        setting.ResolvedMode(CollageMode.Mosaic) is not null, $"{id} resolved to nothing");
                }
            }
        });

        runner.Add("a display set to off still gets a window", () =>
        {
            // The one place to be careful. A screen saver that leaves a display
            // uncovered leaves the desktop showing on it, and with a logon
            // required on resume, an uncovered screen is a hole in the lock.
            // Off means black, not absent, and null here means "draw nothing",
            // not "make no window".
            var off = new DisplaySetting { Mode = DisplaySetting.Off };

            Check.True(off.IsOff, "it is off");
            Check.True(off.ResolvedMode(CollageMode.Mosaic) is null, "and draws no style");
        });

        runner.Add("a style this build has never heard of draws the main style", () =>
        {
            // A settings file written by a newer version must not leave a screen
            // dark. The same lesson as the settings decoding rule, one level
            // down.
            var odd = new DisplaySetting { Mode = "hologram" };

            Check.True(odd.IsInherit, "treated as inherit");
            Check.Equal(CollageMode.Wall, odd.ResolvedMode(CollageMode.Wall), "and draws the main style");
        });
    }
}

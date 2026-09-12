namespace AlbumCoverScreenSaver.Shared.Tests;

/// <summary>
/// The brief calls this the classic first bug of a Windows screen saver, and
/// the symptom is not an error: the saver simply does the wrong thing, or
/// nothing, with no way to see why. So it is tested properly, off Windows,
/// before it is ever run.
/// </summary>
public static class SaverCommandLineTests
{
    private static SaverCommandLine Parse(params string[] args) => SaverCommandLine.Parse(args);

    public static void Register(TestRunner runner)
    {
        runner.Group("Screen saver arguments");

        runner.Add("no arguments means show settings", () =>
        {
            Check.Equal(SaverMode.Configure, SaverCommandLine.Parse(null).Mode, "null");
            Check.Equal(SaverMode.Configure, SaverCommandLine.Parse([]).Mode, "empty");
            Check.Equal(SaverMode.Configure, Parse("").Mode, "an empty string");
            Check.Equal(SaverMode.Configure, Parse("   ").Mode, "whitespace");
        });

        runner.Add("runs full screen for /s in any spelling", () =>
        {
            foreach (var form in new[] { "/s", "/S", "-s", "-S", "s" })
            {
                Check.Equal(SaverMode.FullScreen, Parse(form).Mode, form);
                Check.Equal(0, Parse(form).ParentWindow, $"{form} takes no window");
            }
        });

        runner.Add("takes the preview window as a separate argument", () =>
        {
            // This is the form Windows actually uses for the preview.
            var parsed = Parse("/p", "12345");
            Check.Equal(SaverMode.Preview, parsed.Mode, "mode");
            Check.Equal((nint)12345, parsed.ParentWindow, "handle");
        });

        runner.Add("takes the settings window glued on after a colon", () =>
        {
            // And this is the form the Settings button actually uses.
            var parsed = Parse("/c:12345");
            Check.Equal(SaverMode.Configure, parsed.Mode, "mode");
            Check.Equal((nint)12345, parsed.ParentWindow, "handle");
        });

        runner.Add("accepts either separator for either mode", () =>
        {
            foreach (var form in new[] { "/p:9876", "/p=9876", "-P:9876" })
            {
                var parsed = Parse(form);
                Check.Equal(SaverMode.Preview, parsed.Mode, form);
                Check.Equal((nint)9876, parsed.ParentWindow, $"{form} handle");
            }

            foreach (var args in new[] { new[] { "/c", "9876" }, new[] { "-c", "9876" } })
            {
                var parsed = SaverCommandLine.Parse(args);
                Check.Equal(SaverMode.Configure, parsed.Mode, string.Join(" ", args));
                Check.Equal((nint)9876, parsed.ParentWindow, "handle");
            }
        });

        runner.Add("shows settings for a bare /c", () =>
        {
            var parsed = Parse("/c");
            Check.Equal(SaverMode.Configure, parsed.Mode, "mode");
            Check.Equal(0, parsed.ParentWindow, "no window");
            Check.False(parsed.HasParentWindow, "HasParentWindow");
        });

        runner.Add("handles a window number too large for a signed int", () =>
        {
            // Documented as unsigned decimal, and on 64 bit these get large.
            // Parsing narrow would land on zero, and drawing into window zero
            // is how a preview silently paints over the desktop.
            const string big = "4294967295";
            var parsed = Parse("/p", big);
            Check.Equal(SaverMode.Preview, parsed.Mode, "mode");
            Check.Equal(unchecked((nint)4294967295UL), parsed.ParentWindow, "handle");
            Check.NotEqual(0, parsed.ParentWindow, "handle did not collapse to zero");
        });

        runner.Add("accepts a hex window number as well", () =>
        {
            var parsed = Parse("/p:0x1A2B");
            Check.Equal(SaverMode.Preview, parsed.Mode, "mode");
            Check.Equal((nint)0x1A2B, parsed.ParentWindow, "handle");
        });

        runner.Add("falls back to settings when a preview has no window", () =>
        {
            // Better to show settings than to draw into nothing.
            Check.Equal(SaverMode.Configure, Parse("/p").Mode, "bare /p");
            Check.Equal(SaverMode.Configure, Parse("/p", "not-a-number").Mode, "unparseable handle");
            Check.Equal(SaverMode.Configure, Parse("/p:0").Mode, "explicit zero");
        });

        runner.Add("ignores the password argument instead of acting on it", () =>
        {
            // Windows 95 legacy. Doing anything here is worse than doing nothing.
            Check.Equal(SaverMode.Ignore, Parse("/a", "12345").Mode, "/a");
            Check.Equal(SaverMode.Ignore, Parse("/A:12345").Mode, "/A:");
        });

        runner.Add("shows settings for anything it does not recognise", () =>
        {
            foreach (var form in new[] { "/x", "--verbose", "/?", "nonsense" })
            {
                Check.Equal(SaverMode.Configure, Parse(form).Mode, form);
            }
        });

        runner.Add("does not mistake a trailing word for a window number", () =>
        {
            // "/showconfig" is not "/s" with a handle of "howconfig".
            var parsed = Parse("/showconfig");
            Check.Equal(SaverMode.FullScreen, parsed.Mode, "mode comes from the first letter");
            Check.Equal(0, parsed.ParentWindow, "no window was invented");
        });

        runner.Add("survives quotes around the arguments", () =>
        {
            var parsed = SaverCommandLine.Parse(["\"/p\"", "\"12345\""]);
            Check.Equal(SaverMode.Preview, parsed.Mode, "mode");
            Check.Equal((nint)12345, parsed.ParentWindow, "handle");
        });
    }
}

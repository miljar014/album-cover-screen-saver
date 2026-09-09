namespace AlbumCoverScreenSaver.Shared.Tests;

public static class Program
{
    public static int Main()
    {
        Console.WriteLine("Album Cover Screen Saver, Windows port");
        Console.WriteLine("Step 1: the shared library and the file contract");

        var runner = new TestRunner();

        MakeIdTests.Register(runner);
        JsonFormatTests.Register(runner);
        ArchiveTests.Register(runner);
        NowPlayingTests.Register(runner);
        SettingsTests.Register(runner);
        SharedStoreTests.Register(runner);
        FixtureTests.Register(runner);

        var failures = runner.Run();

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? "Step 1 is good."
            : "Step 1 is NOT good. See the failures above.");

        return failures == 0 ? 0 : 1;
    }
}

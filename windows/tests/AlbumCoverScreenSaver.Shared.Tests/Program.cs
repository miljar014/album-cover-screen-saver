namespace AlbumCoverScreenSaver.Shared.Tests;

public static class Program
{
    public static int Main()
    {
        Console.WriteLine("Album Cover Screen Saver, Windows port");
        Console.WriteLine("Shared library: the file contract, the album id rule, the .scr arguments, and the style maths");

        var runner = new TestRunner();

        MakeIdTests.Register(runner);
        JsonFormatTests.Register(runner);
        SaverCommandLineTests.Register(runner);
        EngineMathTests.Register(runner);
        StyleMathTests.Register(runner);
        PaletteTests.Register(runner);
        ScatterTests.Register(runner);
        FeaturedTests.Register(runner);
        CoverFlowTests.Register(runner);
        StarfieldTests.Register(runner);
        SceneMathTests.Register(runner);
        DrumAndPlatformTests.Register(runner);
        HalftoneTests.Register(runner);
        CrtTests.Register(runner);
        CdPlayerTests.Register(runner);
        DeviceTests.Register(runner);
        LastFmTests.Register(runner);
        LastFmSyncTests.Register(runner);
        MusicSourceTests.Register(runner);
        TrayLogicTests.Register(runner);
        SettingsSchemaTests.Register(runner);
        ArchiveTests.Register(runner);
        NowPlayingTests.Register(runner);
        SettingsTests.Register(runner);
        SharedStoreTests.Register(runner);
        FixtureTests.Register(runner);

        var failures = runner.Run();

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? "All good."
            : "NOT good. See the failures above.");

        return failures == 0 ? 0 : 1;
    }
}

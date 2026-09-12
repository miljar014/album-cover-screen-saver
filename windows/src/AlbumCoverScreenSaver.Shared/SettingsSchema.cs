using System.Globalization;

namespace AlbumCoverScreenSaver.Shared;

/// <summary>One control in the settings window.</summary>
public abstract record SettingControl(string Key, string Label, string? Tooltip = null);

/// <summary>A labelled slider with a live readout.</summary>
public sealed record SliderControl(
    string Key,
    string Label,
    double Minimum,
    double Maximum,
    Func<Settings, double> Read,
    Action<Settings, double> Write,
    Func<Settings, double, string> Format,
    string? Tooltip = null,
    bool Whole = false)
    : SettingControl(Key, Label, Tooltip);

public sealed record ToggleControl(
    string Key,
    string Label,
    Func<Settings, bool> Read,
    Action<Settings, bool> Write,
    string? Tooltip = null)
    : SettingControl(Key, Label, Tooltip);

/// <summary>A dropdown of styles. Only Record Player's fallback uses one.</summary>
public sealed record StyleChoiceControl(
    string Key,
    string Label,
    Func<Settings, CollageMode> Read,
    Action<Settings, CollageMode> Write,
    Func<IReadOnlyList<CollageMode>> Choices,
    string? Tooltip = null)
    : SettingControl(Key, Label, Tooltip);

/// <summary>
/// Every control in the settings window, as data.
/// </summary>
/// <remarks>
/// <para>
/// Taken from the macOS settings window, which is the authority: the ranges in
/// doc 00 section 3 are a summary, and a few of them (tile size, tempo) are not
/// listed there at all. Labels, ranges and readout formats are reproduced
/// exactly, so the two platforms present the same product.
/// </para>
/// <para>
/// The readout formats matter more than they look. Doc 00 section 6 singles
/// them out because they read as English rather than as numbers: a flip rate
/// shown as "about twelve per minute" means something, where "3.0" does not.
/// They are worth the awkwardness of carrying a formatting function around.
/// </para>
/// <para>
/// The settings window builds itself from this table rather than hard-coding
/// nineteen layouts, which is the only reason it can be tested at all.
/// </para>
/// </remarks>
public static class SettingsSchema
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>The controls shown for every style.</summary>
    public static IReadOnlyList<SettingControl> Shared { get; } =
    [
        new SliderControl("tileSize", "Tile size", 90, 340,
            s => s.TileSize, (s, v) => s.TileSize = v,
            (_, v) => $"{(int)v} pt"),

        new SliderControl("tempo", "Seconds between changes", 0.5, 15,
            s => s.Tempo, (s, v) => s.Tempo = v,
            (_, v) => v.ToString("0.0", Invariant) + " s"),

        new SliderControl("recencyBias", "Favour recent listening", 0, 1,
            s => s.RecencyBias, (s, v) => s.RecencyBias = v,
            (_, v) => v < 0.05 ? "Whole archive"
                    : v > 0.95 ? "Only recent"
                    : $"{(int)(v * 100)}%"),

        new ToggleControl("showTrackLabel", "Show album and artist name",
            s => s.ShowTrackLabel, (s, v) => s.ShowTrackLabel = v),
    ];

    /// <summary>The controls for one style, in the order they should appear.</summary>
    public static IReadOnlyList<SettingControl> For(CollageMode mode) => mode switch
    {
        CollageMode.Mosaic =>
        [
            new ToggleControl("mosaicRandomSize", "Random cover size",
                s => s.MosaicRandomSize, (s, v) => s.MosaicRandomSize = v,
                "Promotes some covers to double-size blocks, so the wall reads as a "
                + "mosaic rather than a uniform grid."),

            new SliderControl("flipsAtOnce", "Flip rate", 1, 20,
                s => s.FlipsAtOnce, (s, v) => s.FlipsAtOnce = (int)Math.Round(v),
                // Reads as English rather than as a number, and it depends on
                // tempo, which is why formatting takes the whole settings object.
                (s, v) => "≈ " + (60 * v / Math.Max(0.3, s.Tempo)).ToString("0", Invariant)
                          + " per minute",
                Whole: true),

            new SliderControl("flipDuration", "Flip duration", 0.2, 2.5,
                s => s.FlipDuration, (s, v) => s.FlipDuration = v,
                (_, v) => v.ToString("0.00", Invariant) + " s average"),
        ],

        CollageMode.Drift =>
        [
            new SliderControl("driftCount", "Covers on screen", 4, 44,
                s => s.DriftCount, (s, v) => s.DriftCount = (int)Math.Round(v),
                (_, v) => ((int)v).ToString(Invariant), Whole: true),

            new SliderControl("driftSpeed", "Drift speed", 0.2, 4,
                s => s.DriftSpeed, (s, v) => s.DriftSpeed = v, Multiplier),

            new ToggleControl("driftRandomSize", "Random size (depth effect)",
                s => s.DriftRandomSize, (s, v) => s.DriftRandomSize = v,
                "Covers vary in size; smaller ones sit further back, drift more "
                + "slowly and fade into the background."),

            // Greyed out when random size is on, rather than left as a control
            // that silently does nothing. The window handles that.
            new SliderControl("driftScale", "Cover size", 0.4, 2.2,
                s => s.DriftScale, (s, v) => s.DriftScale = v, Multiplier),

            // Not in the macOS settings window, but specified in doc 00 section 3.
            new SliderControl("driftBackdropBlur", "Backdrop blur", 0, 1,
                s => s.DriftBackdropBlur, (s, v) => s.DriftBackdropBlur = v,
                (_, v) => v < 0.02 ? "Sharp" : $"{(int)(v * 100)}%",
                "How out of focus the playing album is behind the drifting covers."),
        ],

        CollageMode.Wall =>
        [
            new SliderControl("wallHold", "Hold when full", 1, 40,
                s => s.WallHold, (s, v) => s.WallHold = v, Seconds),

            new SliderControl("wallBuildSpeed", "Build speed", 0.2, 5,
                s => s.WallBuildSpeed, (s, v) => s.WallBuildSpeed = v, Multiplier),
        ],

        CollageMode.Hero =>
        [
            Follows("heroFollowNowPlaying",
                s => s.HeroFollowNowPlaying, (s, v) => s.HeroFollowNowPlaying = v,
                "Feature what is playing",
                "While music is playing, the large cover stays on the current track "
                + "instead of rotating."),

            new SliderControl("heroSize", "Featured cover size", 0.25, 0.9,
                s => s.HeroSize, (s, v) => s.HeroSize = v,
                (_, v) => $"{(int)(v * 100)}% of height"),

            new SliderControl("heroDim", "Background dimming", 0, 0.85,
                s => s.HeroDim, (s, v) => s.HeroDim = v, Percent),

            new SliderControl("heroInterval", "Seconds between features", 3, 90,
                s => s.HeroInterval, (s, v) => s.HeroInterval = v, Seconds),
        ],

        CollageMode.Vinyl =>
        [
            new StyleChoiceControl("vinylFallbackMode", "When nothing is playing",
                s => s.VinylFallbackMode, (s, v) => s.VinylFallbackMode = v,
                FallbackStyles,
                "The Record Player needs music. This style is shown when nothing "
                + "is playing."),

            Follows("vinylFollowNowPlaying",
                s => s.VinylFollowNowPlaying, (s, v) => s.VinylFollowNowPlaying = v,
                "Play what is playing",
                "The record on the platter follows the current track; otherwise it "
                + "changes on a timer."),

            new SliderControl("vinylSleeveCount", "Records kept on the table", 0, 40,
                s => s.VinylSleeveCount, (s, v) => s.VinylSleeveCount = (int)Math.Round(v),
                CountOrNone, Whole: true),

            new SliderControl("vinylSleeveScale", "Sleeve size", 0.6, 1.9,
                s => s.VinylSleeveScale, (s, v) => s.VinylSleeveScale = v, Multiplier),

            new SliderControl("vinylSecondsPerRecord", "Seconds per record", 20, 600,
                s => s.VinylSecondsPerRecord, (s, v) => s.VinylSecondsPerRecord = v, Seconds),

            new SliderControl("vinylRPM", "Spin speed", 2, 45,
                s => s.VinylRpm, (s, v) => s.VinylRpm = VinylSpeeds.Snap(v),
                (_, v) => VinylSpeeds.Describe(v)),
        ],

        CollageMode.Ambient =>
        [
            Feature("Seconds per album"),
            new SliderControl("ambientMotion", "Colour drift", 0.1, 3,
                s => s.AmbientMotion, (s, v) => s.AmbientMotion = v, Multiplier),
        ],

        CollageMode.Cassette => [Feature("Seconds per album")],

        CollageMode.Crate =>
        [
            new SliderControl("crateCount", "Sleeves in the crate", 6, 40,
                s => s.CrateCount, (s, v) => s.CrateCount = (int)Math.Round(v),
                Count, Whole: true),
            Feature("Seconds per pass"),
        ],

        CollageMode.Gallery =>
        [
            new SliderControl("galleryColumns", "Frames across", 2, 7,
                s => s.GalleryColumns, (s, v) => s.GalleryColumns = (int)Math.Round(v),
                Count, Whole: true),
            Feature("Seconds per album"),
        ],

        CollageMode.CoverFlow =>
        [
            new SliderControl("flowCount", "Covers on the carousel", 5, 50,
                s => s.FlowCount, (s, v) => s.FlowCount = (int)Math.Round(v),
                Count, Whole: true),
            Feature("Seconds per pass"),
        ],

        CollageMode.Jukebox => [Feature("Seconds per record")],

        CollageMode.Starfield =>
        [
            new SliderControl("orbitCount", "Albums in orbit", 3, 36,
                s => s.OrbitCount, (s, v) => s.OrbitCount = (int)Math.Round(v),
                Count, Whole: true),
            new SliderControl("orbitSpeed", "Orbit speed", 0.1, 4,
                s => s.OrbitSpeed, (s, v) => s.OrbitSpeed = v, Multiplier),
            Feature("Seconds per album"),
        ],

        CollageMode.Crt =>
        [
            new ToggleControl("crtAmber", "Amber phosphor (off = green)",
                s => s.CrtAmber, (s, v) => s.CrtAmber = v),
            Feature("Seconds per album"),
        ],

        CollageMode.CdPlayer =>
        [
            new SliderControl("cdCaseCount", "Jewel cases around it", 0, 20,
                s => s.CdCaseCount, (s, v) => s.CdCaseCount = (int)Math.Round(v),
                CountOrNone, Whole: true),
            Feature("Seconds per disc"),
        ],

        CollageMode.Polaroid =>
        [
            new SliderControl("polaroidCount", "Photos kept on the board", 0, 24,
                s => s.PolaroidCount, (s, v) => s.PolaroidCount = (int)Math.Round(v),
                CountOrNone, Whole: true),
            Feature("Seconds per photo"),
        ],

        CollageMode.Zoetrope =>
        [
            new SliderControl("zoetropeCount", "Covers on the drum", 6, 30,
                s => s.ZoetropeCount, (s, v) => s.ZoetropeCount = (int)Math.Round(v),
                Count, Whole: true),
            Feature("Seconds per album"),
        ],

        // Newsstand, Vaporwave Grid and Subway Platform each feature one album
        // and take nothing but the timer.
        _ => [Feature("Seconds per album")],
    };

    /// <summary>Record Player cannot be its own fallback.</summary>
    public static IReadOnlyList<CollageMode> FallbackStyles() =>
        CollageModes.All.Where(mode => mode != CollageMode.Vinyl).ToArray();

    private static SliderControl Feature(string label) =>
        new("featureSeconds", label, 8, 300,
            s => s.FeatureSeconds, (s, v) => s.FeatureSeconds = v, Seconds);

    /// <summary>
    /// The macOS labels say "on Spotify". Windows watches every player and
    /// ships no Spotify source, so naming one would be wrong here.
    /// </summary>
    private static ToggleControl Follows(
        string key, Func<Settings, bool> read, Action<Settings, bool> write,
        string label, string tooltip) => new(key, label, read, write, tooltip);

    private static string Seconds(Settings _, double v) => v.ToString("0", Invariant) + " s";

    private static string Multiplier(Settings _, double v) =>
        v.ToString("0.00", Invariant) + "×";

    private static string Percent(Settings _, double v) => $"{(int)(v * 100)}%";

    private static string Count(Settings _, double v) => ((int)v).ToString(Invariant);

    /// <summary>Zero is a real choice for some counts, and "0" reads worse than "none".</summary>
    private static string CountOrNone(Settings _, double v) =>
        v < 0.5 ? "none" : ((int)v).ToString(Invariant);
}

/// <summary>The record speeds a turntable actually has.</summary>
public static class VinylSpeeds
{
    /// <summary>33 and a third for an LP, 45 for a single.</summary>
    public static readonly double[] Standard = [33.33, 45.0];

    /// <summary>
    /// Pulls the slider onto a real speed when it lands near one, so hitting
    /// 33 and a third does not require a steady hand.
    /// </summary>
    public static double Snap(double rpm)
    {
        foreach (var standard in Standard)
        {
            if (Math.Abs(rpm - standard) < 1.2) return standard;
        }
        return rpm;
    }

    public static string Describe(double rpm) =>
        Math.Abs(rpm - 33.33) < 1.0
            ? "33⅓ RPM (true LP)"
            : rpm.ToString("0", CultureInfo.InvariantCulture) + " RPM";
}

namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// Everything the user can choose. Written by the tray app, read by the screen
/// saver, and by nothing else.
/// </summary>
/// <remarks>
/// Every field is optional on the way in and falls back to its own default.
/// This is not politeness, it is a bug fix the macOS build paid for: strict
/// decoding meant a settings file written by an older version failed as a
/// whole, and silently reset every preference the user had ever set. See
/// <see cref="SettingsConverter"/> for how that is enforced.
/// </remarks>
public sealed class Settings
{
    /// <summary>Which style is shown.</summary>
    public CollageMode Mode { get; set; } = CollageMode.Mosaic;

    // --- shared across every style ---

    /// <summary>Target cell size in points. Smaller means more, denser tiles.</summary>
    public double TileSize { get; set; } = 200;

    /// <summary>Seconds between visual changes. Lower means busier.</summary>
    public double Tempo { get; set; } = 4.0;

    /// <summary>0 picks albums uniformly, 1 strongly favours recent listening.</summary>
    public double RecencyBias { get; set; } = 0.4;

    public bool ShowTrackLabel { get; set; } = true;

    /// <summary>How the screens relate to each other. Doc 04 section 4.</summary>
    public MultiMonitorMode MultiMonitor { get; set; } = MultiMonitorMode.Separate;

    /// <summary>
    /// Per-display style and on/off, keyed by <c>monitorDevicePath</c>.
    /// Displays that are not currently connected keep their entry, so
    /// unplugging a monitor and plugging it back in restores the user's choice.
    /// </summary>
    public Dictionary<string, DisplaySetting> Displays { get; set; } = new(StringComparer.Ordinal);

    // --- Mosaic Grid ---

    /// <summary>How many tiles flip together on each change.</summary>
    public int FlipsAtOnce { get; set; } = 1;

    /// <summary>Seconds one tile takes to flip.</summary>
    public double FlipDuration { get; set; } = 0.75;

    /// <summary>Promote some covers to 2x2 so the wall reads as a mosaic, not a checkerboard.</summary>
    public bool MosaicRandomSize { get; set; } = true;

    // --- Drifting Float ---

    public int DriftCount { get; set; } = 16;
    public double DriftSpeed { get; set; } = 1.0;
    public double DriftScale { get; set; } = 1.0;

    /// <summary>Vary cover size and tie speed to it, so nearer covers sweep past faster.</summary>
    public bool DriftRandomSize { get; set; } = true;

    /// <summary>Blur on the now-playing backdrop. 0 is sharp, 1 is a flat wash of colour.</summary>
    public double DriftBackdropBlur { get; set; } = 0.3;

    // --- Slow-Building Wall ---

    public double WallHold { get; set; } = 6.0;
    public double WallBuildSpeed { get; set; } = 1.0;

    // --- Hero + Grid ---

    /// <summary>Fraction of screen height taken by the featured cover.</summary>
    public double HeroSize { get; set; } = 0.56;

    public double HeroDim { get; set; } = 0.35;
    public double HeroInterval { get; set; } = 12.0;
    public bool HeroFollowNowPlaying { get; set; } = true;

    // --- Record Player ---

    /// <summary>Seconds a record plays before the arm lifts, when not following live playback.</summary>
    public double VinylSecondsPerRecord { get; set; } = 180;

    /// <summary>
    /// Drawn platter speed. A real LP turns at 33 and a third, but on a large
    /// screen that reads as frantic rather than hypnotic, so the default is
    /// slower on purpose.
    /// </summary>
    public double VinylRpm { get; set; } = 9;

    public bool VinylFollowNowPlaying { get; set; } = true;

    /// <summary>Records accumulating on the table before the oldest is retired.</summary>
    public int VinylSleeveCount { get; set; } = 26;

    public double VinylSleeveScale { get; set; } = 1.0;

    /// <summary>Record Player needs music. This is what runs when nothing is playing.</summary>
    public CollageMode VinylFallbackMode { get; set; } = CollageMode.Mosaic;

    // --- shared by the single-album styles ---

    /// <summary>Seconds an album is featured when nothing is playing.</summary>
    public double FeatureSeconds { get; set; } = 30;

    public double AmbientMotion { get; set; } = 1.0;
    public int CrateCount { get; set; } = 16;
    public int GalleryColumns { get; set; } = 4;
    public int FlowCount { get; set; } = 16;
    public int OrbitCount { get; set; } = 14;
    public double OrbitSpeed { get; set; } = 1.0;

    /// <summary>Amber phosphor when true, green when false.</summary>
    public bool CrtAmber { get; set; } = true;

    public int CdCaseCount { get; set; } = 9;
    public int PolaroidCount { get; set; } = 9;
    public int ZoetropeCount { get; set; } = 14;

    /// <summary>A fresh set of defaults.</summary>
    public static Settings Default => new();

    /// <summary>
    /// The stored setting for a display, or a fresh inherit-and-enabled one for
    /// a display that has never been seen. Never returns null: an unrecognised
    /// display must draw, not go dark.
    /// </summary>
    public DisplaySetting ForDisplay(string key) =>
        Displays.TryGetValue(key, out var setting) ? setting : new DisplaySetting();

    /// <summary>
    /// The style a given display should draw. Null means off, which is still a
    /// window filled with solid black.
    /// </summary>
    public CollageMode? ModeForDisplay(string key) => ForDisplay(key).ResolvedMode(Mode);
}

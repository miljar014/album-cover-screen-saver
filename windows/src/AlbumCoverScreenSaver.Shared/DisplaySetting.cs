namespace AlbumCoverScreenSaver.Shared;

/// <summary>How the screens relate to one another. See doc 04 section 4.</summary>
public enum MultiMonitorMode
{
    /// <summary>Each screen picks its own albums on its own clock. The default.</summary>
    Separate,

    /// <summary>Every screen draws from one shared sequence on one shared clock.</summary>
    Linked,
}

public static class MultiMonitorModes
{
    public static string Id(this MultiMonitorMode mode) =>
        mode == MultiMonitorMode.Linked ? "linked" : "separate";

    /// <summary>
    /// Label for the settings window. Never "Span" or "Stretch": Windows uses
    /// both for wallpaper that is cut across the bezels, and this deliberately
    /// does not do that.
    /// </summary>
    public static string Title(this MultiMonitorMode mode) =>
        mode == MultiMonitorMode.Linked ? "Screens change together" : "Each screen independent";

    public static MultiMonitorMode ParseOr(string? id, MultiMonitorMode fallback) =>
        string.Equals(id, "linked", StringComparison.OrdinalIgnoreCase) ? MultiMonitorMode.Linked
        : string.Equals(id, "separate", StringComparison.OrdinalIgnoreCase) ? MultiMonitorMode.Separate
        : fallback;
}

/// <summary>
/// What one display shows, keyed in settings by its <c>monitorDevicePath</c>.
/// See doc 04 sections 2 and 4.
/// </summary>
public sealed class DisplaySetting
{
    public const string Inherit = "inherit";
    public const string Off = "off";

    /// <summary>
    /// <c>"inherit"</c>, any style id, or <c>"off"</c>. Anything unrecognised
    /// is treated as inherit, because a display whose key is not understood
    /// must never end up showing nothing.
    /// </summary>
    public string Mode { get; set; } = Inherit;

    /// <summary>For the settings window only. Never used to match a display.</summary>
    public string FriendlyName { get; set; } = "";

    /// <summary>
    /// Virtual-desktop rectangle as [x, y, width, height], for drawing the
    /// arrangement map. UI only.
    /// </summary>
    public int[] LastRect { get; set; } = new int[4];

    public DateTime LastSeen { get; set; } = SharedJson.DistantPast;

    public bool IsOff => string.Equals(Mode, Off, StringComparison.OrdinalIgnoreCase);

    /// <summary>True for an explicit inherit and for anything unrecognised.</summary>
    public bool IsInherit => !IsOff && !CollageModes.TryParse(Mode, out _);

    /// <summary>
    /// The style this display should draw, given the global style.
    /// Null means off, which still gets a window: off is solid black, not
    /// absent, or the display is left uncovered and the lock screen has a hole
    /// in it.
    /// </summary>
    public CollageMode? ResolvedMode(CollageMode global)
    {
        if (IsOff) return null;
        return CollageModes.TryParse(Mode, out var mode) ? mode : global;
    }

    public DisplaySetting Clone() => new()
    {
        Mode = Mode,
        FriendlyName = FriendlyName,
        LastRect = (int[])LastRect.Clone(),
        LastSeen = LastSeen,
    };
}

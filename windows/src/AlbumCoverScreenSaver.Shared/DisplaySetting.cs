namespace AlbumCoverScreenSaver.Shared;

/// <summary>How the screens relate to one another. See doc 04 section 4.</summary>
public enum MultiMonitorMode
{
    /// <summary>Each screen picks its own albums on its own clock. The default.</summary>
    Separate,

    /// <summary>Every screen draws from one shared sequence on one shared clock.</summary>
    Linked,

    /// <summary>
    /// One composition spread across every screen, each drawing its own part.
    /// </summary>
    /// <remarks>
    /// Added after the fact, at Jared's request. The original design ruled out a
    /// window spanning the screens, for three good reasons that all turn out to
    /// be about the <em>window</em> rather than about the picture. See
    /// <see cref="SpanLayout"/>: there is still one window per screen, each at
    /// its own scaling and refresh rate, and each is simply told it is looking
    /// at part of something larger.
    /// </remarks>
    Span,
}

public static class MultiMonitorModes
{
    public static string Id(this MultiMonitorMode mode) => mode switch
    {
        MultiMonitorMode.Linked => "linked",
        MultiMonitorMode.Span => "span",
        _ => "separate",
    };

    /// <summary>
    /// Label for the settings window.
    /// </summary>
    /// <remarks>
    /// <b>"Screens change together" is never called Span or Stretch.</b> Windows
    /// uses both words for wallpaper cut across the bezels, and that mode
    /// deliberately does not do that: every screen shows a complete picture. The
    /// mode that genuinely does spread one picture across the screens is the one
    /// allowed to say so.
    /// </remarks>
    public static string Title(this MultiMonitorMode mode) => mode switch
    {
        MultiMonitorMode.Linked => "Screens change together",
        MultiMonitorMode.Span => "One picture across all screens",
        _ => "Each screen independent",
    };

    public static string Blurb(this MultiMonitorMode mode) => mode switch
    {
        MultiMonitorMode.Linked =>
            "Each screen shows a complete picture, and they change at the same time "
            + "using the same albums. Nothing is cut across a bezel.",

        MultiMonitorMode.Span =>
            "One composition spread over the whole desk, laid out to match where your "
            + "screens actually are. Only for the styles that are fields of covers; "
            + "the others centre on one subject and would put a bezel through it.",

        _ =>
            "Each screen picks its own albums on its own clock. Two screens running the "
            + "same style show different covers arriving at different moments.",
    };

    /// <summary>Every mode, in the order the settings window should offer them.</summary>
    public static IReadOnlyList<MultiMonitorMode> All { get; } =
        [MultiMonitorMode.Separate, MultiMonitorMode.Linked, MultiMonitorMode.Span];

    /// <summary>
    /// Reads a stored value, falling back rather than failing.
    /// </summary>
    /// <remarks>
    /// A settings file naming a mode this build has never heard of has to leave
    /// the screens drawing something. Falling back to separate is also what an
    /// older build does when it reads "span", which is the right answer: it
    /// cannot spread a picture it has no code for.
    /// </remarks>
    public static MultiMonitorMode ParseOr(string? id, MultiMonitorMode fallback)
    {
        if (string.IsNullOrWhiteSpace(id)) return fallback;

        foreach (var mode in All)
        {
            if (string.Equals(mode.Id(), id.Trim(), StringComparison.OrdinalIgnoreCase)) return mode;
        }

        return fallback;
    }
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

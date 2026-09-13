namespace AlbumCoverScreenSaver.Shared;

/// <summary>Where listening history comes from.</summary>
/// <remarks>
/// <b>The wire values match the macOS build's</b>, which stores this under
/// <c>musicSource</c> in its own preferences. Windows stores it in its own
/// preferences too, and deliberately <em>not</em> in settings.json: that file is
/// the contract between this app and the screen saver, and the saver has no
/// business knowing where the music came from. Because neither build writes it
/// to the shared file, the two cannot disagree about it.
/// </remarks>
public enum MusicSourceKind
{
    /// <summary>Whatever is playing on this PC, read from Windows itself.</summary>
    Local,

    /// <summary>A Last.fm account, which brings a whole history rather than just now.</summary>
    LastFm,
}

/// <summary>Wire ids and the words the settings window shows.</summary>
public static class MusicSourceKinds
{
    private static readonly (MusicSourceKind Kind, string Id, string Title, string Blurb)[] Table =
    [
        (MusicSourceKind.Local, "local", "This PC",
            "Reads whatever is playing on this PC: Spotify, Apple Music, a browser, anything. "
            + "No account, nothing to set up. Builds its collage from what you play from now on."),

        (MusicSourceKind.LastFm, "lastfm", "Last.fm",
            "A free Last.fm account records everything you play, on any service. "
            + "Gives the collage your whole listening history from day one."),
    ];

    /// <summary>Both sources, in the order the settings window should list them.</summary>
    public static IReadOnlyList<MusicSourceKind> All { get; } = Table.Select(row => row.Kind).ToArray();

    public static string Id(this MusicSourceKind kind) => Table[(int)kind].Id;

    public static string Title(this MusicSourceKind kind) => Table[(int)kind].Title;

    public static string Blurb(this MusicSourceKind kind) => Table[(int)kind].Blurb;

    /// <summary>
    /// Reads a stored id, falling back to the source that needs nothing from the
    /// user.
    /// </summary>
    /// <remarks>
    /// <c>spotify</c> is a value the macOS build can store and this one has
    /// never been able to serve, so it lands here as unrecognised and falls back
    /// rather than failing. That is the same lenient rule the settings file
    /// follows, and for the same reason: a preference written by another version
    /// must never leave the app unable to start.
    /// </remarks>
    public static MusicSourceKind ParseOr(string? id, MusicSourceKind fallback = MusicSourceKind.Local)
    {
        if (string.IsNullOrWhiteSpace(id)) return fallback;

        foreach (var row in Table)
        {
            if (string.Equals(row.Id, id.Trim(), StringComparison.OrdinalIgnoreCase)) return row.Kind;
        }

        return fallback;
    }

    /// <summary>
    /// Whether the running source needs replacing.
    /// </summary>
    /// <param name="wanted">What the settings ask for now.</param>
    /// <param name="current">What was built last time.</param>
    /// <param name="usingHistory">Whether the built source can read a history.</param>
    /// <param name="historyAvailable">
    /// Whether one could be built: the settings ask for Last.fm <em>and</em> it
    /// has a key and a username.
    /// </param>
    /// <remarks>
    /// <para>
    /// Two conditions, not one. The chosen source can change, and so can whether
    /// the chosen one is usable yet, because a username is typed some time after
    /// the source is picked. Watching only the first would leave the app reading
    /// this PC forever after a username was finally entered.
    /// </para>
    /// <para>
    /// <b>This is a function rather than four lines inside the poller because
    /// the four lines were wrong.</b> They compared "is the chosen source
    /// usable" against "is a history source built", which is true against false
    /// whenever the chosen source is this PC, so the poller rebuilt itself every
    /// twenty seconds and said so in the log every time. Harmless, and visible
    /// only because it was written down.
    /// </para>
    /// </remarks>
    public static bool ShouldRebuild(
        MusicSourceKind wanted, MusicSourceKind current, bool usingHistory, bool historyAvailable)
    {
        var shouldUseHistory = wanted == MusicSourceKind.LastFm && historyAvailable;

        return wanted != current || shouldUseHistory != usingHistory;
    }

    /// <summary>
    /// Whether a username looks like something Last.fm could accept.
    /// </summary>
    /// <remarks>
    /// Only enough to catch the obvious mistakes before a request is made: a
    /// blank box, a pasted profile URL, an email address. The service itself is
    /// the judge of whether the account exists, and it says so in words the
    /// settings window shows.
    /// </remarks>
    public static bool LooksLikeUsername(string? user)
    {
        if (string.IsNullOrWhiteSpace(user)) return false;

        var trimmed = user.Trim();

        if (trimmed.Length > 64) return false;
        if (trimmed.Contains('/') || trimmed.Contains('@') || trimmed.Contains(' ')) return false;

        return true;
    }

    /// <summary>
    /// Pulls the username out of whatever somebody pasted.
    /// </summary>
    /// <remarks>
    /// People paste the address of their profile page far more often than they
    /// type the bare name, and refusing that is a worse answer than
    /// understanding it.
    /// </remarks>
    public static string CleanUsername(string? user)
    {
        if (string.IsNullOrWhiteSpace(user)) return "";

        var text = user.Trim();

        var marker = text.IndexOf("last.fm/user/", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0) text = text[(marker + "last.fm/user/".Length)..];

        var slash = text.IndexOf('/');
        if (slash >= 0) text = text[..slash];

        var query = text.IndexOf('?');
        if (query >= 0) text = text[..query];

        return text.Trim();
    }
}

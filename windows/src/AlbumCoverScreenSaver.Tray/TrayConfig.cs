using System.Reflection;
using AlbumCoverScreenSaver.Shared;
using Microsoft.Win32;

namespace AlbumCoverScreenSaver.Tray;

/// <summary>
/// This app's own preferences: where the music comes from, and who to ask.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately not in settings.json.</b> That file is the contract between
/// this app and the screen saver, and it is shared byte for byte with the macOS
/// build. The saver does not care where the music came from, and the macOS
/// companion keeps the same two values in its own preferences rather than in the
/// shared file. Putting them in settings.json on one platform only would be a
/// silent disagreement between two builds that are supposed to read each other's
/// files.
/// </para>
/// <para>
/// The registry under HKEY_CURRENT_USER is the Windows equivalent of the
/// UserDefaults the Mac uses: per user, no administrator rights, and somewhere a
/// person can find and undo it.
/// </para>
/// </remarks>
internal static class TrayConfig
{
    private const string Key = @"Software\AlbumCoverScreenSaver";

    // The same names the macOS build uses, so anybody reading both is not
    // looking at two spellings of one idea.
    private const string SourceValue = "musicSource";
    private const string UserValue = "lastfmUser";

    public static MusicSourceKind Source
    {
        get => MusicSourceKinds.ParseOr(Read(SourceValue));
        set => Write(SourceValue, value.Id());
    }

    public static string LastFmUser
    {
        get => Read(UserValue) ?? "";
        set => Write(UserValue, MusicSourceKinds.CleanUsername(value));
    }

    /// <summary>
    /// The API key, baked into this binary when it was built.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One key serves every user of the app, so nobody has to register anything.
    /// It is compiled in from a <c>LastFMKey</c> file beside the build scripts,
    /// which is excluded from the repository: exactly what the macOS build does
    /// with the same file, for the same reason.
    /// </para>
    /// <para>
    /// A build made without that file simply has no key, and the settings window
    /// says so rather than failing at the first request.
    /// </para>
    /// </remarks>
    public static string LastFmKey =>
        typeof(TrayConfig).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "LastFMAPIKey")?.Value?.Trim() ?? "";

    public static bool HasLastFmKey => LastFmKey.Length >= 20;

    /// <summary>Whether the chosen source can actually be used.</summary>
    public static bool IsConfigured =>
        Source != MusicSourceKind.LastFm
        || (HasLastFmKey && MusicSourceKinds.LooksLikeUsername(LastFmUser));

    /// <summary>What the tray menu should call the connected account.</summary>
    public static string? AccountLabel =>
        Source == MusicSourceKind.LastFm && LastFmUser.Length > 0
            ? $"{LastFmUser} · Last.fm"
            : null;

    private static string? Read(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue(name) as string;
        }
        catch (Exception error)
        {
            Log.Failure($"reading the {name} setting", error);
            return null;
        }
    }

    private static void Write(string name, string value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(Key, writable: true);
            key?.SetValue(name, value ?? "");
        }
        catch (Exception error)
        {
            Log.Failure($"saving the {name} setting", error);
        }
    }
}

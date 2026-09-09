using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// Which renderer the screen saver uses. The wire values are the lowercase ids
/// below and they must not change: they are written into settings.json and are
/// shared with the macOS build.
/// </summary>
public enum CollageMode
{
    Mosaic,
    Drift,
    Wall,
    Hero,
    Vinyl,
    Ambient,
    Cassette,
    Crate,
    Gallery,
    CoverFlow,
    Jukebox,
    Starfield,
    Crt,
    CdPlayer,
    Newsstand,
    Vaporwave,
    Polaroid,
    Zoetrope,
    Subway,
}

/// <summary>Wire ids and display names for <see cref="CollageMode"/>.</summary>
public static class CollageModes
{
    private static readonly (CollageMode Mode, string Id, string Title)[] Table =
    [
        (CollageMode.Mosaic,    "mosaic",    "Mosaic Grid"),
        (CollageMode.Drift,     "drift",     "Drifting Float"),
        (CollageMode.Wall,      "wall",      "Slow-Building Wall"),
        (CollageMode.Hero,      "hero",      "Hero + Grid"),
        (CollageMode.Vinyl,     "vinyl",     "Record Player"),
        (CollageMode.Ambient,   "ambient",   "Ambient Field"),
        (CollageMode.Cassette,  "cassette",  "Cassette Deck"),
        (CollageMode.Crate,     "crate",     "Crate Digging"),
        (CollageMode.Gallery,   "gallery",   "Gallery Wall"),
        (CollageMode.CoverFlow, "coverflow", "Cover Flow"),
        (CollageMode.Jukebox,   "jukebox",   "Neon Jukebox"),
        (CollageMode.Starfield, "starfield", "Starfield Orbit"),
        (CollageMode.Crt,       "crt",       "CRT Terminal"),
        (CollageMode.CdPlayer,  "cdplayer",  "CD Player"),
        (CollageMode.Newsstand, "newsstand", "Newsstand"),
        (CollageMode.Vaporwave, "vaporwave", "Vaporwave Grid"),
        (CollageMode.Polaroid,  "polaroid",  "Polaroid Corkboard"),
        (CollageMode.Zoetrope,  "zoetrope",  "Zoetrope"),
        (CollageMode.Subway,    "subway",    "Subway Platform"),
    ];

    private static readonly Dictionary<string, CollageMode> ById =
        Table.ToDictionary(row => row.Id, row => row.Mode, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every style, in the order the settings window should list them.</summary>
    public static IReadOnlyList<CollageMode> All { get; } = Table.Select(row => row.Mode).ToArray();

    public static string Id(this CollageMode mode) => Table[(int)mode].Id;

    public static string Title(this CollageMode mode) => Table[(int)mode].Title;

    public static bool TryParse(string? id, out CollageMode mode)
    {
        if (id is not null && ById.TryGetValue(id, out mode)) return true;
        mode = CollageMode.Mosaic;
        return false;
    }

    /// <summary>Parses a style id, falling back rather than failing.</summary>
    public static CollageMode ParseOr(string? id, CollageMode fallback) =>
        TryParse(id, out var mode) ? mode : fallback;
}

/// <summary>
/// Reads a style id, falling back to Mosaic for anything unrecognised. The
/// macOS build does the same via <c>try?</c>, so a settings file naming a style
/// that does not exist yet loads with a working default instead of failing.
/// </summary>
public sealed class CollageModeConverter : JsonConverter<CollageMode>
{
    public override CollageMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return CollageModes.ParseOr(reader.GetString(), CollageMode.Mosaic);
        }
        return CollageMode.Mosaic;
    }

    public override void Write(Utf8JsonWriter writer, CollageMode value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Id());
}

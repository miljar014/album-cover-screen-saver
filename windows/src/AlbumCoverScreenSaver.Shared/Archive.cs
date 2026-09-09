using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// The permanent, ever-growing record of every album the user has been heard
/// playing. The tray app writes it; the screen saver only reads it.
/// </summary>
public sealed class Archive
{
    /// <summary>Albums keyed by id.</summary>
    public Dictionary<string, AlbumEntry> Albums { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Milliseconds since the epoch of the newest play already ingested.
    /// This is <c>long</c>, not <c>int</c>: a present-day millisecond timestamp
    /// is about 1.79e12 and overflows a 32-bit integer. Swift's <c>Int</c> is
    /// 64-bit on every platform the macOS app runs on, so the two agree.
    /// </summary>
    public long LastCursorMs { get; set; }

    public DateTime Updated { get; set; } = SharedJson.DistantPast;

    /// <summary>
    /// Newest first. Several styles assume index 0 is the most recently played
    /// album, so this projection is part of the contract rather than a
    /// convenience.
    /// </summary>
    /// <remarks>
    /// Ties are broken by id. Swift's <c>sorted(by:)</c> is not a stable sort,
    /// so equal timestamps have no defined order there either; fixing an order
    /// here makes the same archive render the same way twice, which matters
    /// when the fixture data all shares a seeded timestamp.
    /// </remarks>
    public IReadOnlyList<AlbumEntry> ByRecency() =>
        Albums.Values
            .OrderByDescending(album => album.LastPlayed)
            .ThenBy(album => album.Id, StringComparer.Ordinal)
            .ToArray();

    public int Count => Albums.Count;
}

/// <summary>
/// Reads the archive one album at a time, so a single malformed entry costs
/// that entry rather than the user's entire listening history.
/// </summary>
/// <remarks>
/// This is deliberately more forgiving than the macOS build, which decodes the
/// archive strictly and falls back to an empty one. The archive is the only
/// piece of user data here that cannot be regenerated, so losing all of it to
/// one bad record is the wrong trade. Nothing about the file it writes differs.
/// </remarks>
public sealed class ArchiveConverter : JsonConverter<Archive>
{
    public override Archive Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var archive = new Archive();

        if (root.ValueKind != JsonValueKind.Object) return archive;

        if (root.TryGetProperty("albums", out var albums) && albums.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in albums.EnumerateObject())
            {
                try
                {
                    if (property.Value.ValueKind != JsonValueKind.Object) continue;
                    var entry = property.Value.Deserialize<AlbumEntry>(options);
                    if (entry is null) continue;

                    // An entry that lost its id still has one: the key it is filed under.
                    if (string.IsNullOrEmpty(entry.Id)) entry.Id = property.Name;

                    archive.Albums[property.Name] = entry;
                }
                catch (JsonException)
                {
                    // Skip this album and keep the rest.
                }
            }
        }

        if (root.TryGetProperty("lastCursorMs", out var cursor) &&
            cursor.ValueKind == JsonValueKind.Number &&
            cursor.TryGetInt64(out var cursorMs))
        {
            archive.LastCursorMs = cursorMs;
        }

        if (root.TryGetProperty("updated", out var updated) && updated.ValueKind == JsonValueKind.String)
        {
            archive.Updated = SharedJson.ParseDateOrDistantPast(updated.GetString());
        }

        return archive;
    }

    public override void Write(Utf8JsonWriter writer, Archive value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("albums");
        writer.WriteStartObject();
        foreach (var pair in value.Albums.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(pair.Key);
            JsonSerializer.Serialize(writer, pair.Value, options);
        }
        writer.WriteEndObject();

        writer.WriteNumber("lastCursorMs", value.LastCursorMs);
        writer.WriteString("updated", SharedJson.FormatDate(value.Updated));

        writer.WriteEndObject();
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// Reads settings one field at a time, so nothing in the file can cost the user
/// more than the field it appears in.
/// </summary>
/// <remarks>
/// The rule, from doc 00 section 3: decode leniently, falling back to the
/// default per field. A missing key, a key holding the wrong type, a style id
/// that does not exist, a malformed date, a broken display entry: each of those
/// costs one value. None of them may fail the file.
///
/// Getting this wrong is not a crash, which is what makes it dangerous. It
/// looks like the app quietly forgetting every preference the user set.
/// </remarks>
public sealed class SettingsConverter : JsonConverter<Settings>
{
    public override Settings Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var settings = new Settings();

        if (root.ValueKind != JsonValueKind.Object) return settings;

        settings.Mode = Mode(root, "mode", settings.Mode);
        settings.TileSize = Num(root, "tileSize", settings.TileSize);
        settings.Tempo = Num(root, "tempo", settings.Tempo);
        settings.RecencyBias = Num(root, "recencyBias", settings.RecencyBias);
        settings.ShowTrackLabel = Flag(root, "showTrackLabel", settings.ShowTrackLabel);

        settings.MultiMonitor = MultiMonitorModes.ParseOr(
            Text(root, "multiMonitor", null), settings.MultiMonitor);
        settings.Displays = ReadDisplays(root);

        settings.FlipsAtOnce = Whole(root, "flipsAtOnce", settings.FlipsAtOnce);
        settings.FlipDuration = Num(root, "flipDuration", settings.FlipDuration);
        settings.MosaicRandomSize = Flag(root, "mosaicRandomSize", settings.MosaicRandomSize);

        settings.DriftCount = Whole(root, "driftCount", settings.DriftCount);
        settings.DriftSpeed = Num(root, "driftSpeed", settings.DriftSpeed);
        settings.DriftScale = Num(root, "driftScale", settings.DriftScale);
        settings.DriftRandomSize = Flag(root, "driftRandomSize", settings.DriftRandomSize);
        settings.DriftBackdropBlur = Num(root, "driftBackdropBlur", settings.DriftBackdropBlur);

        settings.WallHold = Num(root, "wallHold", settings.WallHold);
        settings.WallBuildSpeed = Num(root, "wallBuildSpeed", settings.WallBuildSpeed);

        settings.HeroSize = Num(root, "heroSize", settings.HeroSize);
        settings.HeroDim = Num(root, "heroDim", settings.HeroDim);
        settings.HeroInterval = Num(root, "heroInterval", settings.HeroInterval);
        settings.HeroFollowNowPlaying = Flag(root, "heroFollowNowPlaying", settings.HeroFollowNowPlaying);

        settings.VinylSecondsPerRecord = Num(root, "vinylSecondsPerRecord", settings.VinylSecondsPerRecord);
        settings.VinylRpm = Num(root, "vinylRPM", settings.VinylRpm);
        settings.VinylFollowNowPlaying = Flag(root, "vinylFollowNowPlaying", settings.VinylFollowNowPlaying);
        settings.VinylSleeveCount = Whole(root, "vinylSleeveCount", settings.VinylSleeveCount);
        settings.VinylSleeveScale = Num(root, "vinylSleeveScale", settings.VinylSleeveScale);
        settings.VinylFallbackMode = Mode(root, "vinylFallbackMode", settings.VinylFallbackMode);

        settings.FeatureSeconds = Num(root, "featureSeconds", settings.FeatureSeconds);
        settings.AmbientMotion = Num(root, "ambientMotion", settings.AmbientMotion);
        settings.CrateCount = Whole(root, "crateCount", settings.CrateCount);
        settings.GalleryColumns = Whole(root, "galleryColumns", settings.GalleryColumns);
        settings.FlowCount = Whole(root, "flowCount", settings.FlowCount);
        settings.OrbitCount = Whole(root, "orbitCount", settings.OrbitCount);
        settings.OrbitSpeed = Num(root, "orbitSpeed", settings.OrbitSpeed);
        settings.CrtAmber = Flag(root, "crtAmber", settings.CrtAmber);
        settings.CdCaseCount = Whole(root, "cdCaseCount", settings.CdCaseCount);
        settings.PolaroidCount = Whole(root, "polaroidCount", settings.PolaroidCount);
        settings.ZoetropeCount = Whole(root, "zoetropeCount", settings.ZoetropeCount);

        return settings;
    }

    public override void Write(Utf8JsonWriter writer, Settings value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteString("mode", value.Mode.Id());
        writer.WriteNumber("tileSize", value.TileSize);
        writer.WriteNumber("tempo", value.Tempo);
        writer.WriteNumber("recencyBias", value.RecencyBias);
        writer.WriteBoolean("showTrackLabel", value.ShowTrackLabel);

        writer.WriteString("multiMonitor", value.MultiMonitor.Id());
        writer.WritePropertyName("displays");
        WriteDisplays(writer, value.Displays);

        writer.WriteNumber("flipsAtOnce", value.FlipsAtOnce);
        writer.WriteNumber("flipDuration", value.FlipDuration);
        writer.WriteBoolean("mosaicRandomSize", value.MosaicRandomSize);

        writer.WriteNumber("driftCount", value.DriftCount);
        writer.WriteNumber("driftSpeed", value.DriftSpeed);
        writer.WriteNumber("driftScale", value.DriftScale);
        writer.WriteBoolean("driftRandomSize", value.DriftRandomSize);
        writer.WriteNumber("driftBackdropBlur", value.DriftBackdropBlur);

        writer.WriteNumber("wallHold", value.WallHold);
        writer.WriteNumber("wallBuildSpeed", value.WallBuildSpeed);

        writer.WriteNumber("heroSize", value.HeroSize);
        writer.WriteNumber("heroDim", value.HeroDim);
        writer.WriteNumber("heroInterval", value.HeroInterval);
        writer.WriteBoolean("heroFollowNowPlaying", value.HeroFollowNowPlaying);

        writer.WriteNumber("vinylSecondsPerRecord", value.VinylSecondsPerRecord);
        writer.WriteNumber("vinylRPM", value.VinylRpm);
        writer.WriteBoolean("vinylFollowNowPlaying", value.VinylFollowNowPlaying);
        writer.WriteNumber("vinylSleeveCount", value.VinylSleeveCount);
        writer.WriteNumber("vinylSleeveScale", value.VinylSleeveScale);
        writer.WriteString("vinylFallbackMode", value.VinylFallbackMode.Id());

        writer.WriteNumber("featureSeconds", value.FeatureSeconds);
        writer.WriteNumber("ambientMotion", value.AmbientMotion);
        writer.WriteNumber("crateCount", value.CrateCount);
        writer.WriteNumber("galleryColumns", value.GalleryColumns);
        writer.WriteNumber("flowCount", value.FlowCount);
        writer.WriteNumber("orbitCount", value.OrbitCount);
        writer.WriteNumber("orbitSpeed", value.OrbitSpeed);
        writer.WriteBoolean("crtAmber", value.CrtAmber);
        writer.WriteNumber("cdCaseCount", value.CdCaseCount);
        writer.WriteNumber("polaroidCount", value.PolaroidCount);
        writer.WriteNumber("zoetropeCount", value.ZoetropeCount);

        writer.WriteEndObject();
    }

    // --- per-field readers. None of these can throw. ---

    private static double Num(JsonElement owner, string name, double fallback) =>
        owner.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var number)
            ? number
            : fallback;

    private static int Whole(JsonElement owner, string name, int fallback)
    {
        if (!owner.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            return fallback;
        }
        if (value.TryGetInt32(out var whole)) return whole;

        // A count written as 16.0 is still a count.
        if (value.TryGetDouble(out var number) && number >= int.MinValue && number <= int.MaxValue)
        {
            return (int)Math.Round(number);
        }
        return fallback;
    }

    private static bool Flag(JsonElement owner, string name, bool fallback) =>
        owner.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static string? Text(JsonElement owner, string name, string? fallback) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : fallback;

    private static CollageMode Mode(JsonElement owner, string name, CollageMode fallback) =>
        CollageModes.ParseOr(Text(owner, name, null), fallback);

    private static Dictionary<string, DisplaySetting> ReadDisplays(JsonElement root)
    {
        var displays = new Dictionary<string, DisplaySetting>(StringComparer.Ordinal);

        if (!root.TryGetProperty("displays", out var owner) || owner.ValueKind != JsonValueKind.Object)
        {
            return displays;
        }

        foreach (var property in owner.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object) continue;

            displays[property.Name] = new DisplaySetting
            {
                Mode = Text(property.Value, "mode", DisplaySetting.Inherit) ?? DisplaySetting.Inherit,
                FriendlyName = Text(property.Value, "friendlyName", "") ?? "",
                LastRect = ReadRect(property.Value),
                LastSeen = SharedJson.ParseDateOrDistantPast(Text(property.Value, "lastSeen", null)),
            };
        }

        return displays;
    }

    private static int[] ReadRect(JsonElement owner)
    {
        var rect = new int[4];

        if (!owner.TryGetProperty("lastRect", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return rect;
        }

        var index = 0;
        foreach (var element in value.EnumerateArray())
        {
            if (index >= 4) break;
            rect[index++] = element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number)
                ? number
                : 0;
        }
        return rect;
    }

    private static void WriteDisplays(Utf8JsonWriter writer, Dictionary<string, DisplaySetting> displays)
    {
        writer.WriteStartObject();

        foreach (var pair in displays.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(pair.Key);
            writer.WriteStartObject();
            writer.WriteString("mode", pair.Value.Mode);
            writer.WriteString("friendlyName", pair.Value.FriendlyName);

            writer.WritePropertyName("lastRect");
            writer.WriteStartArray();
            var rect = pair.Value.LastRect;
            for (var index = 0; index < 4; index++)
            {
                writer.WriteNumberValue(index < rect.Length ? rect[index] : 0);
            }
            writer.WriteEndArray();

            writer.WriteString("lastSeen", SharedJson.FormatDate(pair.Value.LastSeen));
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }
}

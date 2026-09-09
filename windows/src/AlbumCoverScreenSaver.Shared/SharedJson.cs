using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// The JSON dialect both platforms speak.
/// </summary>
/// <remarks>
/// The macOS build sets <c>dateEncodingStrategy = .iso8601</c>, which is
/// Foundation's <c>ISO8601DateFormatter</c> with its default options. That
/// produces, and on the way back in accepts, exactly
/// <c>yyyy-MM-ddTHH:mm:ssZ</c> in UTC with no fractional seconds.
///
/// This matters more than it looks. .NET's own default would write fractional
/// seconds and a local offset, and Foundation would then refuse to read the
/// file at all. So writing is strict: always UTC, always whole seconds.
/// Reading is deliberately looser, because a hand-edited fixture or a file
/// from some future version costs nothing to accept.
/// </remarks>
public static class SharedJson
{
    /// <summary>The one format written. Matches Foundation's <c>.iso8601</c> output.</summary>
    public const string IsoFormat = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'";

    /// <summary>
    /// The "never" sentinel, matching Swift's <c>Date.distantPast</c>, which
    /// serialises as <c>0001-01-01T00:00:00Z</c>.
    /// </summary>
    public static readonly DateTime DistantPast = new(1, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Options for files on disk. Indented, so a human can read them.</summary>
    public static JsonSerializerOptions Pretty { get; } = Create(indented: true);

    /// <summary>Options for anything that does not need to be read by eye.</summary>
    public static JsonSerializerOptions Compact { get; } = Create(indented: false);

    private static JsonSerializerOptions Create(bool indented)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = indented,
            // Foundation does not escape non-ASCII, and neither should this, or
            // an album called "Björk" round-trips into mojibake-looking
            // escapes that differ byte-for-byte between the two platforms.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new IsoDateTimeConverter());
        options.Converters.Add(new CollageModeConverter());
        options.Converters.Add(new ArchiveConverter());
        options.Converters.Add(new SettingsConverter());
        return options;
    }

    /// <summary>
    /// Treats an unspecified-kind <see cref="DateTime"/> as UTC rather than
    /// local. Every date in this contract is an instant in UTC; assuming local
    /// would silently shift stored times by the machine's offset.
    /// </summary>
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    public static string FormatDate(DateTime value) =>
        AsUtc(value).ToString(IsoFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a date, accepting the strict form first and anything reasonable
    /// after it. Returns false rather than throwing, so a bad date costs one
    /// field instead of the whole file.
    /// </summary>
    public static bool TryParseDate(string? text, out DateTime value)
    {
        value = DistantPast;
        if (string.IsNullOrWhiteSpace(text)) return false;

        const DateTimeStyles styles =
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;

        if (DateTimeOffset.TryParseExact(text, IsoFormat, CultureInfo.InvariantCulture, styles, out var exact))
        {
            value = exact.UtcDateTime;
            return true;
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                styles | DateTimeStyles.RoundtripKind, out var loose))
        {
            value = loose.UtcDateTime;
            return true;
        }

        return false;
    }

    public static DateTime ParseDateOrDistantPast(string? text) =>
        TryParseDate(text, out var value) ? value : DistantPast;
}

/// <summary>
/// Reads and writes dates in Foundation's <c>.iso8601</c> form. Never throws:
/// an unreadable date becomes <see cref="SharedJson.DistantPast"/>, which every
/// consumer already treats as "we have never seen this".
/// </summary>
public sealed class IsoDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String &&
            SharedJson.TryParseDate(reader.GetString(), out var parsed))
        {
            return parsed;
        }
        return SharedJson.DistantPast;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(SharedJson.FormatDate(value));
}

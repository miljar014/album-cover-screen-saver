using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace AlbumCoverScreenSaver.Shared;

/// <summary>One album that appeared in the user's listening history.</summary>
public sealed class AlbumEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = "";

    /// <summary>Remote art URL, 300px preferred. The saver never fetches it.</summary>
    [JsonPropertyName("imageURL")]
    public string ImageUrl { get; set; } = "";

    [JsonPropertyName("firstSeen")]
    public DateTime FirstSeen { get; set; } = SharedJson.DistantPast;

    [JsonPropertyName("lastPlayed")]
    public DateTime LastPlayed { get; set; } = SharedJson.DistantPast;

    [JsonPropertyName("playCount")]
    public int PlayCount { get; set; }

    /// <summary>
    /// Derives the id for sources that do not supply a stable one, which is
    /// every source except Spotify.
    /// </summary>
    /// <remarks>
    /// The Swift original is:
    /// <code>
    /// let raw  = "\(artist)|\(album)".lowercased()
    /// let safe = raw.map { $0.isLetter || $0.isNumber ? String($0) : "-" }.joined()
    /// return "lfm-" + String(safe.prefix(80))
    /// </code>
    ///
    /// Two details decide whether the two platforms agree, and disagreeing
    /// means the same album silently appears twice in the archive:
    ///
    /// 1. Swift iterates <c>Character</c>, which is an extended grapheme
    ///    cluster, not a UTF-16 code unit. C#'s <c>foreach (char c in s)</c>
    ///    would split a surrogate pair or a combining sequence in half, so this
    ///    walks text elements instead.
    /// 2. <c>prefix(80)</c> counts those same graphemes, and it is applied
    ///    before the <c>lfm-</c> prefix is added, so the finished id runs to 84
    ///    characters, not 80.
    ///
    /// The id doubles as the art cache filename, which is why every character
    /// that is not a letter or a digit becomes a hyphen.
    /// </remarks>
    public static string MakeId(string? artist, string? album)
    {
        var raw = ((artist ?? "") + "|" + (album ?? "")).ToLowerInvariant();

        var safe = new StringBuilder(84);
        var elements = StringInfo.GetTextElementEnumerator(raw);
        var taken = 0;

        while (elements.MoveNext() && taken < 80)
        {
            var element = (string)elements.Current;
            safe.Append(IsLetterOrNumber(element) ? element : "-");
            taken++;
        }

        return "lfm-" + safe;
    }

    /// <summary>
    /// Mirrors Swift's <c>Character.isLetter || Character.isNumber</c>, which
    /// tests the grapheme's first scalar. <c>Rune.IsNumber</c> is the match for
    /// <c>isNumber</c>; <c>char.IsDigit</c> would be too narrow, since it
    /// covers only decimal digits and misses the other numeric categories.
    /// </summary>
    private static bool IsLetterOrNumber(string element)
    {
        if (element.Length == 0) return false;
        if (!Rune.TryGetRuneAt(element, 0, out var rune)) return false;
        return Rune.IsLetter(rune) || Rune.IsNumber(rune);
    }
}

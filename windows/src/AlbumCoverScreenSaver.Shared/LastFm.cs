using System.Text.Json;

namespace AlbumCoverScreenSaver.Shared;

/// <summary>One play, as Last.fm reports it.</summary>
public readonly record struct Scrobble(
    string Artist, string Album, string Track, string ImageUrl, DateTime PlayedAt);

/// <summary>One album from a listener's all-time top.</summary>
public readonly record struct TopAlbum(string Artist, string Album, string ImageUrl, int PlayCount);

/// <summary>
/// Reading a listening history out of Last.fm.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes a fresh install open as a wall of your own music rather
/// than as four covers and a lot of black. One API key serves every user and
/// reading a history needs only a username, with no sign-in flow at all, which
/// is the whole reason the product can be handed to strangers.
/// </para>
/// <para>
/// Nothing here touches the network. It builds the addresses and reads the
/// replies, so every rule that is easy to get wrong can be proved against a
/// saved reply instead of against the live service.
/// </para>
/// </remarks>
public static class LastFm
{
    public const string BaseUrl = "https://ws.audioscrobbler.com/2.0/";

    /// <summary>How many plays one history sweep asks for.</summary>
    public const int HistoryLimit = 200;

    /// <summary>How many albums the first-run seed asks for.</summary>
    public const int SeedLimit = 150;

    /// <summary>
    /// Where a listener's recent plays are.
    /// </summary>
    /// <param name="since">
    /// The newest play already recorded. Passing it makes the service return
    /// only what has happened since, which is what keeps a sweep cheap once the
    /// archive is established.
    /// </param>
    public static string RecentTracks(string user, string apiKey, int limit, DateTime? since = null)
    {
        var url = Method("user.getrecenttracks", user, apiKey) + $"&limit={limit}";

        if (since is { } from)
        {
            url += $"&from={ToUnixSeconds(from)}";
        }

        return url;
    }

    /// <summary>The one most recent play, which is how now-playing is read.</summary>
    public static string NowPlaying(string user, string apiKey) =>
        RecentTracks(user, apiKey, 1);

    /// <summary>A listener's all-time top albums, for the first-run seed.</summary>
    public static string TopAlbums(string user, string apiKey, int limit) =>
        Method("user.gettopalbums", user, apiKey) + $"&period=overall&limit={limit}";

    private static string Method(string method, string user, string apiKey) =>
        BaseUrl
        + $"?method={Uri.EscapeDataString(method)}"
        + $"&user={Uri.EscapeDataString(user ?? string.Empty)}"
        + $"&api_key={Uri.EscapeDataString(apiKey ?? string.Empty)}"
        + "&format=json";

    public static long ToUnixSeconds(DateTime value) =>
        (long)(SharedJson.AsUtc(value) - DateTime.UnixEpoch).TotalSeconds;

    public static DateTime FromUnixSeconds(long seconds) =>
        DateTime.UnixEpoch.AddSeconds(seconds);

    /// <summary>
    /// The plays in a recent-tracks reply.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two rules here are the difference between a working archive and a broken
    /// one, and both are easy to miss.
    /// </para>
    /// <para>
    /// <b>The track playing right now has no date.</b> It is in the same list as
    /// the history, marked with an attribute rather than left out, and it has no
    /// timestamp because it has not finished. Take it as history and the same
    /// play is ingested again on every sweep, for as long as it is on.
    /// </para>
    /// <para>
    /// <b>Singles have no album.</b> There is nothing to file them under and no
    /// cover to show, so they are skipped rather than recorded under an empty
    /// name.
    /// </para>
    /// </remarks>
    public static List<Scrobble> ParseRecent(string? json)
    {
        var plays = new List<Scrobble>();

        foreach (var track in Tracks(json))
        {
            if (IsNowPlaying(track)) continue;

            if (!track.TryGetProperty("date", out var date)) continue;
            if (!TryUts(date, out var played)) continue;

            if (!TryRead(track, out var scrobble, played)) continue;

            plays.Add(scrobble);
        }

        return plays;
    }

    /// <summary>
    /// The track playing right now, if one is.
    /// </summary>
    /// <remarks>
    /// Identified by its own attribute rather than by being first in the list,
    /// because when nothing is playing the first entry is simply the most recent
    /// finished play and reporting that as live would be wrong.
    /// </remarks>
    public static Scrobble? ParseNowPlaying(string? json)
    {
        foreach (var track in Tracks(json))
        {
            if (!IsNowPlaying(track)) continue;
            if (!TryRead(track, out var scrobble, DateTime.UnixEpoch)) continue;

            return scrobble;
        }

        return null;
    }

    /// <summary>The albums in a top-albums reply, most played first.</summary>
    public static List<TopAlbum> ParseTopAlbums(string? json)
    {
        var albums = new List<TopAlbum>();

        if (!TryRoot(json, out var root)) return albums;
        if (!root.TryGetProperty("topalbums", out var top)) return albums;
        if (!TryArray(top, "album", out var list)) return albums;

        foreach (var entry in list)
        {
            var album = Text(entry, "name");
            var artist = entry.TryGetProperty("artist", out var who) ? Text(who, "name") : "";

            if (string.IsNullOrWhiteSpace(album) || string.IsNullOrWhiteSpace(artist)) continue;

            var plays = 0;
            if (entry.TryGetProperty("playcount", out var count))
            {
                _ = int.TryParse(Value(count), out plays);
            }

            albums.Add(new TopAlbum(artist.Trim(), album.Trim(), BestImage(entry) ?? "", plays));
        }

        return albums;
    }

    /// <summary>
    /// The largest usable image on an entry.
    /// </summary>
    /// <remarks>
    /// The grey star placeholder is refused outright. It is a real URL that
    /// fetches a real picture, so nothing downstream would notice, and the
    /// result is a collage of identical grey squares that looks like a
    /// rendering fault rather than like missing artwork.
    /// </remarks>
    public static string? BestImage(JsonElement entry)
    {
        if (!TryArray(entry, "image", out var images)) return null;

        foreach (var size in new[] { "extralarge", "large", "medium" })
        {
            foreach (var image in images)
            {
                if (!string.Equals(Text(image, "size"), size, StringComparison.OrdinalIgnoreCase)) continue;

                var url = Text(image, "#text");

                if (string.IsNullOrWhiteSpace(url)) continue;
                if (ArtworkUrls.IsPlaceholder(url)) continue;

                return url.Trim();
            }
        }

        return null;
    }

    private static bool TryRead(JsonElement track, out Scrobble scrobble, DateTime played)
    {
        scrobble = default;

        var album = track.TryGetProperty("album", out var albumNode) ? Value(albumNode) : "";
        var artist = track.TryGetProperty("artist", out var artistNode) ? Value(artistNode) : "";
        var name = Text(track, "name");

        // A single has no album to file it under and no cover to show.
        if (string.IsNullOrWhiteSpace(album) || string.IsNullOrWhiteSpace(artist)) return false;

        scrobble = new Scrobble(
            Artist: artist.Trim(),
            Album: album.Trim(),
            Track: (name ?? string.Empty).Trim(),
            ImageUrl: BestImage(track) ?? "",
            PlayedAt: played);

        return true;
    }

    private static bool IsNowPlaying(JsonElement track)
    {
        if (!track.TryGetProperty("@attr", out var attr)) return false;
        if (!attr.TryGetProperty("nowplaying", out var flag)) return false;

        return string.Equals(Value(flag), "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryUts(JsonElement date, out DateTime value)
    {
        value = DateTime.UnixEpoch;

        if (!date.TryGetProperty("uts", out var uts)) return false;
        if (!long.TryParse(Value(uts), out var seconds)) return false;
        if (seconds <= 0) return false;

        value = FromUnixSeconds(seconds);
        return true;
    }

    private static IEnumerable<JsonElement> Tracks(string? json)
    {
        if (!TryRoot(json, out var root)) yield break;
        if (!root.TryGetProperty("recenttracks", out var recent)) yield break;
        if (!TryArray(recent, "track", out var list)) yield break;

        foreach (var track in list) yield return track;
    }

    private static bool TryRoot(string? json, out JsonElement root)
    {
        root = default;

        if (string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
            return root.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads a property that is a list, tolerating the single-item case.
    /// </summary>
    /// <remarks>
    /// A listener with exactly one play gets an object where everyone else gets
    /// an array of one. Treating that as an empty list is how a brand new
    /// account ends up with an archive that never fills.
    /// </remarks>
    private static bool TryArray(JsonElement parent, string name, out List<JsonElement> items)
    {
        items = [];

        if (!parent.TryGetProperty(name, out var node)) return false;

        if (node.ValueKind == JsonValueKind.Array)
        {
            items = node.EnumerateArray().ToList();
            return true;
        }

        if (node.ValueKind == JsonValueKind.Object)
        {
            items = [node];
            return true;
        }

        return false;
    }

    /// <summary>
    /// A named string on an element, whatever shape the service used for it.
    /// </summary>
    private static string Text(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var node)
            ? Value(node)
            : "";

    /// <summary>
    /// One value as a string.
    /// </summary>
    /// <remarks>
    /// Last.fm writes the same field as a bare string in one place and as an
    /// object with a <c>#text</c> member in another, and numbers arrive as
    /// strings about as often as they arrive as numbers.
    /// </remarks>
    private static string Value(JsonElement node) => node.ValueKind switch
    {
        JsonValueKind.String => node.GetString() ?? "",
        JsonValueKind.Number => node.ToString(),
        JsonValueKind.Object => Named(node),
        _ => "",
    };

    /// <summary>
    /// The same field is <c>#text</c> in one reply and <c>name</c> in another.
    /// </summary>
    /// <remarks>
    /// An artist arrives as <c>#text</c> under recent tracks and as <c>name</c>
    /// under top albums. The Mac build tries both in that order at its own call
    /// site; doing it here means neither call site has to know.
    /// </remarks>
    private static string Named(JsonElement node)
    {
        if (node.TryGetProperty("#text", out var text) && Value(text).Length > 0) return Value(text);
        if (node.TryGetProperty("name", out var name)) return Value(name);

        return "";
    }

    /// <summary>
    /// The service's own complaint, when it has one.
    /// </summary>
    /// <remarks>
    /// A refusal is a perfectly well formed reply with a message in it rather
    /// than an HTTP failure, so a caller that only checks the status code sees a
    /// mistyped username as an empty history and reports nothing wrong. The
    /// settings window needs the actual words.
    /// </remarks>
    public static string? ErrorMessage(string? json)
    {
        if (!TryRoot(json, out var root)) return null;
        if (!root.TryGetProperty("message", out var message)) return null;

        var text = Value(message);
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}

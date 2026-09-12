namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// Working out where a cover can be found when the player did not supply one.
/// </summary>
public static class ArtworkUrls
{
    /// <summary>
    /// The image hash Last.fm serves for art it does not have: a grey star
    /// placeholder.
    /// </summary>
    /// <remarks>
    /// Treating it as real artwork fills the collage with identical grey
    /// squares, which looks like a rendering bug rather than missing data.
    /// </remarks>
    public const string LastFmPlaceholderHash = "2a96cbd8b46e442fc41c2b86b821562f";

    public static bool IsPlaceholder(string? url) =>
        !string.IsNullOrEmpty(url) &&
        url.Contains(LastFmPlaceholderHash, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The artwork of last resort: free, keyless, and with better coverage than
    /// Last.fm's own images.
    /// </summary>
    public static string ItunesSearch(string artist, string album)
    {
        // The documented shape joins the terms with "+", so the parts are
        // escaped individually rather than escaping the joined string, which
        // would turn the separator into %2B.
        var terms = new[] { artist ?? "", album ?? "" }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => Uri.EscapeDataString(part.Trim()));

        var term = string.Join("+", terms);
        return $"https://itunes.apple.com/search?term={term}&entity=album&limit=1";
    }

    /// <summary>
    /// Turns the 100 pixel thumbnail iTunes returns into the 600 pixel version.
    /// </summary>
    /// <remarks>
    /// The search result only ever names <c>artworkUrl100</c>. The larger sizes
    /// are not listed anywhere; the URL is simply rewritten, and the bigger file
    /// is there.
    /// </remarks>
    public static string? Upscale(string? artworkUrl100)
    {
        if (string.IsNullOrWhiteSpace(artworkUrl100)) return null;
        return artworkUrl100.Replace("100x100bb", "600x600bb", StringComparison.Ordinal);
    }
}

namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// How the archive grows.
/// </summary>
/// <remarks>
/// The archive is the only thing in this product that cannot be regenerated, so
/// the rules for changing it live here, in one place, where they can be tested.
/// </remarks>
public static class ArchiveUpdates
{
    /// <summary>
    /// Records that an album was heard playing.
    /// </summary>
    /// <remarks>
    /// Call this once per track that starts, not once per poll. The tray app
    /// checks what is playing every three seconds; counting each of those as a
    /// play would inflate a four minute song into eighty of them, and recency
    /// bias would then be driven by how long something was left on rather than
    /// by what was actually listened to.
    ///
    /// <para>
    /// <c>firstSeen</c> only ever moves earlier and <c>lastPlayed</c> only ever
    /// moves later, so an out of order observation, which a history sweep will
    /// produce, cannot rewrite history the wrong way round.
    /// </para>
    /// </remarks>
    /// <returns>True if the archive actually changed.</returns>
    public static bool RecordPlay(
        Archive archive,
        string id,
        string name,
        string artist,
        string imageUrl,
        DateTime playedAt)
    {
        if (archive is null) throw new ArgumentNullException(nameof(archive));
        if (string.IsNullOrEmpty(id)) return false;

        playedAt = SharedJson.AsUtc(playedAt);

        if (!archive.Albums.TryGetValue(id, out var entry))
        {
            archive.Albums[id] = new AlbumEntry
            {
                Id = id,
                Name = name ?? "",
                Artist = artist ?? "",
                ImageUrl = imageUrl ?? "",
                FirstSeen = playedAt,
                LastPlayed = playedAt,
                PlayCount = 1,
            };
            archive.Updated = Later(archive.Updated, playedAt);
            return true;
        }

        entry.PlayCount++;
        if (playedAt > entry.LastPlayed) entry.LastPlayed = playedAt;
        if (entry.FirstSeen == SharedJson.DistantPast || playedAt < entry.FirstSeen)
        {
            entry.FirstSeen = playedAt;
        }

        // Names can arrive empty from one source and filled in from another, so
        // a better value wins but a blank one never overwrites a good one.
        if (string.IsNullOrEmpty(entry.Name) && !string.IsNullOrEmpty(name)) entry.Name = name;
        if (string.IsNullOrEmpty(entry.Artist) && !string.IsNullOrEmpty(artist)) entry.Artist = artist;
        if (string.IsNullOrEmpty(entry.ImageUrl) && !string.IsNullOrEmpty(imageUrl)) entry.ImageUrl = imageUrl;

        archive.Updated = Later(archive.Updated, playedAt);
        return true;
    }

    /// <summary>
    /// Adds an album the user is known to have listened to, without claiming to
    /// know when.
    /// </summary>
    /// <remarks>
    /// Seeded entries are stamped one second after the epoch, so they sort
    /// behind every real play and recency bias still favours actual listening.
    /// An album already in the archive is left completely alone: a real play
    /// always outranks a seed.
    /// </remarks>
    public static bool Seed(
        Archive archive, string id, string name, string artist, string imageUrl, int playCount)
    {
        if (archive is null) throw new ArgumentNullException(nameof(archive));
        if (string.IsNullOrEmpty(id)) return false;
        if (archive.Albums.ContainsKey(id)) return false;

        archive.Albums[id] = new AlbumEntry
        {
            Id = id,
            Name = name ?? "",
            Artist = artist ?? "",
            ImageUrl = imageUrl ?? "",
            FirstSeen = SeedStamp,
            LastPlayed = SeedStamp,
            PlayCount = Math.Max(0, playCount),
        };
        return true;
    }

    /// <summary>One second after the epoch. Behind every real play, ahead of "never".</summary>
    public static readonly DateTime SeedStamp = new(1970, 1, 1, 0, 0, 1, DateTimeKind.Utc);

    private static DateTime Later(DateTime a, DateTime b) => b > a ? b : a;
}

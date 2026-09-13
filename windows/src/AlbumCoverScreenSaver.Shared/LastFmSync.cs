namespace AlbumCoverScreenSaver.Shared;

/// <summary>What one sweep of a listening history did.</summary>
public readonly record struct SweepResult(int Recorded, int Skipped, DateTime? Watermark);

/// <summary>
/// Folding a Last.fm history into the archive.
/// </summary>
/// <remarks>
/// <para>
/// The archive is the only thing in this product that cannot be regenerated, so
/// everything that writes to it goes through <see cref="ArchiveUpdates"/> and
/// everything that decides <em>what</em> to write goes through here.
/// </para>
/// <para>
/// The two halves are different in kind and it matters. A history sweep records
/// real plays with real timestamps. The first-run seed records albums somebody
/// is known to have listened to a great deal, without claiming to know when, and
/// it must never be able to outrank an actual listen.
/// </para>
/// </remarks>
public static class LastFmSync
{
    /// <summary>
    /// Records a batch of plays.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Oldest first, whatever order they arrived in.</b> Last.fm sends
    /// newest first, and the archive's rule is that a play count rises by one
    /// each time while the first-seen and last-played stamps only ever move
    /// outward. Fed newest first the stamps still land correctly, but any future
    /// rule that cares about the order of arrival would see the evening
    /// backwards. Sorting here costs nothing and removes the question.
    /// </para>
    /// <para>
    /// Anything at or before <paramref name="after"/> is dropped rather than
    /// recorded. The service's <c>from</c> parameter is inclusive of the second,
    /// so without this the play on the boundary is counted twice, once per
    /// sweep, forever.
    /// </para>
    /// </remarks>
    public static SweepResult Record(Archive archive, IReadOnlyList<Scrobble> plays)
    {
        if (archive is null) throw new ArgumentNullException(nameof(archive));

        var after = Cursor(archive);

        if (plays is null || plays.Count == 0) return new SweepResult(0, 0, null);

        var recorded = 0;
        var skipped = 0;
        DateTime? newest = null;

        foreach (var play in plays.OrderBy(play => play.PlayedAt))
        {
            if (after is { } since && play.PlayedAt <= since)
            {
                skipped++;
                continue;
            }

            // Checked on the names rather than on the id, because the id rule
            // replaces anything that is not a letter or a digit and therefore
            // turns two empty names into the perfectly valid "lfm--". The
            // parser drops these already; this is the second line, and the
            // archive is the one thing here that cannot be regenerated.
            if (string.IsNullOrWhiteSpace(play.Artist) || string.IsNullOrWhiteSpace(play.Album))
            {
                skipped++;
                continue;
            }

            var id = AlbumEntry.MakeId(play.Artist, play.Album);
            if (string.IsNullOrEmpty(id))
            {
                skipped++;
                continue;
            }

            ArchiveUpdates.RecordPlay(
                archive, id, play.Album, play.Artist, play.ImageUrl, play.PlayedAt);

            recorded++;
            if (newest is null || play.PlayedAt > newest) newest = play.PlayedAt;
        }

        if (newest is { } mark) SetCursor(archive, mark);

        return new SweepResult(recorded, skipped, newest);
    }

    /// <summary>
    /// Where the next sweep resumes from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the archive's own <c>lastCursorMs</c>, which is part of the
    /// contract shared with the macOS build rather than something worked out
    /// here. Deriving it instead from the newest entry would be wrong twice
    /// over: a seeded album is stamped at the epoch and would drag it back to
    /// 1970, and an album played by a different source would move it forward
    /// past history that has never been read.
    /// </para>
    /// <para>
    /// Null on a first run, which asks for the whole history. That is the point
    /// of the step. It can double count the handful of plays another source
    /// already saw today, which moves a play count by one or two and nothing
    /// else; not sweeping at all would mean the history never arrives.
    /// </para>
    /// </remarks>
    public static DateTime? Cursor(Archive archive)
    {
        if (archive is null) throw new ArgumentNullException(nameof(archive));

        return archive.LastCursorMs > 0
            ? DateTime.UnixEpoch.AddMilliseconds(archive.LastCursorMs)
            : null;
    }

    /// <summary>Moves the cursor forward. It never goes back.</summary>
    public static void SetCursor(Archive archive, DateTime played)
    {
        if (archive is null) throw new ArgumentNullException(nameof(archive));

        var ms = (long)(SharedJson.AsUtc(played) - DateTime.UnixEpoch).TotalMilliseconds;

        if (ms > archive.LastCursorMs) archive.LastCursorMs = ms;
    }

    /// <summary>
    /// Fills an empty archive from a listener's most played albums.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole point of the step: a fresh install opens as a wall of
    /// your own music rather than as four covers and a lot of black.
    /// </para>
    /// <para>
    /// It only ever adds. An album already in the archive has been really
    /// played, and a seed must not overwrite that with a stamp from 1970.
    /// Albums with no usable cover are skipped, because a seeded album that
    /// cannot be drawn is a gap in the collage rather than a contribution to it.
    /// </para>
    /// </remarks>
    public static int SeedFrom(Archive archive, IReadOnlyList<TopAlbum> albums, int cap)
    {
        if (archive is null) throw new ArgumentNullException(nameof(archive));
        if (albums is null || cap <= 0) return 0;

        var seeded = 0;

        foreach (var album in albums)
        {
            if (seeded >= cap) break;

            if (string.IsNullOrWhiteSpace(album.Artist) || string.IsNullOrWhiteSpace(album.Album)) continue;

            var id = AlbumEntry.MakeId(album.Artist, album.Album);
            if (string.IsNullOrEmpty(id)) continue;

            // A play count of nothing, deliberately, and the macOS build does
            // the same. The number Last.fm reports is a lifetime total across
            // every device somebody has ever scrobbled from, and writing it into
            // an archive whose other counts are "times seen by this app" would
            // put the two on a scale that has nothing in common.
            if (ArchiveUpdates.Seed(archive, id, album.Album, album.Artist, album.ImageUrl, 0))
            {
                seeded++;
            }
        }

        return seeded;
    }

    /// <summary>
    /// Whether a first-run seed is worth doing at all.
    /// </summary>
    /// <remarks>
    /// Only on an archive that would otherwise leave the screen nearly empty.
    /// Seeding one that already has a session's worth of real listening in it
    /// would bury it under a hundred and fifty albums nobody put on today.
    /// </remarks>
    public const int SeedBelow = 12;

    public static bool NeedsSeeding(Archive archive) =>
        archive is not null && archive.Albums.Count < SeedBelow;
}

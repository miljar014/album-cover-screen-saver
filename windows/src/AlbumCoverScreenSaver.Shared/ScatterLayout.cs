namespace AlbumCoverScreenSaver.Shared;

/// <summary>One record accumulated on a persistent board.</summary>
/// <remarks>
/// <para>
/// <b>Identity is the album id, never an index.</b> The album list reorders
/// every time the archive grows, so an index stored here would quietly come to
/// mean a different record.
/// </para>
/// <para>
/// <b>Position is normalised to 0 to 1 of the width and height, never pixels.</b>
/// A change of resolution then carries the pile with it rather than
/// re-scattering it.
/// </para>
/// <para>
/// Both of those are load-bearing. They are why a record you played an hour ago
/// is still exactly where it was.
/// </para>
/// </remarks>
public sealed record TableSleeve(
    string AlbumId,
    float Nx,
    float Ny,
    float SizeFraction,
    float Angle,
    float DiscPeek,
    float DiscDirection,
    double AddedAt);

/// <summary>One item of a disposable scatter, in pixels.</summary>
/// <remarks>
/// Pixels rather than normalised coordinates, and deliberately so: a scatter is
/// rebuilt whenever the layout changes, so there is nothing to carry across.
/// The distinction from <see cref="TableSleeve"/> is the whole difference
/// between a board and a backdrop.
/// </remarks>
public sealed record Sleeve(
    float CentreX,
    float CentreY,
    float Side,
    float Angle,
    float DiscPeek,
    float DiscDirection,
    int Index);

/// <summary>Where a free spot was found, normalised.</summary>
public readonly record struct TableSpot(float Nx, float Ny, float SizeFraction);

/// <summary>
/// The machinery behind the boards: where to put a new record so it does not
/// land on one already there, and the rules that keep it where it was put.
/// </summary>
/// <remarks>
/// <para>
/// Used by Record Player, where every record played this session accumulates on
/// the table, and by Polaroid Corkboard.
/// </para>
/// <para>
/// The persistence rules are the point of the whole thing, and each of them
/// exists because of something that went wrong without it. They are named in the
/// individual methods.
/// </para>
/// </remarks>
public sealed class ScatterLayout
{
    private readonly Random _random;
    private readonly List<TableSleeve> _table = [];

    private CollageMode? _tableOwner;

    public ScatterLayout(Random random) => _random = random;

    public IReadOnlyList<TableSleeve> Table => _table;

    /// <summary>
    /// Clears the board only when a <em>different style</em> takes it over.
    /// </summary>
    /// <remarks>
    /// Deliberately not called on an archive refresh. Doing that is what used to
    /// wipe the pile every single time a new song was recorded, which on a
    /// listening session is every three minutes.
    /// </remarks>
    public void Claim(CollageMode mode)
    {
        if (_tableOwner == mode) return;
        _table.Clear();
        _tableOwner = mode;
    }

    /// <summary>
    /// Retires the oldest pinned items until the board is within its cap.
    /// </summary>
    /// <remarks>
    /// Oldest <em>pinned</em>, in insertion order, regardless of position or of
    /// how recently the album was played. A cap of zero clears the board.
    /// </remarks>
    public void TrimTo(int cap)
    {
        if (cap < 0) cap = 0;
        while (_table.Count > cap) _table.RemoveAt(0);
    }

    /// <summary>
    /// Puts an album on the board at a spot, if it is not already there.
    /// </summary>
    /// <remarks>
    /// An album already on the board is never re-pinned and never moved. That,
    /// plus <see cref="Claim"/> not firing on a reload, is the entire
    /// persistence contract: items keep their positions across track changes.
    /// </remarks>
    public bool Pin(string albumId, TableSpot? spot, double phase)
    {
        if (spot is not { } where) return false;
        if (_table.Any(item => string.Equals(item.AlbumId, albumId, StringComparison.Ordinal))) return false;

        _table.Add(new TableSleeve(
            AlbumId: albumId,
            Nx: where.Nx,
            Ny: where.Ny,
            SizeFraction: where.SizeFraction,
            Angle: (float)((_random.NextDouble() * 0.48) - 0.24),
            DiscPeek: _random.NextDouble() < 0.45
                ? (float)(0.20 + (_random.NextDouble() * 0.24))
                : 0f,
            DiscDirection: (float)((_random.NextDouble() * 1.4) - 0.7),
            AddedAt: phase));

        return true;
    }

    /// <summary>
    /// Finds a free spot on the board, or gives the emptiest one going.
    /// </summary>
    /// <param name="keepOutRadius">
    /// Zero for no circular keep-out. Used to hold the area under the turntable
    /// clear.
    /// </param>
    /// <param name="keepOutRect">
    /// Null for none. Used to hold a column of typography clear.
    /// </param>
    /// <remarks>
    /// <para>
    /// Nine hundred tries at a genuinely free spot, with the overlap tolerance
    /// loosened after six hundred rather than the item being dropped. A board
    /// that fills up should get more crowded, not stop accepting records.
    /// </para>
    /// <para>
    /// <b>The emptiest-spot fallback is not optional.</b> Returning nothing here
    /// is what made newly played songs silently fail to appear, which looks
    /// exactly like the app having stopped noticing your music.
    /// </para>
    /// </remarks>
    public TableSpot? FindSpot(
        float width, float height,
        float minSizeFraction, float maxSizeFraction, float scale,
        float keepOutCentreX = 0f, float keepOutCentreY = 0f, float keepOutRadius = 0f,
        (float X, float Y, float Width, float Height)? keepOutRect = null)
    {
        if (width <= 10 || height <= 10) return null;

        float RandomSize() =>
            (minSizeFraction + (float)(_random.NextDouble() * (maxSizeFraction - minSizeFraction)))
            * scale;

        for (var attempt = 0; attempt < 900; attempt++)
        {
            var sizeFraction = RandomSize();
            var side = height * sizeFraction;
            var half = side * 0.75f;

            // Cannot fit at all. This aborts the whole search, not the attempt.
            if (width - half <= half || height - half <= half) return null;

            var x = half + (float)(_random.NextDouble() * (width - half - half));
            var y = half + (float)(_random.NextDouble() * (height - half - half));

            if (keepOutRadius > 0f && Distance(x, y, keepOutCentreX, keepOutCentreY) < keepOutRadius + half)
            {
                continue;
            }

            if (keepOutRect is { } box)
            {
                var pad = half * 0.4f;
                if (x >= box.X - pad && x <= box.X + box.Width + pad &&
                    y >= box.Y - pad && y <= box.Y + box.Height + pad)
                {
                    continue;
                }
            }

            // Later attempts accept more overlap rather than giving up.
            var slack = attempt > 600 ? 0.30f : 0.42f;

            var collides = false;
            foreach (var item in _table)
            {
                var itemX = item.Nx * width;
                var itemY = item.Ny * height;
                if (Distance(x, y, itemX, itemY) < ((item.SizeFraction * height) + side) * slack)
                {
                    collides = true;
                    break;
                }
            }

            if (collides) continue;

            return new TableSpot(x / width, y / height, sizeFraction);
        }

        return EmptiestSpot(width, height, minSizeFraction * scale,
            keepOutCentreX, keepOutCentreY, keepOutRadius);
    }

    /// <summary>
    /// The emptiest spot going, at the small end of the size range so it fits
    /// more easily.
    /// </summary>
    /// <remarks>
    /// The circular keep-out still holds, tightened slightly and no longer
    /// padded by the item's own size. The rectangular keep-out and the overlap
    /// rule are dropped entirely: at this point a crowded record is far better
    /// than a missing one.
    /// </remarks>
    private TableSpot? EmptiestSpot(
        float width, float height, float sizeFraction,
        float keepOutCentreX, float keepOutCentreY, float keepOutRadius)
    {
        var side = height * sizeFraction;
        var half = side * 0.75f;

        if (width - half <= half || height - half <= half) return null;

        TableSpot? best = null;
        var bestGap = float.NegativeInfinity;

        for (var attempt = 0; attempt < 300; attempt++)
        {
            var x = half + (float)(_random.NextDouble() * (width - half - half));
            var y = half + (float)(_random.NextDouble() * (height - half - half));

            if (keepOutRadius > 0f && Distance(x, y, keepOutCentreX, keepOutCentreY) < keepOutRadius * 0.92f)
            {
                continue;
            }

            var gap = float.PositiveInfinity;
            foreach (var item in _table)
            {
                gap = Math.Min(gap, Distance(x, y, item.Nx * width, item.Ny * height));
            }

            if (gap <= bestGap) continue;

            bestGap = gap;
            best = new TableSpot(x / width, y / height, sizeFraction);
        }

        return best;
    }

    /// <summary>
    /// The disposable sibling of the board: a field of covers rebuilt once per
    /// layout so nothing shimmers between frames.
    /// </summary>
    /// <remarks>
    /// Note there is no size multiplier here, unlike <see cref="FindSpot"/>, and
    /// the spacing rules are looser. It is a backdrop, and it is thrown away and
    /// rebuilt rather than maintained.
    /// </remarks>
    public List<Sleeve> BuildScatter(
        float width, float height,
        int count, int albumCount, double recencyBias, AlbumPicker picker,
        float minSizeFraction, float maxSizeFraction,
        float keepOutCentreX = 0f, float keepOutCentreY = 0f, float keepOutRadius = 0f,
        bool isPreview = false)
    {
        var want = isPreview ? Math.Max(3, count / 3) : count;
        var scatter = new List<Sleeve>();

        if (want <= 0 || albumCount <= 0 || width <= 10 || height <= 10) return scatter;

        var used = new HashSet<int>();

        for (var attempt = 0; attempt < 2500 && scatter.Count < want; attempt++)
        {
            var side = height *
                (minSizeFraction + (float)(_random.NextDouble() * (maxSizeFraction - minSizeFraction)));
            var half = side * 0.78f;

            if (width - half <= half || height - half <= half) break;

            var x = half + (float)(_random.NextDouble() * (width - half - half));
            var y = half + (float)(_random.NextDouble() * (height - half - half));

            if (keepOutRadius > 0f &&
                Distance(x, y, keepOutCentreX, keepOutCentreY) < keepOutRadius + (half * 0.7f))
            {
                continue;
            }

            var collides = false;
            foreach (var other in scatter)
            {
                if (Distance(x, y, other.CentreX, other.CentreY) < (other.Side + side) * 0.44f)
                {
                    collides = true;
                    break;
                }
            }

            if (collides) continue;

            var index = picker.PickAvoiding(albumCount, recencyBias, used);
            used.Add(index);

            scatter.Add(new Sleeve(
                CentreX: x,
                CentreY: y,
                Side: side,
                Angle: (float)((_random.NextDouble() * 0.40) - 0.20),
                DiscPeek: 0f,
                DiscDirection: (float)((_random.NextDouble() * 1.4) - 0.7),
                Index: index));
        }

        return scatter;
    }

    /// <summary>Seeds an empty board from the archive, newest on top.</summary>
    /// <remarks>
    /// Pinned in reverse, so the most recently played record goes on last and
    /// therefore lands on top of the pile, which is where a person would have
    /// put it down.
    /// </remarks>
    public void Seed(IReadOnlyList<string> albumIdsNewestFirst, int cap, double phase,
        Func<TableSpot?> findSpot)
    {
        if (_table.Count > 0 || cap <= 0) return;

        var take = Math.Min(cap, albumIdsNewestFirst.Count);
        for (var i = take - 1; i >= 0; i--)
        {
            Pin(albumIdsNewestFirst[i], findSpot(), phase);
        }
    }

    private static float Distance(float ax, float ay, float bx, float by)
    {
        var dx = ax - bx;
        var dy = ay - by;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }
}

using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// Decoded cover bitmaps, kept as a least-recently-used cache of 160.
/// </summary>
/// <remarks>
/// Evicted bitmaps are disposed explicitly. The Swift original leans on
/// reference counting to free them; on .NET an SKBitmap holds unmanaged memory
/// that the garbage collector is in no hurry to release, and at full screen
/// resolution 160 covers is a great deal of it.
/// </remarks>
internal sealed class ImageStore : IDisposable
{
    private const int Capacity = 160;

    private readonly SharedStore _store;
    private readonly Dictionary<string, LinkedListNode<string>> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SKBitmap> _bitmaps = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _order = new();

    public ImageStore(SharedStore store) => _store = store;

    public int Count => _bitmaps.Count;

    /// <summary>
    /// The cover for an album, or null if it is not on disk or will not decode.
    /// Every draw path treats null as "draw nothing" rather than as an error.
    /// </summary>
    public SKBitmap? Get(string albumId)
    {
        if (string.IsNullOrEmpty(albumId)) return null;

        if (_bitmaps.TryGetValue(albumId, out var cached))
        {
            Touch(albumId);
            return cached;
        }

        SKBitmap? loaded = null;
        try
        {
            var path = _store.ArtFile(albumId);
            if (File.Exists(path)) loaded = SKBitmap.Decode(path);
        }
        catch (Exception error)
        {
            Log.Write($"cover {albumId} would not decode: {error.Message}");
            loaded = null;
        }

        // A failure is deliberately not cached. Art arrives on disk while the
        // saver is running, and remembering that it was missing once would
        // leave a hole until the next purge.
        if (loaded is null) return null;

        _bitmaps[albumId] = loaded;
        _nodes[albumId] = _order.AddLast(albumId);
        Evict();

        return loaded;
    }

    private void Touch(string albumId)
    {
        if (!_nodes.TryGetValue(albumId, out var node)) return;
        _order.Remove(node);
        _nodes[albumId] = _order.AddLast(albumId);
    }

    private void Evict()
    {
        while (_bitmaps.Count > Capacity && _order.First is { } oldest)
        {
            var key = oldest.Value;
            _order.RemoveFirst();
            _nodes.Remove(key);
            if (_bitmaps.Remove(key, out var bitmap)) bitmap.Dispose();
        }
    }

    public void Purge()
    {
        foreach (var bitmap in _bitmaps.Values) bitmap.Dispose();
        _bitmaps.Clear();
        _nodes.Clear();
        _order.Clear();
    }

    public void Dispose() => Purge();
}

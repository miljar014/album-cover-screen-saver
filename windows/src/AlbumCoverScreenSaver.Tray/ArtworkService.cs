using System.Text.Json;
using AlbumCoverScreenSaver.Shared;

namespace AlbumCoverScreenSaver.Tray;

/// <summary>
/// Gets a cover onto disk, by whatever route works.
/// </summary>
/// <remarks>
/// In order: the artwork the player is already showing, then a free keyless
/// lookup at iTunes. The player's own copy is strictly better when it exists,
/// because it needs no network at all and it is the art that player associates
/// with the record.
/// </remarks>
internal sealed class ArtworkService : IDisposable
{
    /// <summary>
    /// Three at a time, from doc 00 section 4e. Four concurrent downloads
    /// appending to a shared list is what segfaulted the macOS app, with no
    /// error message of any kind.
    /// </summary>
    private const int MaximumConcurrent = 3;

    private static readonly HttpClient Http = CreateClient();

    private readonly SemaphoreSlim _gate = new(MaximumConcurrent, MaximumConcurrent);
    private readonly SharedStore _store;
    private readonly HashSet<string> _givenUp = new(StringComparer.Ordinal);

    public ArtworkService(SharedStore store) => _store = store;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AlbumCoverScreenSaver/1.0");
        return client;
    }

    /// <summary>
    /// Makes sure a cover for this album is on disk, and says whether one is.
    /// </summary>
    public async Task<bool> EnsureArtworkAsync(
        string albumId,
        string artist,
        string album,
        Func<CancellationToken, Task<byte[]?>>? readPlayerArtwork,
        CancellationToken token)
    {
        if (string.IsNullOrEmpty(albumId)) return false;
        if (_store.HasArtwork(albumId)) return true;

        // Some albums simply have no art anywhere. Asking iTunes about them
        // every three seconds forever is pointless traffic.
        if (_givenUp.Contains(albumId)) return false;

        await _gate.WaitAsync(token);
        try
        {
            if (_store.HasArtwork(albumId)) return true;

            if (readPlayerArtwork is not null)
            {
                var bytes = await readPlayerArtwork(token);
                if (bytes is not null && _store.SaveArtwork(albumId, bytes))
                {
                    Log.Write($"artwork for {albumId} came from the player ({bytes.Length} bytes)");
                    return true;
                }
            }

            var url = await LookUpAsync(artist, album, token);
            if (url is null)
            {
                _givenUp.Add(albumId);
                Log.Write($"no artwork found anywhere for {artist} - {album}");
                return false;
            }

            var downloaded = await Http.GetByteArrayAsync(url, token);
            if (_store.SaveArtwork(albumId, downloaded))
            {
                Log.Write($"artwork for {albumId} came from iTunes ({downloaded.Length} bytes)");
                return true;
            }

            _givenUp.Add(albumId);
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            Log.Write($"artwork for {albumId} failed: {error.Message}");
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Asks iTunes where the cover is. Free, keyless, no account.</summary>
    private static async Task<string?> LookUpAsync(string artist, string album, CancellationToken token)
    {
        var search = ArtworkUrls.ItunesSearch(artist, album);

        using var response = await Http.GetAsync(search, token);
        if (!response.IsSuccessStatusCode) return null;

        await using var body = await response.Content.ReadAsStreamAsync(token);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: token);

        if (!document.RootElement.TryGetProperty("results", out var results)) return null;
        if (results.ValueKind != JsonValueKind.Array) return null;

        foreach (var result in results.EnumerateArray())
        {
            if (!result.TryGetProperty("artworkUrl100", out var artwork)) continue;
            if (artwork.ValueKind != JsonValueKind.String) continue;

            var upscaled = ArtworkUrls.Upscale(artwork.GetString());
            if (!string.IsNullOrEmpty(upscaled)) return upscaled;
        }

        return null;
    }

    public void Dispose() => _gate.Dispose();
}

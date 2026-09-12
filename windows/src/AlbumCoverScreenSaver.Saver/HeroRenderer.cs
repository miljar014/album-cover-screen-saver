using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// Hero + Grid. One large cover centred on a dimmed grid of the rest.
/// </summary>
/// <remarks>
/// <para>
/// The strongest reaction to playback of the four base styles. When something is
/// playing and the setting allows it, the featured cover is pinned to whatever
/// is on, the rotation is frozen, and the label switches to the track with a
/// green badge. Everything degrades quietly: art not downloaded yet, or the
/// setting off, and it goes back to rotating on its own.
/// </para>
/// <para>
/// The background grid is denser than Mosaic's at the same setting, and flips at
/// two thirds the rate. It is texture, not subject.
/// </para>
/// </remarks>
internal sealed class HeroRenderer : IStyleRenderer
{
    private readonly SaverData _data;
    private readonly TileField _field;
    private readonly AlbumPicker _picker;
    private readonly Random _random;
    private readonly bool _isPreview;

    private int _index;
    private int _previousIndex;
    private double _swapAt;
    private double _fadeStart = -1;

    public HeroRenderer(SaverData data, TileField field, AlbumPicker picker, Random random, bool isPreview)
    {
        _data = data;
        _field = field;
        _picker = picker;
        _random = random;
        _isPreview = isPreview;
    }

    public void BuildLayout(float width, float height)
    {
        // Always the uniform grid: mosaicRandomSize is ignored here, because a
        // 2x2 block behind the hero competes with it.
        _field.Build(
            width, height,
            TileField.CellTarget(_data.Settings.TileSize, _isPreview) * HeroLayout.GridCellFraction,
            varied: false);

        _index = _data.Albums.Count == 0
            ? 0
            : _picker.Pick(_data.Albums.Count, _data.Settings.RecencyBias);
        _previousIndex = _index;
        _swapAt = HeroLayout.Interval(_data.Settings.HeroInterval);
        _fadeStart = -1;

        Log.Write($"hero layout: {_field.Count} background tiles across {width:0}x{height:0} points");
    }

    public void Advance(double phase, float width, float height)
    {
        _field.AdvanceFlips(phase, _data.Settings.Tempo * 1.5);
        AdvanceHero(phase);
    }

    /// <summary>The album being played, if it is being followed and its art is here.</summary>
    private int? LiveHeroIndex =>
        _data.Settings.HeroFollowNowPlaying ? _data.LiveAlbumIndex : null;

    private void AdvanceHero(double phase)
    {
        if (_fadeStart >= 0 && phase - _fadeStart >= HeroLayout.FadeDuration) _fadeStart = -1;

        if (LiveHeroIndex is { } live)
        {
            // Live playback outranks the rotation entirely. The crossfade fires
            // on a change of album, not on every track, so playing an album
            // through does not make the hero flicker between its tracks.
            if (live != _index)
            {
                _previousIndex = _index;
                _index = live;
                _fadeStart = phase;
            }

            // Pushed back continuously, so when the music stops the rotation
            // resumes one full interval later rather than immediately.
            _swapAt = phase + HeroLayout.Interval(_data.Settings.HeroInterval);
            return;
        }

        if (phase < _swapAt) return;

        _swapAt = phase + HeroLayout.Interval(_data.Settings.HeroInterval);
        _previousIndex = _index;
        _index = PickIdleHero();
        _fadeStart = phase;
    }

    /// <summary>
    /// The hero is the headline, so it leans on recency harder than the grid
    /// behind it: a uniform draw from the newest eighth of the archive rather
    /// than the picker's coin flip. There is no de-duplication, so it can
    /// repeat.
    /// </summary>
    private int PickIdleHero()
    {
        var count = _data.Albums.Count;
        if (count == 0) return 0;

        return HeroLayout.RotationIsNarrowed(count)
            ? _random.Next(0, HeroLayout.IdleRotationBound(count))
            : _picker.Pick(count, _data.Settings.RecencyBias);
    }

    public void Draw(SKCanvas canvas, float width, float height, double phase)
    {
        // Dimmed twice: the tiles at 45% over black, then a scrim at heroDim.
        // At the defaults the grid ends up at about 29% brightness.
        foreach (var tile in _field.Tiles)
        {
            Drawing.DrawTile(canvas, tile, _data.ImageFor(tile.Index), phase, alpha: 0.45f);
        }

        Drawing.Scrim(canvas, width, height, (float)_data.Settings.HeroDim);

        var side = HeroLayout.Side(width, height, _data.Settings.HeroSize);
        var rect = SKRect.Create((width - side) / 2f, (height - side) / 2f, side, side);
        var radius = side * 0.02f;

        var fade = 1f;
        if (_fadeStart >= 0)
        {
            fade = Ease.InOut((float)((phase - _fadeStart) / HeroLayout.FadeDuration));

            // A straight opacity cross with no scale change, unlike the shared
            // featured-cover fade the single-album styles use. The two shadows
            // overlap mid-cross and it reads as slightly heavier for a moment.
            Drawing.DrawImage(
                canvas, _data.ImageFor(_previousIndex), rect, 1f - fade, radius, shadow: true);
        }

        Drawing.DrawImage(canvas, _data.ImageFor(_index), rect, fade, radius, shadow: true);

        DrawHeroLabel(canvas, width, height);
    }

    private void DrawHeroLabel(SKCanvas canvas, float width, float height)
    {
        var playing = _data.NowPlaying;

        if (LiveHeroIndex is not null && !string.IsNullOrEmpty(playing.Track))
        {
            Drawing.DrawLabel(
                canvas, width, height,
                playing.Track,
                $"{playing.Artist} · {playing.AlbumName}",
                badge: "NOW PLAYING",
                _isPreview, _data.Settings.ShowTrackLabel);
            return;
        }

        var albums = _data.Albums;
        if (_index < 0 || _index >= albums.Count) return;

        // Not albums.first as the other three styles use: here the label
        // belongs to whatever the hero is showing.
        Drawing.DrawLabel(
            canvas, width, height,
            albums[_index].Name, albums[_index].Artist, badge: null,
            _isPreview, _data.Settings.ShowTrackLabel);
    }
}

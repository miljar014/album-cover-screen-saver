using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// One display's worth of engine: what it is showing, and its own random
/// source. The equivalent of <c>AlbumCoverSaverView</c> in the macOS build.
/// </summary>
/// <remarks>
/// There is one of these per window, sharing a single <see cref="SaverData"/>.
/// The data is loaded once; the composition is not shared at all. Two screens
/// running Mosaic Grid therefore show different covers arriving at different
/// moments, which is the "each screen independent" default from doc 04 section
/// 4. Each scene seeds its own random source, and that is the whole mechanism:
/// without it two displays would build identical walls.
/// </remarks>
internal sealed class Scene : IDisposable
{
    private readonly SaverData _data;
    private readonly Random _random;
    private readonly bool _isPreview;
    private readonly BlurStore _blur = new();
    private readonly PaletteStore _palettes = new();
    private readonly HalftoneStore _halftones = new();
    private readonly Dictionary<CollageMode, IStyleRenderer> _styles = new();

    private CollageMode? _layoutMode;
    private float _layoutWidth = -1f;
    private float _layoutHeight = -1f;
    private int _layoutGeneration = -1;
    private readonly HashSet<CollageMode> _reportedMissing = [];

    private double _phase;

    /// <summary>
    /// Which display's settings this scene obeys, or null in preview.
    /// </summary>
    /// <remarks>
    /// Set once the window knows which screen it is on. Null means "follow the
    /// main style", which is also what an unrecognised display gets: a screen
    /// whose key is not in the settings must never end up showing nothing.
    /// </remarks>
    public string? DisplayKey
    {
        get => _displayKey;
        set => _displayKey = value;
    }

    private string? _displayKey;

    /// <summary>True when this screen was deliberately turned off.</summary>
    private bool IsOff =>
        !_isPreview && _displayKey is not null && _data.Settings.ForDisplay(_displayKey).IsOff;

    // Starts a long way in the past, so with nothing playing the saver opens
    // straight into Record Player's fallback style rather than showing an idle
    // turntable for twenty seconds first.
    private double _lastLiveAt = -1000;

    public Scene(SaverData data, bool isPreview, int seed)
    {
        _data = data;
        _isPreview = isPreview;
        _random = new Random(seed);

        var picker = new AlbumPicker(_random);

        // One field per grid style rather than one shared between them. They
        // are built at different cell targets and hold different flip state, so
        // sharing would rebuild the grid every time the style changed, which is
        // what the layout check below exists to avoid.
        _styles[CollageMode.Mosaic] =
            new MosaicRenderer(data, new TileField(data, picker, _random), isPreview);

        _styles[CollageMode.Wall] =
            new WallRenderer(data, new TileField(data, picker, _random), _random, isPreview);

        _styles[CollageMode.Hero] =
            new HeroRenderer(data, new TileField(data, picker, _random), picker, _random, isPreview);

        _styles[CollageMode.Drift] =
            new DriftRenderer(data, picker, _random, _blur, isPreview);

        _styles[CollageMode.Gallery] =
            new GalleryRenderer(data, picker, _palettes, isPreview);

        _styles[CollageMode.Ambient] =
            new AmbientRenderer(data, picker, _palettes, isPreview);

        _styles[CollageMode.CoverFlow] =
            new CoverFlowRenderer(data, picker, _palettes, isPreview);

        _styles[CollageMode.Starfield] =
            new StarfieldRenderer(data, picker, _palettes, _random, isPreview);

        _styles[CollageMode.Polaroid] =
            new PolaroidRenderer(data, picker, _palettes, _random, isPreview);

        _styles[CollageMode.Crate] =
            new CrateRenderer(data, picker, _palettes, isPreview);

        _styles[CollageMode.Vaporwave] =
            new VaporwaveRenderer(data, picker, _palettes, isPreview);

        _styles[CollageMode.Zoetrope] =
            new ZoetropeRenderer(data, picker, _palettes, isPreview);

        _styles[CollageMode.Subway] =
            new SubwayRenderer(data, picker, _palettes, isPreview);

        _styles[CollageMode.Newsstand] =
            new NewsstandRenderer(data, picker, _palettes, _halftones);

        // Shares the halftone grid with Newsstand, which is why they were built
        // one after the other.
        _styles[CollageMode.Crt] =
            new CrtRenderer(data, picker, _halftones);

        _styles[CollageMode.CdPlayer] =
            new CdPlayerRenderer(data, picker, _palettes, _random, isPreview);

        _styles[CollageMode.Jukebox] =
            new JukeboxRenderer(data, picker, _palettes, _random, isPreview);

        _styles[CollageMode.Cassette] =
            new CassetteRenderer(data, picker, _palettes, isPreview);

        _styles[CollageMode.Vinyl] =
            new VinylRenderer(data, picker, _palettes, _blur, _random, isPreview);
    }

    /// <summary>
    /// The style actually being drawn.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All nineteen are built. Anything unrecognised still falls back to Mosaic
    /// Grid, which matters because settings.json is shared with the macOS
    /// build: a file naming a style this version has never heard of has to
    /// leave the saver drawing something rather than going black.
    /// </para>
    /// <para>
    /// Record Player's own rule sits on top of that. A turntable with nothing on
    /// it is a dead screen, so after twenty seconds without music the screen is
    /// handed to whichever style the user chose for it.
    /// </para>
    /// </remarks>
    private CollageMode ActiveMode
    {
        get
        {
            // What this screen was told to show, which is the main style unless
            // it was given one of its own. An unrecognised display reads as
            // inherit, so a screen whose key is not in the settings draws the
            // main style rather than nothing.
            var chosen = _displayKey is null
                ? _data.Settings.Mode
                : _data.Settings.ModeForDisplay(_displayKey) ?? _data.Settings.Mode;

            var wanted = Turntable.ActiveMode(
                chosen, _phase, _lastLiveAt, _data.Settings.VinylFallbackMode);

            if (_styles.ContainsKey(wanted)) return wanted;

            if (_reportedMissing.Add(wanted))
            {
                Log.Write($"settings ask for {wanted.Title()}, which is not built yet; drawing Mosaic Grid");
            }

            return CollageMode.Mosaic;
        }
    }

    /// <summary>
    /// What this screen is drawing: itself, or its part of a shared composition.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Spreading happens only when the settings ask for it, there is more than
    /// one screen, this screen follows the main style rather than having one of
    /// its own, and that style is a field of covers rather than something with a
    /// single subject in the middle. See <see cref="SpanLayout"/>.
    /// </para>
    /// <para>
    /// Note what is <em>not</em> here: no second window, no change to any
    /// renderer. A style is handed a larger surface and the canvas is
    /// translated, and it goes on drawing exactly as it did.
    /// </para>
    /// </remarks>
    public SpanCanvas CanvasFor(
        (int X, int Y, int Width, int Height) screen,
        IReadOnlyList<(int X, int Y, int Width, int Height)> arrangement,
        float scale)
    {
        if (scale <= 0f) scale = 1f;

        var own = SpanCanvas.Single(screen.Width / scale, screen.Height / scale);

        if (_isPreview) return own;

        var followsMain = _displayKey is null || _data.Settings.ForDisplay(_displayKey).IsInherit;

        return SpanLayout.ShouldSpan(
            _data.Settings.MultiMonitor, ActiveMode, arrangement?.Count ?? 0, followsMain)
            ? SpanLayout.For(screen, arrangement!, scale)
            : own;
    }

    /// <summary>
    /// One frame: settle the layout, advance the state, then draw. Width and
    /// height are in points, the canvas having already been scaled for this
    /// display's DPI.
    /// </summary>
    public void Render(SKCanvas canvas, float width, float height, double phase, string? diagnostic)
    {
        _phase = phase;

        // Refreshed every frame the now-playing signal is live, which is what
        // the grace period is measured from.
        if (_data.NowPlaying.IsLive) _lastLiveAt = phase;

        // Off means black, not absent. The window is still created and still
        // covers the screen, because a screen saver that leaves a display
        // uncovered leaves the desktop showing on it, and with a logon required
        // on resume an uncovered screen is a hole in the lock.
        if (IsOff)
        {
            canvas.Clear(SKColors.Black);
            return;
        }

        EnsureLayout(width, height);

        var style = _styles[ActiveMode];
        style.Advance(phase, width, height);

        canvas.Clear(SKColors.Black);

        if (_data.Albums.Count == 0)
        {
            Drawing.DrawEmptyState(canvas, width, height, _isPreview);
            return;
        }

        style.Draw(canvas, width, height, phase);

        if (!_isPreview && diagnostic is not null)
        {
            Drawing.DrawDiagnostic(canvas, diagnostic, width, height);
        }
    }

    /// <summary>
    /// Rebuilds the layout on exactly three events: the style changed, the
    /// surface changed size, or the archive was reloaded.
    /// </summary>
    /// <remarks>
    /// Note what is <em>not</em> in that list: changing a setting like tileSize
    /// does not rebuild on its own, so it takes effect at the next real
    /// relayout. That is existing macOS behaviour, kept deliberately.
    /// </remarks>
    private void EnsureLayout(float width, float height)
    {
        var mode = ActiveMode;

        if (_layoutMode == mode
            && _layoutWidth == width
            && _layoutHeight == height
            && _layoutGeneration == _data.Generation)
        {
            return;
        }

        _layoutMode = mode;
        _layoutWidth = width;
        _layoutHeight = height;
        _layoutGeneration = _data.Generation;

        _styles[mode].BuildLayout(width, height);
    }

    /// <summary>
    /// Dropped when the saver stops, not merely when it is destroyed. A blurred
    /// backdrop is a full bitmap of unmanaged memory and the garbage collector
    /// is in no hurry about those.
    /// </summary>
    public void Dispose()
    {
        _blur.Dispose();
        _palettes.Purge();
        _halftones.Purge();
        foreach (var style in _styles.Values)
        {
            if (style is IDisposable disposable) disposable.Dispose();
        }
    }
}

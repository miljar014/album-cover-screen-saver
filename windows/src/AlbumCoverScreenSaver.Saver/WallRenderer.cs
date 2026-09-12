using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// Slow-Building Wall. The same exact-fit grid as Mosaic, but empty at the start
/// of each cycle: tiles arrive one at a time in a shuffled order, the full wall
/// is held, and then it dissolves away in the order it arrived.
/// </summary>
/// <remarks>
/// <para>
/// Tiles never flip here. The whole style is one clock, and every tile's
/// appearance is a function of how far into that clock we are, which is why all
/// the timing lives in <see cref="WallCycle"/> where it can be tested. Nothing
/// is stored per tile except its place in the arrival order.
/// </para>
/// <para>
/// The geometry is stable across cycles. Only the covers and the order change,
/// so the wall looks like the same wall being refilled rather than a new grid
/// every half minute.
/// </para>
/// </remarks>
internal sealed class WallRenderer : IStyleRenderer
{
    private readonly SaverData _data;
    private readonly TileField _field;
    private readonly Random _random;
    private readonly bool _isPreview;

    private readonly List<int> _order = [];
    private double _cycleStart;
    private double _step;
    private double _fillTime;
    private double _holdTime;
    private double _cycleTime;

    public WallRenderer(SaverData data, TileField field, Random random, bool isPreview)
    {
        _data = data;
        _field = field;
        _random = random;
        _isPreview = isPreview;
    }

    public void BuildLayout(float width, float height)
    {
        _field.Build(
            width, height,
            TileField.CellTarget(_data.Settings.TileSize, _isPreview),
            varied: _data.Settings.MosaicRandomSize);

        StartCycle(phase: 0);

        Log.Write($"wall layout: {_field.Count} tiles, {_fillTime:0.0}s fill, {_cycleTime:0.0}s cycle");
    }

    /// <summary>
    /// Reshuffles the arrival order and restarts the clock.
    /// </summary>
    /// <remarks>
    /// The timings are recomputed here rather than per frame, so a settings
    /// change lands at the start of a cycle instead of stretching one that is
    /// already half built.
    /// </remarks>
    private void StartCycle(double phase)
    {
        _cycleStart = phase;

        _step = WallCycle.Step(_data.Settings.Tempo, _field.Count, _data.Settings.WallBuildSpeed);
        _fillTime = WallCycle.FillTime(_step, _field.Count);
        _holdTime = WallCycle.HoldTime(_data.Settings.WallHold);
        _cycleTime = WallCycle.CycleTime(_fillTime, _holdTime);

        _order.Clear();
        for (var i = 0; i < _field.Count; i++) _order.Add(i);

        // Fisher-Yates. Each tile's position in this list is what decides both
        // when it arrives and when it leaves.
        for (var i = _order.Count - 1; i > 0; i--)
        {
            var j = _random.Next(0, i + 1);
            (_order[i], _order[j]) = (_order[j], _order[i]);
        }

        for (var position = 0; position < _order.Count; position++)
        {
            _field.Tiles[_order[position]].AppearAt = position * _step;
        }
    }

    public void Advance(double phase, float width, float height)
    {
        if (_field.Count == 0) return;

        if (phase - _cycleStart > _cycleTime)
        {
            // The rectangles are kept and only the covers re-picked, so the
            // wall reads as the same wall being refilled.
            _field.AssignAlbums();
            StartCycle(phase);
        }
    }

    public void Draw(SKCanvas canvas, float width, float height, double phase)
    {
        var elapsed = phase - _cycleStart;

        foreach (var tile in _field.Tiles)
        {
            var alpha = WallCycle.AlphaAt(
                elapsed, tile.AppearAt, _step, _field.Count, _fillTime, _holdTime);

            if (alpha <= 0.01f) continue;

            // Lands at 94% of its cell and settles to full size on the same
            // clock as the fade. DrawTile then puts its own hairline inset on
            // top of this.
            var grow = WallCycle.GrowAt(elapsed, tile.AppearAt);
            var cell = tile.Cell;
            var insetX = cell.Width * (1f - grow) / 2f;
            var insetY = cell.Height * (1f - grow) / 2f;

            var rect = SKRect.Create(
                cell.X + insetX, cell.Y + insetY,
                cell.Width - (insetX * 2f), cell.Height - (insetY * 2f));

            Drawing.DrawTile(canvas, tile, _data.ImageFor(tile.Index), phase, alpha, rect);
        }

        Drawing.DrawArchiveLabel(canvas, _data, width, height, _isPreview);
    }
}

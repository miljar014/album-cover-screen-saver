using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// Mosaic Grid. A dense wall of covers filling the display edge to edge, with
/// individual tiles turning over in place at random.
/// </summary>
/// <remarks>
/// Nothing moves, scales or fades except the flipping tiles. The style does not
/// react to what is playing at all: its label always shows the most recently
/// played album in the archive, and the only indirect effect of playback is that
/// a newly played album reaches the front of the list on the next reload, which
/// both biases selection toward it and changes the label.
/// </remarks>
internal sealed class MosaicRenderer : IStyleRenderer
{
    private readonly SaverData _data;
    private readonly TileField _field;
    private readonly bool _isPreview;

    public MosaicRenderer(SaverData data, TileField field, bool isPreview)
    {
        _data = data;
        _field = field;
        _isPreview = isPreview;
    }

    public void BuildLayout(float width, float height)
    {
        _field.Build(
            width, height,
            TileField.CellTarget(_data.Settings.TileSize, _isPreview),
            varied: _data.Settings.MosaicRandomSize);

        Log.Write($"mosaic layout: {_field.Count} tiles across {width:0}x{height:0} points");
    }

    public void Advance(double phase, float width, float height) =>
        _field.AdvanceFlips(phase, _data.Settings.Tempo);

    public void Draw(SKCanvas canvas, float width, float height, double phase)
    {
        foreach (var tile in _field.Tiles)
        {
            Drawing.DrawTile(canvas, tile, _data.ImageFor(tile.Index), phase);
        }

        Drawing.DrawArchiveLabel(canvas, _data, width, height, _isPreview);
    }
}

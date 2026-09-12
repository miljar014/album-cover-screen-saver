using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// One style. Built when the layout changes, advanced once a frame, drawn once
/// a frame.
/// </summary>
/// <remarks>
/// Width and height are in points, the canvas having already been scaled for
/// the display's DPI. Nothing in a renderer should ever see a pixel.
/// </remarks>
internal interface IStyleRenderer
{
    void BuildLayout(float width, float height);

    void Advance(double phase, float width, float height);

    void Draw(SKCanvas canvas, float width, float height, double phase);
}

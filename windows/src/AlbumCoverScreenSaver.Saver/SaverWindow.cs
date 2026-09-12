using System.Runtime.InteropServices;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// One window on one display, with its own drawing surface.
/// </summary>
/// <remarks>
/// There is exactly one of these per logical display and never one spanning
/// several. A spanning window carries a single DPI value and a single refresh
/// rate for screens that may differ in both, and it draws into the dead space
/// of any arrangement that is not a perfect row. Doc 04 section 3 settles this;
/// it is not a preference.
/// </remarks>
internal sealed class SaverWindow : IDisposable
{
    private SKBitmap? _surface;
    private int _width;
    private int _height;

    public SaverWindow(
        nint handle, bool isPreview, int ordinal, int total, DisplayInfo? display,
        SaverData data, int seed)
    {
        Handle = handle;
        IsPreview = isPreview;
        Ordinal = ordinal;
        Total = total;
        Display = display;

        // Its own scene, and therefore its own tiles and its own random source.
        // Sharing one would give every screen an identical wall.
        Scene = new Scene(data, isPreview, seed);
    }

    public nint Handle { get; }
    public bool IsPreview { get; }
    public int Ordinal { get; }
    public int Total { get; }
    public DisplayInfo? Display { get; }
    public Scene Scene { get; }

    public void Render(double phase)
    {
        if (!EnsureSurface()) return;

        using (var canvas = new SKCanvas(_surface!))
        {
            // Everything above this line is in physical pixels; everything below
            // it is in points. See the note on DpiScale.
            var scale = DpiScale;
            canvas.Scale(scale);
            Scene.Render(canvas, _width / scale, _height / scale, phase, IsPreview ? null : Describe());
            canvas.Flush();
        }

        var hdc = Native.GetDC(Handle);
        if (hdc == 0) return;
        try
        {
            Present(hdc);
        }
        finally
        {
            Native.ReleaseDC(Handle, hdc);
        }
    }

    /// <summary>Blits the last rendered frame. Also used to answer WM_PAINT.</summary>
    public void Present(nint hdc)
    {
        if (_surface is null || _width <= 0 || _height <= 0) return;

        var header = new Native.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
            biWidth = _width,
            // Negative height means the rows run top-down, which is the order
            // Skia hands them over. Positive would render the frame upside down.
            biHeight = -_height,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = Native.BI_RGB,
        };

        Native.StretchDIBits(
            hdc,
            0, 0, _width, _height,
            0, 0, _width, _height,
            _surface.GetPixels(), ref header, Native.DIB_RGB_COLORS, Native.SRCCOPY);
    }

    private bool EnsureSurface()
    {
        if (!Native.GetClientRect(Handle, out var client)) return false;

        var width = client.Width;
        var height = client.Height;
        if (width <= 0 || height <= 0) return false;

        if (_surface is not null && width == _width && height == _height) return true;

        _surface?.Dispose();
        _width = width;
        _height = height;

        // Bgra8888 is what a 32 bit DIB expects, so the blit is a straight copy
        // with no channel swap.
        _surface = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        Log.Write($"surface for {Describe()} is {width}x{height}");
        return true;
    }

    /// <summary>
    /// Physical pixels per point on this window's display.
    /// </summary>
    /// <remarks>
    /// The surface is allocated in physical pixels, so on a display at 200% it
    /// is twice the size in each direction. Drawing straight into it with the
    /// sizes from the style documents would render everything at half the
    /// intended size, which is what a 16 point caption looking like 8 points
    /// actually is.
    ///
    /// So the canvas is scaled once, here, and every renderer works in points.
    /// That is what makes the numbers in the style documents (a 200 point tile,
    /// a 22 point label, a label origin 44 points up from the bottom) mean the
    /// same thing on every screen, which is the entire purpose of declaring
    /// per-monitor DPI awareness in the first place.
    ///
    /// It is read per frame rather than cached, because a window can be dragged
    /// between displays at different scaling and this must follow it.
    /// </remarks>
    public float DpiScale
    {
        get
        {
            var dpi = Native.GetDpiForWindow(Handle);
            return dpi == 0 ? 1f : dpi / 96f;
        }
    }

    /// <summary>
    /// The line drawn in the corner. On two screens this is how you confirm
    /// each really did get its own window at its own size and scaling.
    /// </summary>
    public string Describe()
    {
        if (IsPreview) return "preview";

        var percent = (int)Math.Round(DpiScale * 100f);
        var name = Display?.DeviceName ?? "?";
        var primary = Display?.IsPrimary == true ? ", primary" : "";

        return $"Display {Ordinal} of {Total}  {_width}x{_height}  {percent}%  {name}{primary}";
    }

    public void Dispose()
    {
        _surface?.Dispose();
        _surface = null;

        // The scene holds the blurred backdrops, which are full bitmaps of
        // unmanaged memory rather than anything the collector hurries over.
        Scene.Dispose();
    }
}

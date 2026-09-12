using System.Runtime.InteropServices;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>One logical display, as Windows sees it.</summary>
internal sealed record DisplayInfo(
    nint Monitor,
    Native.RECT Bounds,
    bool IsPrimary,
    string DeviceName)
{
    public int Width => Bounds.Width;
    public int Height => Bounds.Height;

    public override string ToString() =>
        $"{DeviceName} {Width}x{Height} at ({Bounds.Left},{Bounds.Top}){(IsPrimary ? " primary" : "")}";
}

internal static class Displays
{
    /// <summary>
    /// Every logical display, in enumeration order.
    /// </summary>
    /// <remarks>
    /// Enumerating rather than counting, because the rectangles are needed
    /// regardless.
    ///
    /// Duplicated displays need no handling at all: when Windows is set to
    /// "Duplicate these displays" it presents the pair as a single logical
    /// display, so this returns one entry and that is the correct answer. An
    /// application fundamentally cannot put different content on the two halves
    /// of a duplicated pair; they share one framebuffer.
    /// </remarks>
    public static IReadOnlyList<DisplayInfo> Enumerate()
    {
        var found = new List<DisplayInfo>();

        // The callback must stay alive for the duration of the call. Letting
        // the delegate be collected mid-enumeration is a crash with no message.
        Native.MonitorEnumProc callback = (nint monitor, nint _, ref Native.RECT _, nint _) =>
        {
            var info = new Native.MONITORINFOEXW
            {
                cbSize = (uint)Marshal.SizeOf<Native.MONITORINFOEXW>(),
            };

            if (Native.GetMonitorInfoW(monitor, ref info))
            {
                found.Add(new DisplayInfo(
                    monitor,
                    info.rcMonitor,
                    (info.dwFlags & Native.MONITORINFOF_PRIMARY) != 0,
                    info.szDevice ?? ""));
            }

            return true;
        };

        Native.EnumDisplayMonitors(0, 0, callback, 0);
        GC.KeepAlive(callback);

        return found;
    }
}

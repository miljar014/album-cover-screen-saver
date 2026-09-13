namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// What one screen is drawing, when the composition is spread across all of them.
/// </summary>
/// <param name="Width">The whole arrangement's width, in this screen's points.</param>
/// <param name="Height">The whole arrangement's height, in this screen's points.</param>
/// <param name="OffsetX">Where this screen's left edge sits on it, in this screen's points.</param>
/// <param name="OffsetY">Where this screen's top edge sits on it.</param>
public readonly record struct SpanCanvas(float Width, float Height, float OffsetX, float OffsetY)
{
    /// <summary>The canvas exactly filling one screen, which is the ordinary case.</summary>
    public static SpanCanvas Single(float width, float height) => new(width, height, 0f, 0f);

    public bool IsSingle => OffsetX == 0f && OffsetY == 0f;
}

/// <summary>
/// Spreading one composition across every screen.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is not a window spanning the screens</b>, and the difference is the
/// whole of why it works. A single window straddling a 4K display at 150% and a
/// 1080p display at 100% carries one scaling value, so one of the two is drawn
/// wrong; it presents at one refresh rate, so a 60 Hz and a 144 Hz screen
/// visibly disagree; and it covers the bounding rectangle of the arrangement,
/// which for anything but a perfect row includes regions that are not on any
/// screen at all.
/// </para>
/// <para>
/// There is still one borderless window per screen, each at its own scaling and
/// its own refresh rate. Each is simply told that the surface is the size of the
/// whole arrangement and that it is looking at a particular part of it. Nothing
/// is drawn into the gaps because no screen has a gap in it, and the covers line
/// up across the desk because every screen converts the same arrangement into
/// its own points.
/// </para>
/// <para>
/// A renderer needs no changes for this. It is handed a bigger surface and a
/// translation, and goes on drawing exactly as it did.
/// </para>
/// </remarks>
public static class SpanLayout
{
    /// <summary>
    /// Whether spreading a style across screens reads as one picture or as a
    /// mistake.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fields of covers gain from the room: a mosaic across two screens is
    /// a bigger mosaic, and covers drifting off one screen and onto the next is
    /// the effect working.
    /// </para>
    /// <para>
    /// Everything else has one subject in the middle of it. Spread across two
    /// screens, a turntable's centre lands wherever the middle of the
    /// arrangement happens to be, which is usually the gap between the monitors,
    /// and the other screen shows an empty plinth. That is not a picture of a
    /// record player; it is a record player with a monitor bezel through it.
    /// </para>
    /// </remarks>
    public static bool CanSpan(CollageMode mode) => mode switch
    {
        CollageMode.Mosaic => true,
        CollageMode.Wall => true,
        CollageMode.Drift => true,
        CollageMode.Gallery => true,
        CollageMode.Ambient => true,
        CollageMode.Starfield => true,
        _ => false,
    };

    /// <summary>Every style that can be spread across screens, in listing order.</summary>
    public static IReadOnlyList<CollageMode> Spannable() =>
        CollageModes.All.Where(CanSpan).ToArray();

    /// <summary>
    /// What one screen should draw.
    /// </summary>
    /// <param name="screen">This screen's rectangle, in virtual-desktop pixels.</param>
    /// <param name="all">Every screen's rectangle, in the same pixels.</param>
    /// <param name="scale">This screen's device pixels per point.</param>
    /// <remarks>
    /// <para>
    /// The arrangement is measured in the pixels Windows reports, which are the
    /// same for every screen, and then converted into <em>this</em> screen's
    /// points. Two screens at different scaling therefore see canvases of
    /// different point sizes, and that is correct rather than a bug: a cover at
    /// the same place on the arrangement lands at the same physical spot on the
    /// desk, which is the only thing that matters.
    /// </para>
    /// <para>
    /// Falls back to the screen on its own if the arrangement cannot be worked
    /// out, because a screen drawing an ordinary composition is a far better
    /// failure than a screen drawing nothing.
    /// </para>
    /// </remarks>
    public static SpanCanvas For(
        (int X, int Y, int Width, int Height) screen,
        IReadOnlyList<(int X, int Y, int Width, int Height)> all,
        float scale)
    {
        if (scale <= 0f) scale = 1f;

        var mine = Points(screen.Width, scale);
        var minesHeight = Points(screen.Height, scale);

        if (all is null || all.Count <= 1) return SpanCanvas.Single(mine, minesHeight);

        var left = int.MaxValue;
        var top = int.MaxValue;
        var right = int.MinValue;
        var bottom = int.MinValue;

        foreach (var rect in all)
        {
            if (rect.Width <= 0 || rect.Height <= 0) continue;

            left = Math.Min(left, rect.X);
            top = Math.Min(top, rect.Y);
            right = Math.Max(right, rect.X + rect.Width);
            bottom = Math.Max(bottom, rect.Y + rect.Height);
        }

        if (left == int.MaxValue || right <= left || bottom <= top)
        {
            return SpanCanvas.Single(mine, minesHeight);
        }

        return new SpanCanvas(
            Width: Points(right - left, scale),
            Height: Points(bottom - top, scale),
            OffsetX: Points(screen.X - left, scale),
            OffsetY: Points(screen.Y - top, scale));
    }

    private static float Points(int pixels, float scale) => pixels / scale;

    /// <summary>
    /// Whether this run should spread the composition at all.
    /// </summary>
    /// <remarks>
    /// Three conditions, and every one of them has to hold. Spreading one screen
    /// across itself is just a screen. Spreading a style that cannot take it
    /// puts a bezel through the middle of a turntable. And a screen set to its
    /// own style is not part of the shared picture by definition.
    /// </remarks>
    public static bool ShouldSpan(
        MultiMonitorMode mode, CollageMode style, int screenCount, bool followsMainStyle) =>
        mode == MultiMonitorMode.Span
        && screenCount > 1
        && followsMainStyle
        && CanSpan(style);
}

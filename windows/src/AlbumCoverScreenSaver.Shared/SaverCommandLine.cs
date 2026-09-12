using System.Globalization;

namespace AlbumCoverScreenSaver.Shared;

/// <summary>What Windows is asking the screen saver to do.</summary>
public enum SaverMode
{
    /// <summary>Run full screen. <c>/s</c></summary>
    FullScreen,

    /// <summary>Draw the little thumbnail inside a window Windows owns. <c>/p HWND</c></summary>
    Preview,

    /// <summary>Show settings. <c>/c</c>, <c>/c:HWND</c>, or no arguments at all.</summary>
    Configure,

    /// <summary>Change password. Windows 95 legacy. Do nothing and exit. <c>/a HWND</c></summary>
    Ignore,
}

/// <summary>
/// Parses the arguments Windows hands a <c>.scr</c>.
/// </summary>
/// <remarks>
/// The brief calls getting this wrong "the classic first bug", and it is easy
/// to get wrong because the forms are not consistent. In practice Windows
/// passes <c>/c:12345</c> from the Settings button but <c>/p 12345</c> for the
/// preview, some shells pass <c>-s</c>, and the case varies. So: match
/// case-insensitively, accept both a leading slash and a leading dash, and
/// accept the handle either glued on after a colon or standing alone as the
/// next argument.
///
/// This lives in the shared library rather than in the saver on purpose. It is
/// plain string parsing with nothing Windows-specific about it, so keeping it
/// here means it can be tested on any machine, which is the only reason it
/// arrived already proven rather than being debugged through a screen saver
/// that does not appear.
///
/// The window handle is documented as an unsigned decimal number. Hex with an
/// <c>0x</c> prefix is accepted too, because it costs nothing and a shell
/// somewhere will do it.
/// </remarks>
public sealed class SaverCommandLine
{
    private SaverCommandLine(SaverMode mode, nint parentWindow)
    {
        Mode = mode;
        ParentWindow = parentWindow;
    }

    public SaverMode Mode { get; }

    /// <summary>The window to draw into or sit modal to. Zero when there is none.</summary>
    public nint ParentWindow { get; }

    public bool HasParentWindow => ParentWindow != 0;

    public static SaverCommandLine Parse(string[]? args)
    {
        if (args is null || args.Length == 0) return new SaverCommandLine(SaverMode.Configure, 0);

        var first = (args[0] ?? "").Trim().Trim('"');
        if (first.Length == 0) return new SaverCommandLine(SaverMode.Configure, 0);

        // Both prefixes are seen in the wild, and so is neither.
        var body = first.TrimStart('/', '-');
        if (body.Length == 0) return new SaverCommandLine(SaverMode.Configure, 0);

        var letter = char.ToLowerInvariant(body[0]);
        var tail = body[1..];

        // "/c:12345" and "/c=12345" both glue the handle on. "/p 12345" does not.
        if (tail.StartsWith(':') || tail.StartsWith('='))
        {
            tail = tail[1..];
        }
        else if (tail.Length > 0)
        {
            // Something like "/scr" or "/showconfig". Only the first letter is
            // meaningful, and a trailing word is not a handle.
            tail = "";
        }

        var handleText = tail.Length > 0
            ? tail
            : args.Length > 1 ? (args[1] ?? "").Trim().Trim('"') : "";

        var handle = ParseHandle(handleText);

        return letter switch
        {
            's' => new SaverCommandLine(SaverMode.FullScreen, 0),

            // A preview with no window to draw into is not a preview. Falling
            // back to Configure is harmless; trying to draw into window zero is
            // not.
            'p' => handle != 0
                ? new SaverCommandLine(SaverMode.Preview, handle)
                : new SaverCommandLine(SaverMode.Configure, 0),

            'c' => new SaverCommandLine(SaverMode.Configure, handle),
            'a' => new SaverCommandLine(SaverMode.Ignore, handle),
            _ => new SaverCommandLine(SaverMode.Configure, 0),
        };
    }

    private static nint ParseHandle(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        text = text.Trim();

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)
                ? unchecked((nint)hex)
                : 0;
        }

        // Documented as unsigned decimal, and on 64 bit it can exceed a signed
        // int, so parse wide and reinterpret rather than overflowing to zero.
        return ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? unchecked((nint)value)
            : 0;
    }

    public override string ToString() =>
        HasParentWindow ? $"{Mode} (parent 0x{ParentWindow:X})" : Mode.ToString();
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using AlbumCoverScreenSaver.Shared;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// Owns the windows, the message pump and the frame loop.
/// </summary>
internal static class SaverHost
{
    private const string ClassName = "AlbumCoverScreenSaverWindow";

    /// <summary>Nominal frame interval. See the note on the phase clock in Loop.</summary>
    private const double FrameInterval = 1.0 / 30.0;

    /// <summary>
    /// The window procedure must outlive every window that uses it. Letting the
    /// delegate be collected is a crash with no message and no stack.
    /// </summary>
    private static Native.WndProc? _windowProcedure;

    private static readonly Dictionary<nint, SaverWindow> Windows = new();

    /// <summary>Shared by every window. The composition on each is not.</summary>
    private static SaverData? _data;

    private static bool _running = true;
    private static bool _exitOnInput;
    private static bool _rebuildWindows;
    private static bool _cursorHidden;
    private static nint _previewParent;

    private static Native.POINT _firstCursor;
    private static bool _haveFirstCursor;

    public static int RunFullScreen()
    {
        _exitOnInput = true;

        var module = Native.GetModuleHandleW(null);
        if (!RegisterClass(module)) return 1;

        using var data = new SaverData(SharedStore.Default);
        _data = data;
        Log.Write($"{data.Albums.Count} album(s) have art on disk");

        if (!CreateFullScreenWindows(module)) return 1;

        HideCursor();
        try
        {
            return Loop();
        }
        finally
        {
            ShowCursorAgain();
            DestroyAll();
            _data = null;
        }
    }

    public static int RunPreview(nint parent)
    {
        _exitOnInput = false;
        _previewParent = parent;

        if (!Native.IsWindow(parent))
        {
            Log.Write($"preview parent 0x{parent:X} is not a window, nothing to do");
            return 0;
        }

        var module = Native.GetModuleHandleW(null);
        if (!RegisterClass(module)) return 1;

        using var data = new SaverData(SharedStore.Default);
        _data = data;

        Native.GetClientRect(parent, out var client);

        var handle = Native.CreateWindowExW(
            0, ClassName, null,
            Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_CLIPCHILDREN,
            0, 0, client.Width, client.Height,
            parent, 0, module, 0);

        if (handle == 0)
        {
            Log.Write($"could not create the preview window: error {Marshal.GetLastWin32Error()}");
            return 1;
        }

        Windows[handle] = new SaverWindow(
            handle, isPreview: true, 1, 1, null, data, Environment.TickCount);
        Log.Write($"preview window {client.Width}x{client.Height} inside 0x{parent:X}");

        try
        {
            return Loop();
        }
        finally
        {
            DestroyAll();
            _data = null;
        }
    }

    private static bool RegisterClass(nint module)
    {
        _windowProcedure = WindowProcedure;

        var windowClass = new Native.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Native.WNDCLASSEXW>(),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProcedure),
            hInstance = module,
            // No background brush and no cursor: every pixel is painted by us,
            // and letting Windows erase the background first is visible flicker.
            hbrBackground = 0,
            hCursor = 0,
            lpszClassName = ClassName,
        };

        if (Native.RegisterClassExW(ref windowClass) != 0) return true;

        Log.Write($"could not register the window class: error {Marshal.GetLastWin32Error()}");
        return false;
    }

    private static bool CreateFullScreenWindows(nint module)
    {
        var displays = Displays.Enumerate();
        Log.Write($"{displays.Count} logical display(s)");
        foreach (var display in displays) Log.Write($"  {display}");

        if (displays.Count == 0)
        {
            Log.Write("no displays reported, nothing to draw on");
            return false;
        }

        for (var i = 0; i < displays.Count; i++)
        {
            var display = displays[i];

            var handle = Native.CreateWindowExW(
                Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW,
                ClassName, "Album Cover Screen Saver",
                Native.WS_POPUP | Native.WS_VISIBLE,
                display.Bounds.Left, display.Bounds.Top, display.Width, display.Height,
                0, 0, module, 0);

            if (handle == 0)
            {
                Log.Write($"could not create a window for {display}: error {Marshal.GetLastWin32Error()}");
                continue;
            }

            // A different seed per screen, so two displays running the same
            // style do not build the same wall.
            var seed = Environment.TickCount + (i * 7919);

            Windows[handle] = new SaverWindow(
                handle, isPreview: false, i + 1, displays.Count, display, _data!, seed);

            Native.ShowWindow(handle, Native.SW_SHOW);
            Native.UpdateWindow(handle);
        }

        if (Windows.Count == 0) return false;

        // The first window takes focus so key presses arrive as messages rather
        // than going to whatever was in front before.
        Native.SetForegroundWindow(Windows.Keys.First());
        return true;
    }

    private static int Loop()
    {
        var clock = Stopwatch.StartNew();

        // phase is a nominal frame counter in seconds, not wall clock. It
        // advances by exactly one frame per rendered frame, which is what every
        // timing constant in the style documents is expressed in. Wall clock is
        // used only by NowPlaying.IsLive and ProgressFraction, and that split is
        // deliberate.
        var phase = 0.0;
        var nextFrame = 0.0;

        // Frame timing, because "it looks jumpy" and "it is slow" are different
        // faults with different fixes and cannot be told apart by watching. A
        // steady 20 fps looks smooth; an average of 30 with one frame in twenty
        // taking four times as long does not.
        var frames = 0;
        var spent = 0.0;
        var worst = 0.0;
        var windowStart = 0.0;
        var reports = 0;
        var lastComplaint = -100.0;

        while (_running)
        {
            while (Native.PeekMessageW(out var message, 0, 0, 0, Native.PM_REMOVE))
            {
                if (message.message == Native.WM_QUIT)
                {
                    _running = false;
                    break;
                }
                Native.TranslateMessage(ref message);
                Native.DispatchMessageW(ref message);
            }

            if (!_running) break;

            // The Settings dialog will not tell us politely when it closes.
            if (_previewParent != 0 && !Native.IsWindow(_previewParent))
            {
                Log.Write("the preview's parent window went away");
                break;
            }

            if (_exitOnInput && CursorHasMoved())
            {
                Log.Write("cursor moved, quitting");
                break;
            }

            if (_rebuildWindows)
            {
                _rebuildWindows = false;
                Log.Write("displays changed, rebuilding the window set");
                DestroyAll();
                if (!CreateFullScreenWindows(Native.GetModuleHandleW(null))) break;
            }

            var now = clock.Elapsed.TotalSeconds;
            if (now < nextFrame)
            {
                Thread.Sleep(1);
                continue;
            }

            nextFrame = now + FrameInterval;
            phase += FrameInterval;

            _data?.Poll(phase);

            var began = clock.Elapsed.TotalSeconds;
            foreach (var window in Windows.Values.ToArray()) window.Render(phase);
            var took = clock.Elapsed.TotalSeconds - began;

            frames++;
            spent += took;
            worst = Math.Max(worst, took);

            if (now - windowStart >= 5.0)
            {
                var average = spent / Math.Max(1, frames) * 1000.0;
                var achieved = frames / (now - windowStart);

                // The first few reports show how it starts; after that only a
                // genuine problem is worth a line, and at most once a minute, or
                // a long night's run writes a log nobody will read.
                var struggling = achieved < 24 || worst * 1000.0 > 70;

                if (reports < 3 || (struggling && now - lastComplaint > 60))
                {
                    Log.Write(
                        $"frames: {achieved:0.0} per second, {average:0.0} ms each, worst {worst * 1000.0:0.0} ms");

                    reports++;
                    if (struggling) lastComplaint = now;
                }

                frames = 0;
                spent = 0;
                worst = 0;
                windowStart = now;
            }
        }

        return 0;
    }

    /// <summary>
    /// Windows sends a spurious mouse move immediately after launch, so the
    /// first reading is treated as the resting position and only a real
    /// movement away from it counts. Without this the saver dies the instant it
    /// starts, which looks exactly like a crash.
    /// </summary>
    private static bool CursorHasMoved()
    {
        if (!Native.GetCursorPos(out var position)) return false;

        if (!_haveFirstCursor)
        {
            _firstCursor = position;
            _haveFirstCursor = true;
            return false;
        }

        var dx = position.X - _firstCursor.X;
        var dy = position.Y - _firstCursor.Y;
        return (dx * dx) + (dy * dy) > 25;
    }

    private static nint WindowProcedure(nint handle, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case Native.WM_ERASEBKGND:
                // Claimed, so Windows does not paint over the frame first.
                return 1;

            case Native.WM_PAINT:
            {
                var hdc = Native.BeginPaint(handle, out var paint);
                if (Windows.TryGetValue(handle, out var window)) window.Present(hdc);
                Native.EndPaint(handle, ref paint);
                return 0;
            }

            case Native.WM_SETCURSOR:
                // Keep the pointer hidden over a full screen window.
                if (_exitOnInput) return 1;
                break;

            case Native.WM_KEYDOWN:
            case Native.WM_SYSKEYDOWN:
            case Native.WM_LBUTTONDOWN:
            case Native.WM_RBUTTONDOWN:
            case Native.WM_MBUTTONDOWN:
            case Native.WM_MOUSEWHEEL:
                if (_exitOnInput)
                {
                    Log.Write($"input received (message 0x{message:X}), quitting");
                    _running = false;
                    return 0;
                }
                break;

            case Native.WM_DISPLAYCHANGE:
                // A laptop being docked or undocked while the saver runs is
                // normal, not an edge case.
                if (_exitOnInput) _rebuildWindows = true;
                return 0;

            case Native.WM_CLOSE:
                _running = false;
                return 0;

            case Native.WM_DESTROY:
                Windows.Remove(handle);
                return 0;
        }

        return Native.DefWindowProcW(handle, message, wParam, lParam);
    }

    private static void HideCursor()
    {
        if (_cursorHidden) return;
        Native.ShowCursor(false);
        _cursorHidden = true;
    }

    private static void ShowCursorAgain()
    {
        if (!_cursorHidden) return;
        Native.ShowCursor(true);
        _cursorHidden = false;
    }

    private static void DestroyAll()
    {
        foreach (var window in Windows.Values.ToArray())
        {
            window.Dispose();
            Native.DestroyWindow(window.Handle);
        }
        Windows.Clear();
    }
}

using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using AlbumCoverScreenSaver.Shared;

namespace AlbumCoverScreenSaver.Tray;

/// <summary>
/// The tray icon and its menu, and the loop behind them.
/// </summary>
/// <remarks>
/// Settings live here rather than in the screen saver, and will be built in
/// step 5. The Windows Screen Saver dialog's Settings button calls back into
/// the .scr, which is a poor place to host a nineteen-style settings window.
/// That is the same conclusion the macOS build reached about Apple's Options
/// button, for the same reasons.
/// </remarks>
internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _status;
    private readonly ToolStripMenuItem _nowPlaying;
    private readonly ToolStripMenuItem _startWithWindows;
    /// <summary>
    /// Set by anything that wants this app to close itself.
    /// </summary>
    /// <remarks>
    /// The build script uses it. A tray app holds its own files open, so a
    /// rebuild cannot replace them while it is running, and killing the process
    /// instead leaves the icon sitting in the tray as a ghost until the mouse
    /// passes over it. Asking it to quit means the icon is removed properly.
    ///
    /// The same mechanism is what doc 00 section 5a suggests for the screen
    /// saver's /c argument, when that is wired up.
    /// </remarks>
    public const string QuitSignalName = "AlbumCoverScreenSaverTrayQuit";

    private readonly CancellationTokenSource _stopping = new();
    private readonly Poller _poller;
    private readonly EventHandler _iconDoubleClicked;
    private readonly EventWaitHandle? _quitSignal;
    private readonly System.Windows.Forms.Timer _quitWatch;

    private SettingsForm? _settingsWindow;

    private static Icon? _appIcon;

    /// <summary>The record mark, shared by the tray and the settings window.</summary>
    public static Icon AppIcon => _appIcon ??= LoadIcon();

    public TrayApp()
    {
        _poller = new Poller(SharedStore.Default);

        _status = new ToolStripMenuItem("Starting up") { Enabled = false };
        _nowPlaying = new ToolStripMenuItem("Nothing playing") { Enabled = false };

        _startWithWindows = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = Startup.IsEnabled(),
        };
        _startWithWindows.CheckedChanged += (_, _) => Startup.SetEnabled(_startWithWindows.Checked);

        var settings = new ToolStripMenuItem("Settings...");
        settings.Click += (_, _) => ShowSettings();

        var openFolder = new ToolStripMenuItem("Open Data Folder");
        openFolder.Click += (_, _) => OpenDataFolder();

        var quit = new ToolStripMenuItem("Quit");
        quit.Click += (_, _) => Quit();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(
        [
            _status,
            _nowPlaying,
            new ToolStripSeparator(),
            settings,
            new ToolStripSeparator(),
            _startWithWindows,
            openFolder,
            new ToolStripSeparator(),
            quit,
        ]);
        menu.Opening += (_, _) => Refresh();

        // Double-clicking the tray icon is the conventional way to open an app
        // that has no window of its own.
        _iconDoubleClicked = (_, _) => ShowSettings();

        _icon = new NotifyIcon
        {
            Icon = AppIcon,
            Text = "Album Cover Screen Saver",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += _iconDoubleClicked;

        try
        {
            _quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, QuitSignalName);
        }
        catch (Exception error)
        {
            // Without it the build script falls back to stopping the process.
            Log.Failure("creating the quit signal", error);
        }

        // Polled on the UI thread rather than waited on a background one, so
        // quitting happens where the icon and menus live and needs no
        // marshalling back.
        _quitWatch = new System.Windows.Forms.Timer { Interval = 400 };
        _quitWatch.Tick += (_, _) =>
        {
            if (_quitSignal is null || !_quitSignal.WaitOne(0)) return;
            Log.Write("asked to quit by another program");
            Quit();
        };
        _quitWatch.Start();

        _ = RunAsync(_stopping.Token);
    }

    /// <summary>
    /// Opens the settings window, or brings the open one to the front.
    /// </summary>
    /// <remarks>
    /// The window is deliberately <b>not</b> cached across closes. Doing that is
    /// what made the macOS Options button work once and then never again: the
    /// code held one instance and handed the same closed window back forever,
    /// and AppKit refused silently. Any settings screen that "works the first
    /// time" is this bug. Checking IsDisposed is the whole fix.
    /// </remarks>
    private void ShowSettings()
    {
        if (_settingsWindow is { IsDisposed: false })
        {
            _settingsWindow.WindowState = FormWindowState.Normal;
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsForm(SharedStore.Default);
        _settingsWindow.FormClosed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private async Task RunAsync(CancellationToken token)
    {
        Log.Write("watching for music");

        while (!token.IsCancellationRequested)
        {
            var playing = false;
            try
            {
                playing = await _poller.PollOnceAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception error)
            {
                Log.Failure("the polling loop", error);
            }

            Refresh();

            try
            {
                await Task.Delay(PollingPlan.NowPlayingInterval(playing), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Log.Write("stopped watching");
    }

    private void Refresh()
    {
        var albums = _poller.AlbumCount;
        var counted = albums == 1 ? "1 album archived" : $"{albums} albums archived";

        _status.Text = counted;
        _nowPlaying.Text = _poller.Status;

        // The tooltip is capped at 127 characters by Windows and is silently
        // truncated past it.
        var tooltip = $"Album Cover Screen Saver\n{_poller.Status}";
        _icon.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;
    }

    private static void OpenDataFolder()
    {
        try
        {
            var folder = SharedStore.DefaultRoot();
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception error)
        {
            Log.Failure("opening the data folder", error);
        }
    }

    private static Icon LoadIcon()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var name = assembly.GetManifestResourceNames()
                .FirstOrDefault(resource => resource.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));

            if (name is not null)
            {
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream is not null)
                {
                    // Asking for the small icon size makes Windows pick the
                    // right image out of the .ico rather than shrinking the
                    // 256 pixel one, which turns to mush in a tray.
                    return new Icon(stream, SystemInformation.SmallIconSize);
                }
            }
        }
        catch (Exception error)
        {
            Log.Failure("loading the tray icon", error);
        }

        return SystemIcons.Application;
    }

    private void Quit()
    {
        Log.Write("quitting");
        _quitWatch.Stop();
        _stopping.Cancel();

        // Hidden before exiting, or the icon stays in the tray as a dead entry
        // until something makes Windows repaint it.
        _icon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stopping.Cancel();
            _quitWatch.Stop();
            _quitWatch.Dispose();
            _quitSignal?.Dispose();
            _icon.DoubleClick -= _iconDoubleClicked;
            _icon.Dispose();
            if (_settingsWindow is { IsDisposed: false }) _settingsWindow.Dispose();
            _poller.Dispose();
            _stopping.Dispose();
        }
        base.Dispose(disposing);
    }
}

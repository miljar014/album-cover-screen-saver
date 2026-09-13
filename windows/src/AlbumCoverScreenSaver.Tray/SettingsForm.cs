using System.Drawing;
using AlbumCoverScreenSaver.Shared;

namespace AlbumCoverScreenSaver.Tray;

/// <summary>
/// The settings window, built from <see cref="SettingsSchema"/> rather than
/// laid out by hand nineteen times, and laid out to match the Mac version.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read this before changing the layout.</b> Three attempts at this window
/// failed, all in the same way: the middle column collapsed to a hairline, the
/// readouts landed on top of the labels, and the window sized itself to the
/// wreckage. TableLayoutPanel was the cause every time. A "take the remaining
/// space" column has no space to take inside a panel that sizes itself to its
/// contents, and fixed column widths measured before the window exists are
/// wrong the moment per-monitor scaling multiplies them again.
/// </para>
/// <para>
/// So there is no TableLayoutPanel here at all. Rows are <see cref="Row"/>,
/// which places its three parts in <c>OnLayout</c> from the width the row
/// actually has at that moment. The label and readout widths are capped to a
/// share of that width, so even if the text measurement is wrong by a factor of
/// two the slider still gets roughly a third of the row. It cannot collapse.
/// </para>
/// <para>
/// Everything stacks with <c>Dock = Top</c>, which takes its width from the
/// parent and its height from itself. Docked controls stack in reverse order of
/// being added, so <see cref="StackInto"/> adds them backwards and the calling
/// code reads top to bottom.
/// </para>
/// <para>
/// Changes save immediately. There is no OK button, because there is nothing to
/// confirm: the running screen saver re-reads settings every three seconds.
/// Writes are held a fifth of a second first, because a dragged slider fires
/// continuously.
/// </para>
/// </remarks>
internal sealed class SettingsForm : Form
{
    private static readonly ToolTip Tips = new() { AutoPopDelay = 20000, InitialDelay = 400 };
    private static readonly int[] TimeoutChoices = [1, 2, 3, 5, 10, 15, 20, 30, 45, 60];

    /// <summary>The gap between the three parts of a row.</summary>
    private const int Gap = 10;

    private readonly SharedStore _store;
    private readonly System.Windows.Forms.Timer _saveSoon;

    private readonly Panel _page = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        Padding = new Padding(20, 2, 20, 20),
    };

    private readonly Panel _shared = new() { Dock = DockStyle.Top, Margin = Padding.Empty };
    private readonly Panel _styleBody = new() { Dock = DockStyle.Top, Margin = Padding.Empty };
    private readonly Panel _footer = new() { Dock = DockStyle.Bottom, BackColor = SystemColors.Control };
    private readonly Label _styleHeader;

    private readonly ComboBox _stylePicker = new();
    private readonly Note _gridNote;
    private readonly Note _saverStatus;
    private readonly ComboBox _timeout = new();

    private readonly ComboBox _sourcePicker = new();
    private readonly TextBox _lastFmUser = new();
    private readonly Button _testAccount = new();
    private readonly Note _sourceBlurb;
    private readonly Note _accountStatus;

    private readonly List<Action> _refreshers = [];
    private readonly Dictionary<string, Control> _byKey = new(StringComparer.Ordinal);

    private int _sharedRefreshers;

    /// <summary>
    /// The label and readout widths, in real pixels at the window's current
    /// scaling.
    /// </summary>
    /// <remarks>
    /// Shared with every <see cref="Row"/> and re-measured whenever the window
    /// is shown or moved to a display at a different scaling. A plain int field
    /// would not do: WinForms scales Size, Padding and Margin for you, but it
    /// knows nothing about a number you measured yourself, so one taken at 100%
    /// and used at 200% is half the width the text now needs.
    /// </remarks>
    private sealed class Metrics
    {
        public int Label;
        public int Readout;
    }

    private readonly Metrics _metrics = new();

    private Settings _settings;
    private bool _loading;

    public SettingsForm(SharedStore store)
    {
        _store = store;
        _settings = store.LoadSettings();

        _saveSoon = new System.Windows.Forms.Timer { Interval = 200 };
        _saveSoon.Tick += (_, _) =>
        {
            _saveSoon.Stop();
            _store.SaveSettings(_settings);
        };

        Text = "Album Cover Screen Saver Settings";

        // The system font, untouched. Setting a font here is one more thing that
        // has to agree with the scaling, and it buys nothing.
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = SystemColors.Window;

        try { Icon = TrayApp.AppIcon; } catch (Exception) { /* a window without an icon still works */ }

        Measure();

        _styleHeader = Header("Options");
        _gridNote = new Note("") { IndentTo = _metrics, Margin = new Padding(0, 0, 0, 10) };
        _saverStatus = new Note("") { Margin = new Padding(0, 0, 0, 12) };
        _sourceBlurb = new Note("") { IndentTo = _metrics, Margin = new Padding(0, 0, 0, 10) };
        _accountStatus = new Note("") { IndentTo = _metrics, Margin = new Padding(0, 0, 0, 12) };

        Build();
    }

    /// <summary>
    /// Works out the label and readout widths from the longest text they will
    /// ever hold. These are a starting point, not a promise: <see cref="Row"/>
    /// caps both against the width it actually has.
    /// </summary>
    private void Measure()
    {
        var everyControl = SettingsSchema.Shared
            .Concat(CollageModes.All.SelectMany(SettingsSchema.For))
            .ToArray();

        int Width(string text) => TextRenderer.MeasureText(text, Font).Width;

        var labels = everyControl
            .Where(control => control is SliderControl or StyleChoiceControl)
            .Select(control => control.Label)
            .Append("Start after")
            .Append("Where music comes from")
            .Append("Last.fm username");

        _metrics.Label = labels.Select(Width).DefaultIfEmpty(120).Max() + 6;

        // The longest readout any control ever produces.
        _metrics.Readout = Width("33⅓ RPM (true LP)") + 8;
    }

    // --- stacking -----------------------------------------------------------

    /// <summary>
    /// Puts controls into a parent top to bottom.
    /// </summary>
    /// <remarks>
    /// Docked controls stack in reverse order of being added, so the first one
    /// added ends up at the bottom. Adding backwards here is what lets every
    /// caller list its rows in reading order.
    /// </remarks>
    private static void StackInto(Control parent, params Control[] children)
    {
        for (var index = children.Length - 1; index >= 0; index--)
        {
            children[index].Dock = DockStyle.Top;
            parent.Controls.Add(children[index]);
        }
    }

    /// <summary>Sets a container's height to exactly what it holds.</summary>
    /// <remarks>
    /// A note does not know how tall it is until it knows how wide it is, and it
    /// only learns that once the window has a size, which is after all of this
    /// is built. So heights are refitted rather than computed once, and
    /// <see cref="KeepFitted"/> subscribes so a note that rewraps later drags
    /// its container along with it.
    /// </remarks>
    private static void FitHeight(Panel panel)
    {
        var total = 0;
        foreach (Control child in panel.Controls) total += child.Height + child.Margin.Vertical;

        var wanted = total + panel.Padding.Vertical;
        if (panel.Height != wanted) panel.Height = wanted;
    }

    private static void KeepFitted(Panel panel)
    {
        foreach (Control child in panel.Controls) child.SizeChanged += (_, _) => FitHeight(panel);
        FitHeight(panel);
    }

    // --- building -----------------------------------------------------------

    private void Build()
    {
        SuspendLayout();

        Controls.Add(BuildFooter());
        Controls.Add(_page);

        // The filling control has to be at the front of the z-order, or the
        // footer is laid out inside it instead of below it. This one line is
        // the whole of that rule.
        _page.BringToFront();

        _stylePicker.DropDownStyle = ComboBoxStyle.DropDownList;
        _stylePicker.Margin = new Padding(0, 0, 0, 6);
        _stylePicker.Height = _stylePicker.PreferredHeight;
        foreach (var mode in CollageModes.All) _stylePicker.Items.Add(mode.Title());
        _stylePicker.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _stylePicker.SelectedIndex < 0) return;
            _settings.Mode = CollageModes.All[_stylePicker.SelectedIndex];
            Save();
            RebuildStyleOptions();
        };

        var sharedRows = new List<Control>();

        foreach (var control in SettingsSchema.Shared)
        {
            sharedRows.Add(MakeControl(control));
            if (control.Key == "tileSize") sharedRows.Add(_gridNote);
        }

        StackInto(_shared, sharedRows.ToArray());
        _sharedRefreshers = _refreshers.Count;

        StackInto(
            _page,
            Header("Music", first: true),
            MusicSection(),
            Header("Style"),
            _stylePicker,
            Header("Layout & Timing"),
            _shared,
            _styleHeader,
            _styleBody,
            Header("Screen Saver"),
            _saverStatus,
            TimeoutRow(),
            SaverButtons(),
            new Note("Changes save immediately. The running screen saver picks them up "
                     + "within a few seconds.") { Margin = new Padding(0, 16, 0, 0) });

        ResumeLayout(performLayout: true);
        KeepFitted(_shared);

        LoadIntoControls();
        RebuildStyleOptions();
        RefreshSaverStatus();
        RefreshMusicSection();
    }

    /// <summary>
    /// Chooses the window size once the window is real.
    /// </summary>
    /// <remarks>
    /// Not in the constructor. Before the handle exists there is no display, no
    /// scaling and no width, so every note reports the wrong height and the
    /// window comes out the wrong size. Here everything is known.
    /// </remarks>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Measure();
        SizeToContent();
    }

    private Panel BuildFooter()
    {
        var bar = _footer;
        bar.Height = 58;
        bar.Padding = new Padding(20, 10, 20, 14);

        // No anchoring: the Layout handler below places both buttons, and an
        // anchor would fight it every time the window is resized.
        var reset = new Button { Text = "Reset to Defaults", AutoSize = true };
        reset.Click += (_, _) =>
        {
            _settings = Settings.Default;
            _store.SaveSettings(_settings);
            LoadIntoControls();
            RebuildStyleOptions();
        };

        var done = new Button { Text = "Done", AutoSize = true };
        done.Click += (_, _) => Close();
        AcceptButton = done;

        bar.Controls.Add(reset);
        bar.Controls.Add(done);

        // Anchoring inside a panel that has just been resized by scaling is not
        // reliable on its own, so the two buttons are placed on every layout.
        bar.Layout += (_, _) =>
        {
            var top = (bar.ClientSize.Height - reset.Height) / 2;
            var left = new Point(bar.Padding.Left, top);
            var right = new Point(bar.ClientSize.Width - bar.Padding.Right - done.Width, top);

            // Guarded. Moving a child raises the parent's Layout event, so
            // assigning unconditionally here is an endless loop.
            if (reset.Location != left) reset.Location = left;
            if (done.Location != right) done.Location = right;
        };

        return bar;
    }

    /// <summary>
    /// Where the music comes from, and who to ask.
    /// </summary>
    /// <remarks>
    /// These two are not in settings.json and must not be. See
    /// <see cref="TrayConfig"/>: that file is shared byte for byte with the
    /// macOS build, which keeps the same two values in its own preferences.
    /// </remarks>
    private Control MusicSection()
    {
        var body = new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };

        _sourcePicker.DropDownStyle = ComboBoxStyle.DropDownList;
        _sourcePicker.Height = _sourcePicker.PreferredHeight;
        foreach (var kind in MusicSourceKinds.All) _sourcePicker.Items.Add(kind.Title());

        _sourcePicker.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _sourcePicker.SelectedIndex < 0) return;

            TrayConfig.Source = MusicSourceKinds.All[_sourcePicker.SelectedIndex];
            RefreshMusicSection();
        };

        _lastFmUser.Margin = new Padding(0, 0, 0, 4);

        // Saved on the way out of the box rather than on every keystroke, so a
        // half-typed username never becomes the configured one. The same trap
        // the macOS build hit from the other side: an uncommitted edit sitting
        // in a field and needing Save pressed twice.
        _lastFmUser.Leave += (_, _) => CommitUsername();
        _lastFmUser.KeyDown += (_, key) =>
        {
            if (key.KeyCode != Keys.Enter) return;
            key.SuppressKeyPress = true;
            CommitUsername();
        };

        _testAccount.Text = "Check";
        _testAccount.AutoSize = true;
        _testAccount.Click += async (_, _) => await CheckAccountAsync();

        StackInto(
            body,
            new Row(RowLabel("Where music comes from"), _sourcePicker, null, _metrics, readoutColumn: false)
            {
                MiddleSample = "This PC or Last.fm",
                Margin = new Padding(0, 0, 0, 6),
            },
            _sourceBlurb,
            new Row(RowLabel("Last.fm username"), _lastFmUser, _testAccount, _metrics, readoutColumn: true)
            {
                Margin = new Padding(0, 0, 0, 4),
            },
            _accountStatus);

        return body;
    }

    private void CommitUsername()
    {
        var cleaned = MusicSourceKinds.CleanUsername(_lastFmUser.Text);

        if (cleaned == TrayConfig.LastFmUser)
        {
            // Still put the cleaned form back, so pasting a profile address
            // visibly becomes the username rather than sitting there looking
            // like it was ignored.
            if (_lastFmUser.Text != cleaned) _lastFmUser.Text = cleaned;
            return;
        }

        TrayConfig.LastFmUser = cleaned;
        _lastFmUser.Text = cleaned;
        RefreshMusicSection();
    }

    private async Task CheckAccountAsync()
    {
        CommitUsername();

        _accountStatus.Text = "Checking...";
        _testAccount.Enabled = false;

        try
        {
            using var source = new LastFmSource();
            using var giveUp = new CancellationTokenSource(TimeSpan.FromSeconds(25));

            var complaint = await source.TestAsync(_lastFmUser.Text, giveUp.Token);

            _accountStatus.Text = complaint is null
                ? $"{_lastFmUser.Text} answered. Your history will be read within half an hour."
                : $"Last.fm said: {complaint}";
        }
        catch (Exception error)
        {
            _accountStatus.Text = $"Could not reach Last.fm: {error.Message}";
        }
        finally
        {
            _testAccount.Enabled = true;
        }
    }

    /// <summary>
    /// Shows only what the chosen source needs, and says plainly when something
    /// is missing.
    /// </summary>
    private void RefreshMusicSection()
    {
        var kind = TrayConfig.Source;
        var lastfm = kind == MusicSourceKind.LastFm;

        _sourceBlurb.Text = kind.Blurb();

        _lastFmUser.Enabled = lastfm;
        _testAccount.Enabled = lastfm;

        if (_lastFmUser.Parent is Row row) row.Dimmed = !lastfm;

        _accountStatus.Text = !lastfm
            ? ""
            : !TrayConfig.HasLastFmKey
                ? "This build has no Last.fm key in it, so Last.fm cannot be used."
                : TrayConfig.LastFmUser.Length == 0
                    ? "Type your Last.fm username, then press Check."
                    : $"Set to {TrayConfig.LastFmUser}.";
    }

    private Control TimeoutRow()
    {
        _timeout.DropDownStyle = ComboBoxStyle.DropDownList;
        _timeout.Height = _timeout.PreferredHeight;
        foreach (var minutes in TimeoutChoices) _timeout.Items.Add($"{minutes} minutes");
        _timeout.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || !SaverInstaller.IsInstalled()) return;
            SaverInstaller.Install(TimeoutChoices[_timeout.SelectedIndex] * 60, out _);
            RefreshSaverStatus();
        };

        // The dropdown is given a fixed sensible width rather than the rest of
        // the row: "10 minutes" in a box half the window wide looks broken.
        return new Row(RowLabel("Start after"), _timeout, null, _metrics, readoutColumn: false)
        {
            MiddleSample = "000 minutes",
            Margin = new Padding(0, 0, 0, 4),
        };
    }

    private Control SaverButtons()
    {
        var install = new Button { Text = "Make this my screen saver", AutoSize = true };
        install.Click += (_, _) =>
        {
            var seconds = TimeoutChoices[Math.Max(0, _timeout.SelectedIndex)] * 60;
            SaverInstaller.Install(seconds, out var message);
            MessageBox.Show(this, message, "Album Cover Screen Saver",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshSaverStatus();
        };

        var test = new Button { Text = "Test it now", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        test.Click += (_, _) => TestScreenSaver();

        // Allowed to wrap, so a long caption drops to a second line rather than
        // being cut in half.
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Margin = new Padding(_metrics.Label + Gap, 2, 0, 0),
        };

        actions.Controls.Add(install);
        actions.Controls.Add(test);
        return actions;
    }

    /// <summary>A small bold caption above a group, matching the Mac window.</summary>
    /// <remarks>
    /// <c>UseMnemonic</c> is off, or "Layout &amp; Timing" loses its ampersand and
    /// renders as "Layout  Timing": a Label reads "&amp;" as the marker for a
    /// keyboard shortcut and swallows it. The same goes for every label here
    /// that shows text we did not write, such as an album called "Halos &amp;
    /// Horns".
    /// </remarks>
    private Label Header(string text, bool first = false) => new()
    {
        Text = text,
        UseMnemonic = false,
        AutoSize = false,
        Height = Font.Height + 8,
        ForeColor = SystemColors.GrayText,
        Font = new Font(Font, FontStyle.Bold),
        TextAlign = ContentAlignment.BottomLeft,
        Margin = new Padding(0, first ? 10 : 22, 0, 8),
    };

    private Label RowLabel(string text) => new()
    {
        Text = text,
        UseMnemonic = false,
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleRight,
        Height = Font.Height + 4,
    };

    // --- the controls themselves --------------------------------------------

    private Control MakeControl(SettingControl control)
    {
        switch (control)
        {
            case SliderControl slider:
                return MakeSlider(slider);

            case ToggleControl toggle:
            {
                var box = new CheckBox
                {
                    Text = toggle.Label,
                    AutoSize = false,
                    Height = Font.Height + 10,
                    // Indented so it lines up with the sliders rather than with
                    // the labels, which is where the Mac window puts it.
                    Margin = new Padding(_metrics.Label + Gap, 4, 0, 6),
                };
                if (toggle.Tooltip is not null) Tips.SetToolTip(box, toggle.Tooltip);

                box.CheckedChanged += (_, _) =>
                {
                    if (_loading) return;
                    toggle.Write(_settings, box.Checked);
                    Save();
                    UpdateDependentControls();
                };

                _byKey[toggle.Key] = box;
                _refreshers.Add(() => box.Checked = toggle.Read(_settings));
                return box;
            }

            case StyleChoiceControl choice:
            {
                var options = choice.Choices();
                var picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
                picker.Height = picker.PreferredHeight;
                foreach (var mode in options) picker.Items.Add(mode.Title());
                if (choice.Tooltip is not null) Tips.SetToolTip(picker, choice.Tooltip);

                picker.SelectedIndexChanged += (_, _) =>
                {
                    if (_loading || picker.SelectedIndex < 0) return;
                    choice.Write(_settings, options[picker.SelectedIndex]);
                    Save();
                };

                _byKey[choice.Key] = picker;
                _refreshers.Add(() =>
                {
                    var index = options.ToList().IndexOf(choice.Read(_settings));
                    picker.SelectedIndex = Math.Max(0, index);
                });

                // No readout, so the dropdown runs to the right edge.
                return new Row(RowLabel(choice.Label), picker, null, _metrics, readoutColumn: false)
                {
                    Margin = new Padding(0, 0, 0, 6),
                };
            }
        }

        throw new ArgumentOutOfRangeException(nameof(control));
    }

    private Control MakeSlider(SliderControl control)
    {
        // A TrackBar only understands integers, so every slider runs 0 to 1000
        // and the real value is mapped on and off it.
        var bar = new TrackBar
        {
            Minimum = 0,
            Maximum = 1000,
            TickStyle = TickStyle.None,
            AutoSize = false,
            Height = Font.Height + 18,
        };
        if (control.Tooltip is not null) Tips.SetToolTip(bar, control.Tooltip);

        var readout = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.GrayText,
            UseMnemonic = false,
            Height = Font.Height + 4,
        };

        double FromTicks(int ticks) =>
            control.Minimum + ((control.Maximum - control.Minimum) * ticks / 1000.0);

        int ToTicks(double value) => (int)Math.Round(
            (value - control.Minimum) / (control.Maximum - control.Minimum) * 1000.0);

        bar.ValueChanged += (_, _) =>
        {
            if (_loading) return;

            var value = FromTicks(bar.Value);
            if (control.Whole) value = Math.Round(value);

            control.Write(_settings, value);

            // Read back rather than echoing what was written: the setter may
            // have rounded it, or snapped it onto a real record speed.
            readout.Text = control.Format(_settings, control.Read(_settings));

            if (control.Key is "tileSize" or "tempo") UpdateGridNote();
            Save();
        };

        _byKey[control.Key] = bar;

        _refreshers.Add(() =>
        {
            var value = control.Read(_settings);
            bar.Value = Math.Clamp(ToTicks(value), bar.Minimum, bar.Maximum);
            readout.Text = control.Format(_settings, value);
        });

        return new Row(RowLabel(control.Label), bar, readout, _metrics, readoutColumn: true)
        {
            Margin = new Padding(0, 0, 0, 2),
        };
    }

    // --- the style section --------------------------------------------------

    private void RebuildStyleOptions()
    {
        // Every control in here is thrown away and rebuilt, so the refreshers
        // pointing at them must go too, or they fire at disposed controls.
        if (_refreshers.Count > _sharedRefreshers)
        {
            _refreshers.RemoveRange(_sharedRefreshers, _refreshers.Count - _sharedRefreshers);
        }

        _styleBody.SuspendLayout();

        foreach (var child in _styleBody.Controls.Cast<Control>().ToArray())
        {
            foreach (var key in _byKey.Where(pair => IsInside(child, pair.Value))
                                      .Select(pair => pair.Key).ToArray())
            {
                _byKey.Remove(key);
            }

            _styleBody.Controls.Remove(child);
            child.Dispose();
        }

        _styleHeader.Text = _settings.Mode.Title() + " options";

        StackInto(
            _styleBody,
            SettingsSchema.For(_settings.Mode).Select(MakeControl).ToArray());

        _styleBody.ResumeLayout(performLayout: true);
        foreach (Control child in _styleBody.Controls) child.PerformLayout();
        FitHeight(_styleBody);

        LoadIntoControls();
        UpdateDependentControls();
    }

    /// <summary>True if <paramref name="control"/> is the row, or sits in it.</summary>
    private static bool IsInside(Control row, Control control) =>
        ReferenceEquals(row, control) || (control.Parent is not null && ReferenceEquals(row, control.Parent));

    /// <summary>
    /// Greys out a control that would silently do nothing, rather than leaving
    /// it there to be dragged with no effect. Drifting Float's cover size is
    /// meaningless while sizes are randomised by depth.
    /// </summary>
    private void UpdateDependentControls()
    {
        if (_settings.Mode != CollageMode.Drift) return;
        if (!_byKey.TryGetValue("driftScale", out var scale)) return;

        scale.Enabled = !_settings.DriftRandomSize;
        if (scale.Parent is Row row) row.Dimmed = !scale.Enabled;
    }

    // --- state --------------------------------------------------------------

    private void LoadIntoControls()
    {
        _loading = true;
        try
        {
            _stylePicker.SelectedIndex = CollageModes.All.ToList().IndexOf(_settings.Mode);
            foreach (var refresh in _refreshers) refresh();
            UpdateGridNote();

            _sourcePicker.SelectedIndex = MusicSourceKinds.All.ToList().IndexOf(TrayConfig.Source);
            _lastFmUser.Text = TrayConfig.LastFmUser;

            var minutes = Math.Max(1, SaverInstaller.CurrentTimeoutSeconds() / 60);
            var choice = Array.IndexOf(TimeoutChoices, minutes);
            _timeout.SelectedIndex = choice >= 0 ? choice : 3;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Sizes the window to what it holds, capped to the screen.
    /// </summary>
    /// <remarks>
    /// The width is chosen, not measured. The window is a single column of rows
    /// that stretch, so there is no natural width to discover, and asking the
    /// page how wide it would like to be is what produced a window one column
    /// wide the last three times.
    /// </remarks>
    private void SizeToContent()
    {
        var working = Screen.FromControl(this).WorkingArea;
        var scale = DeviceDpi / 96f;

        // Width first and on its own, because the height depends on it: every
        // note has to be given its width before it can say how many lines it
        // wraps to.
        var wanted = _metrics.Label + _metrics.Readout + (int)(280 * scale) + _page.Padding.Horizontal;
        var width = Math.Clamp(wanted, (int)(560 * scale), Math.Max((int)(560 * scale), working.Width - 120));

        ClientSize = new Size(width, ClientSize.Height);

        // Twice. The first pass gives every note its real width; only then can
        // it say how many lines it wraps to, and the second pass adds those
        // heights up. One pass leaves a band of empty window under the content.
        var content = 0;
        for (var pass = 0; pass < 2; pass++)
        {
            PerformLayout();
            FitHeight(_shared);
            FitHeight(_styleBody);

            content = _page.Padding.Vertical;
            foreach (Control child in _page.Controls) content += child.Height + child.Margin.Vertical;
        }

        ClientSize = new Size(
            width, Math.Min(working.Height - 100, content + _footer.Height));

        // Wide enough that the sliders never have to fight for room, and tall
        // enough to show the buttons. Below this the page scrolls instead.
        MinimumSize = new Size(
            Width - ClientSize.Width + (int)(460 * scale),
            Height - ClientSize.Height + (int)(320 * scale));
    }

    /// <summary>
    /// Says how many covers the current tile size actually produces here, which
    /// is the only way a number in points means anything to a person.
    /// </summary>
    private void UpdateGridNote()
    {
        try
        {
            var screen = Screen.FromControl(this).Bounds;
            var scale = DeviceDpi / 96f;
            var layout = GridBuilder.Measure(
                screen.Width / scale, screen.Height / scale, (float)_settings.TileSize);

            _gridNote.Text = $"≈ {layout.Columns} × {layout.Rows} = "
                             + $"{layout.Columns * layout.Rows} covers on this display";
        }
        catch (Exception)
        {
            _gridNote.Text = "";
        }
    }

    private void RefreshSaverStatus()
    {
        if (SaverInstaller.IsInstalled())
        {
            var minutes = Math.Max(1, SaverInstaller.CurrentTimeoutSeconds() / 60);
            _saverStatus.Text =
                $"This is your screen saver, starting after {minutes} "
                + $"minute{(minutes == 1 ? "" : "s")} of no activity. It will not be "
                + "listed in the Windows Screen Saver dialog, which is expected. If you "
                + "open that dialog, close it with Cancel: pressing OK there switches "
                + "this off.";
        }
        else if (SaverInstaller.FindScreenSaver() is null)
        {
            _saverStatus.Text = "The screen saver has not been built yet.";
        }
        else
        {
            _saverStatus.Text = "Not currently your screen saver.";
        }
    }

    private void TestScreenSaver()
    {
        var saver = SaverInstaller.FindScreenSaver();
        if (saver is null) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(saver)
            {
                Arguments = "/s",
                UseShellExecute = true,
            });
        }
        catch (Exception error)
        {
            Log.Failure("running the screen saver", error);
        }
    }

    private void Save()
    {
        _saveSoon.Stop();
        _saveSoon.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // Anything still pending is written now rather than lost.
        if (_saveSoon.Enabled)
        {
            _saveSoon.Stop();
            _store.SaveSettings(_settings);
        }
        _saveSoon.Dispose();
        base.OnFormClosed(e);
    }

    // --- the two layout pieces ----------------------------------------------

    /// <summary>
    /// One settings row: a right-aligned label, a control, and a readout.
    /// </summary>
    /// <remarks>
    /// The whole point of this class is the line that caps the label and readout
    /// against the row's real width. Fixed widths are a guess made before the
    /// window exists; this is measured after it does, every time it changes, so
    /// the control in the middle always gets a usable share and can never be
    /// squeezed to a hairline.
    /// </remarks>
    private sealed class Row : Panel
    {
        private readonly Control? _label;
        private readonly Control _middle;
        private readonly Control? _readout;
        private readonly Metrics _metrics;
        private readonly bool _hasReadout;

        /// <summary>
        /// Text the middle control should be just wide enough to hold, instead
        /// of stretching. "10 minutes" in a box half the window wide looks
        /// broken, so the timeout dropdown uses this.
        /// </summary>
        public string? MiddleSample { get; init; }

        public Row(Control? label, Control middle, Control? readout, Metrics metrics, bool readoutColumn)
        {
            _label = label;
            _middle = middle;
            _readout = readout;
            _metrics = metrics;
            _hasReadout = readoutColumn;

            Dock = DockStyle.Top;
            Height = Math.Max(middle.Height, label?.Height ?? 0);

            if (label is not null) Controls.Add(label);
            Controls.Add(middle);
            if (readout is not null) Controls.Add(readout);
        }

        /// <summary>Greys the label and readout when the control is disabled.</summary>
        public bool Dimmed
        {
            set
            {
                if (_label is not null) _label.ForeColor = value ? SystemColors.GrayText : SystemColors.ControlText;
                if (_readout is not null) _readout.ForeColor = value ? SystemColors.ControlDark : SystemColors.GrayText;
            }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            var width = ClientSize.Width;
            var height = ClientSize.Height;
            var gap = Gap * DeviceDpi / 96;

            // Capped, not trusted. Even a measurement that is out by a factor of
            // two leaves the middle control at least a third of the row.
            var left = _label is null ? 0 : Math.Min(_metrics.Label, width * 40 / 100);
            var right = !_hasReadout || _readout is null ? 0 : Math.Min(_metrics.Readout, width * 26 / 100);

            var middleLeft = left + (left > 0 ? gap : 0);
            var middleWidth = Math.Max(60, width - middleLeft - right - (right > 0 ? gap : 0));

            if (MiddleSample is not null)
            {
                // Measured here rather than stored, so it is right at whatever
                // scaling this window is on now.
                var sample = TextRenderer.MeasureText(MiddleSample, Font).Width + (32 * DeviceDpi / 96);
                middleWidth = Math.Min(middleWidth, sample);
            }

            void Place(Control? control, int x, int controlWidth)
            {
                if (control is null) return;
                control.Bounds = new Rectangle(
                    x, Math.Max(0, (height - control.Height) / 2), Math.Max(1, controlWidth), control.Height);
            }

            base.OnLayout(e);

            Place(_label, 0, left);
            Place(_middle, middleLeft, middleWidth);
            Place(_readout, width - right, right);
        }
    }

    /// <summary>
    /// A wrapping grey caption that works out its own height once it knows how
    /// wide it is, so it is never clipped and never leaves a gap.
    /// </summary>
    private sealed class Note : Label
    {
        public Note(string text)
        {
            AutoSize = false;
            Dock = DockStyle.Top;
            ForeColor = SystemColors.GrayText;
            UseMnemonic = false;
            Text = text;
        }

        /// <summary>
        /// When set, the note is inset to line up under the sliders. Read at
        /// layout time rather than stored, because the metrics change when the
        /// window moves to a display at different scaling.
        /// </summary>
        public Metrics? IndentTo { get; init; }

        private int Indent => IndentTo is null ? 0 : IndentTo.Label + (Gap * DeviceDpi / 96);

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            FitHeight();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            FitHeight();
        }

        private void FitHeight()
        {
            if (Padding.Left != Indent) Padding = new Padding(Indent, 0, 0, 0);

            var usable = Math.Max(60, Width - Indent);
            var needed = string.IsNullOrEmpty(Text)
                ? 0
                : TextRenderer.MeasureText(
                    Text, Font, new Size(usable, int.MaxValue), TextFormatFlags.WordBreak).Height;

            // Guarded, or setting the height re-enters this through OnResize.
            if (Height != needed) Height = needed;
        }
    }
}

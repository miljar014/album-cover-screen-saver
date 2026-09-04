import AppKit

/// Connects a listening source.
///
/// Laid out with stack views and auto layout rather than hand-computed frames.
/// The previous version measured wrapped text itself and got it wrong, so labels
/// clipped mid-sentence and buttons landed on top of the lines above them.
final class SetupWindowController: NSWindowController {
    static let shared = SetupWindowController()

    private let windowWidth: CGFloat = 660
    private let inset: CGFloat = 30
    private var contentWidth: CGFloat { windowWidth - inset * 2 }
    private var stepTextWidth: CGFloat { contentWidth - 34 }

    /// Only the two options that suit most people. Spotify is not a third tab —
    /// it is a separate screen, reached deliberately.
    private let pickerKinds: [MusicSourceKind] = [.local, .lastfm]
    private let sourcePicker = NSSegmentedControl(
        labels: [MusicSourceKind.local.title, MusicSourceKind.lastfm.title],
        trackingMode: .selectOne, target: nil, action: nil)
    private var advancedLink = NSButton()
    private var backLink = NSButton()
    private var spotifyMode = false
    private let userField = NSTextField(string: "")
    private let idField = NSTextField(string: "")
    private let uriField = NSTextField(string: "")
    private let status = NSTextField(labelWithString: "")
    private let stack = NSStackView()
    private var sections: [MusicSourceKind: NSView] = [:]
    var onSaved: (() -> Void)?

    private init() {
        let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 660, height: 620),
                         styleMask: [.titled, .closable], backing: .buffered, defer: false)
        w.title = "Connect Your Music"
        w.isReleasedWhenClosed = false
        super.init(window: w)
        build()
    }

    required init?(coder: NSCoder) { fatalError("unused") }

    private var chosenKind: MusicSourceKind {
        if spotifyMode { return .spotify }
        let i = max(0, min(pickerKinds.count - 1, sourcePicker.selectedSegment))
        return pickerKinds[i]
    }

    func show() {
        idField.stringValue = UserDefaults.standard.string(forKey: "spotifyClientID") ?? ""
        userField.stringValue = Config.lastFMUser
        // Someone already on Spotify opens straight onto that screen, rather than
        // finding their own source missing.
        spotifyMode = Config.sourceKind == .spotify
        sourcePicker.selectedSegment = pickerKinds.firstIndex(of: Config.sourceKind) ?? 0
        status.stringValue = ""
        applySource()
        window?.center()
        NSApp.activate(ignoringOtherApps: true)
        showWindow(nil)
        window?.makeKeyAndOrderFront(nil)
    }

    // MARK: building blocks

    private func wrapping(_ text: String, size: CGFloat, weight: NSFont.Weight = .regular,
                          colour: NSColor, width: CGFloat) -> NSTextField {
        let l = NSTextField(wrappingLabelWithString: text)
        l.font = .systemFont(ofSize: size, weight: weight)
        l.textColor = colour
        l.lineBreakMode = .byWordWrapping
        // Without this the field reports a single-line intrinsic height and the
        // text gets clipped.
        l.preferredMaxLayoutWidth = width
        l.setContentCompressionResistancePriority(.required, for: .vertical)
        l.translatesAutoresizingMaskIntoConstraints = false
        l.widthAnchor.constraint(equalToConstant: width).isActive = true
        return l
    }

    private func vstack(_ views: [NSView], spacing: CGFloat = 6) -> NSStackView {
        let s = NSStackView(views: views)
        s.orientation = .vertical
        s.alignment = .leading
        s.spacing = spacing
        s.translatesAutoresizingMaskIntoConstraints = false
        return s
    }

    private func badge(_ n: Int) -> NSView {
        let v = NSTextField(labelWithString: "\(n)")
        v.font = .systemFont(ofSize: 12, weight: .bold)
        v.textColor = .white
        v.alignment = .center
        v.drawsBackground = false
        v.wantsLayer = true
        v.layer?.backgroundColor = NSColor.controlAccentColor.cgColor
        v.layer?.cornerRadius = 11
        v.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([
            v.widthAnchor.constraint(equalToConstant: 22),
            v.heightAnchor.constraint(equalToConstant: 22),
        ])
        return v
    }

    private func step(_ n: Int, _ title: String, _ detail: String,
                      extra: NSView? = nil) -> NSView {
        var column: [NSView] = [
            wrapping(title, size: 13, weight: .semibold, colour: .labelColor,
                     width: stepTextWidth),
            wrapping(detail, size: 11.5, colour: .secondaryLabelColor,
                     width: stepTextWidth),
        ]
        if let extra { column.append(extra) }

        let row = NSStackView(views: [badge(n), vstack(column)])
        row.orientation = .horizontal
        row.alignment = .top
        row.spacing = 12
        row.translatesAutoresizingMaskIntoConstraints = false
        return row
    }

    private func button(_ title: String, _ action: Selector, width w: CGFloat) -> NSButton {
        let b = NSButton(title: title, target: self, action: action)
        b.bezelStyle = .rounded
        b.translatesAutoresizingMaskIntoConstraints = false
        b.widthAnchor.constraint(equalToConstant: w).isActive = true
        return b
    }

    private func field(_ f: NSTextField, placeholder: String, mono: Bool,
                       width w: CGFloat) -> NSTextField {
        f.placeholderString = placeholder
        f.target = self
        f.action = #selector(save)
        if mono { f.font = .monospacedSystemFont(ofSize: 12, weight: .regular) }
        f.translatesAutoresizingMaskIntoConstraints = false
        f.widthAnchor.constraint(equalToConstant: w).isActive = true
        return f
    }

    // MARK: build

    private func build() {
        let content = window!.contentView!

        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 18
        stack.translatesAutoresizingMaskIntoConstraints = false
        content.addSubview(stack)

        let title = wrapping("Connect your music", size: 18, weight: .semibold,
                             colour: .labelColor, width: contentWidth)
        stack.addArrangedSubview(title)

        sourcePicker.target = self
        sourcePicker.action = #selector(applySource)
        sourcePicker.translatesAutoresizingMaskIntoConstraints = false
        sourcePicker.segmentDistribution = .fillEqually
        sourcePicker.widthAnchor.constraint(equalToConstant: 260).isActive = true
        stack.addArrangedSubview(sourcePicker)

        for kind in MusicSourceKind.allCases {
            let section = buildSection(kind)
            sections[kind] = section
            stack.addArrangedSubview(section)
        }

        advancedLink = linkButton("Advanced: use your own Spotify developer app \u{2192}",
                                  #selector(enterSpotifyMode))
        stack.addArrangedSubview(advancedLink)

        backLink = linkButton("\u{2190} Back to the simple options",
                              #selector(leaveSpotifyMode))
        stack.addArrangedSubview(backLink)

        status.font = .systemFont(ofSize: 11)
        status.lineBreakMode = .byTruncatingTail
        status.translatesAutoresizingMaskIntoConstraints = false
        status.widthAnchor.constraint(equalToConstant: contentWidth).isActive = true

        let buttons = NSStackView(views: [
            button("Later", #selector(closeWindow), width: 90),
            button("Save & Connect", #selector(save), width: 160),
        ])
        buttons.orientation = .horizontal
        buttons.spacing = 10
        buttons.translatesAutoresizingMaskIntoConstraints = false
        (buttons.arrangedSubviews.last as? NSButton)?.keyEquivalent = "\r"
        content.addSubview(status)
        content.addSubview(buttons)

        NSLayoutConstraint.activate([
            stack.topAnchor.constraint(equalTo: content.topAnchor, constant: 24),
            stack.leadingAnchor.constraint(equalTo: content.leadingAnchor, constant: inset),
            buttons.trailingAnchor.constraint(equalTo: content.trailingAnchor, constant: -inset),
            buttons.bottomAnchor.constraint(equalTo: content.bottomAnchor, constant: -20),
            status.leadingAnchor.constraint(equalTo: content.leadingAnchor, constant: inset),
            status.bottomAnchor.constraint(equalTo: buttons.topAnchor, constant: -14),
        ])
    }

    private func buildSection(_ kind: MusicSourceKind) -> NSView {
        var rows: [NSView] = [
            wrapping(kind.blurb, size: 11.5, colour: .secondaryLabelColor,
                     width: contentWidth)
        ]

        switch kind {
        case .local:
            rows.append(step(1, "There is nothing to set up",
                             "Press Save & Connect and start playing music. Album art "
                           + "appears as you listen, and the collage grows from there."))
            rows.append(step(2, "Allow automation, once",
                             "macOS will ask permission to read the track playing in "
                           + "Spotify or Music. That prompt is the only setup step — "
                           + "playback is never controlled, only read."))
            rows.append(wrapping("This sees only the desktop apps, not the Spotify web "
                               + "player or your phone, and it starts with an empty "
                               + "collage rather than your past listening. Choose "
                               + "Last.fm if you want your whole history from day one.",
                                 size: 11, colour: .tertiaryLabelColor,
                                 width: contentWidth))

        case .lastfm:
            rows.append(step(1, "Have a Last.fm account",
                             "Free. Skip this if you already have one.",
                             extra: button("Create a Last.fm Account",
                                           #selector(openLastFMSignup), width: 210)))
            rows.append(step(2, "Connect Spotify to Last.fm",
                             "On Last.fm go to Settings → Applications and connect "
                           + "Spotify. Everything you play is recorded from then on.",
                             extra: button("Open Last.fm Applications",
                                           #selector(openLastFMConnect), width: 210)))
            rows.append(step(3, "Enter your Last.fm username",
                             "Just the username — no password, nothing else to set up.",
                             extra: field(userField, placeholder: "your Last.fm username",
                                          mono: false, width: stepTextWidth)))
            if !Config.usesBundledLastFMKey {
                rows.append(wrapping("This build has no Last.fm API key compiled in, so "
                                   + "Last.fm will not work. Put one in the LastFMKey "
                                   + "file and rebuild.",
                                     size: 11, colour: .systemRed, width: contentWidth))
            }

        case .spotify:
            uriField.stringValue = SpotifyAuth.shared.redirectURI
            uriField.isEditable = false
            let uriRow = NSStackView(views: [
                field(uriField, placeholder: "", mono: true, width: stepTextWidth - 100),
                button("Copy", #selector(copyURI), width: 90),
            ])
            uriRow.orientation = .horizontal
            uriRow.spacing = 10
            uriRow.translatesAutoresizingMaskIntoConstraints = false

            rows.append(step(1, "Open the Spotify developer dashboard",
                             "Log in normally. If Developer Terms appear, accept them — "
                           + "the dashboard only shows up afterwards.",
                             extra: button("Open Spotify Dashboard",
                                           #selector(openDashboard), width: 200)))
            rows.append(step(2, "Click “Create app” and name it anything",
                             "The name must not begin with “Spot” — Spotify rejects "
                           + "those. Website can be left blank."))
            rows.append(step(3, "Add this Redirect URI, then click Add",
                             "Must be 127.0.0.1, not “localhost”. If it is still sitting "
                           + "in the text box when you save, sign-in fails later with a "
                           + "redirect mismatch.", extra: uriRow))
            rows.append(step(4, "Tick “Web API”, save, then paste the Client ID",
                             "32 letters and digits, from the app’s page. There is no "
                           + "client secret — this uses PKCE.",
                             extra: field(idField,
                                          placeholder: "e.g. a0006a15a9304a7e8704bf682caf8497",
                                          mono: true, width: stepTextWidth)))
        }

        return vstack(rows, spacing: 16)
    }

    // MARK: actions

    /// A quiet text link rather than a button — it should read as a way out, not
    /// as a third equal choice.
    private func linkButton(_ title: String, _ action: Selector) -> NSButton {
        let b = NSButton(title: title, target: self, action: action)
        b.isBordered = false
        b.contentTintColor = .linkColor
        b.font = .systemFont(ofSize: 11.5)
        b.translatesAutoresizingMaskIntoConstraints = false
        return b
    }

    @objc private func enterSpotifyMode() {
        spotifyMode = true
        applySource()
    }

    @objc private func leaveSpotifyMode() {
        spotifyMode = false
        sourcePicker.selectedSegment = pickerKinds.firstIndex(of: Config.sourceKind) ?? 0
        applySource()
    }

    @objc func applySource() {
        let chosen = chosenKind
        for (kind, view) in sections { view.isHidden = kind != chosen }
        sourcePicker.isHidden = spotifyMode
        advancedLink.isHidden = spotifyMode
        backLink.isHidden = !spotifyMode
        // Sections differ in height, so the window is sized to whichever is shown.
        window?.layoutIfNeeded()
        let needed = stack.fittingSize.height + 24 + 100
        window?.setContentSize(NSSize(width: windowWidth, height: max(360, needed)))
    }

    @objc private func openDashboard() {
        NSWorkspace.shared.open(URL(string: "https://developer.spotify.com/dashboard")!)
    }
    @objc private func openLastFMSignup() {
        NSWorkspace.shared.open(URL(string: "https://www.last.fm/join")!)
    }
    @objc private func openLastFMConnect() {
        NSWorkspace.shared.open(URL(string: "https://www.last.fm/settings/applications")!)
    }
    @objc private func copyURI() {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(SpotifyAuth.shared.redirectURI, forType: .string)
        status.textColor = .secondaryLabelColor
        status.stringValue = "Copied — paste into Redirect URIs, then click Add."
    }
    @objc private func closeWindow() { window?.close() }

    @objc private func save() {
        // A text field being edited keeps its text in the field editor, so
        // `stringValue` is stale until editing ends. Without this the first click
        // only commits the edit and the second one does the work.
        window?.makeFirstResponder(nil)

        let chosen = chosenKind
        Config.sourceKind = chosen

        switch chosen {
        case .local:
            Config.setAccountLabel("This Mac")
        case .lastfm:
            let user = userField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !user.isEmpty else {
                status.textColor = .systemRed
                status.stringValue = "Enter your Last.fm username."
                return
            }
            Config.lastFMUser = user
            Config.setAccountLabel("\(user) · Last.fm")
        case .spotify:
            let id = idField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
            Config.setClientID(id)
            guard Config.hasClientID else {
                status.textColor = .systemRed
                status.stringValue = id.isEmpty
                    ? "Paste the Client ID from your Spotify app’s page."
                    : "That doesn’t look like a Client ID — expected 32 letters and digits."
                return
            }
        }
        window?.close()
        onSaved?()
    }
}

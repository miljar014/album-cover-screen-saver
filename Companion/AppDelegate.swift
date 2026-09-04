import AppKit
import ServiceManagement

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var statusItem: NSStatusItem!
    /// Held for the app's lifetime. Without it macOS App Naps this process —
    /// it's a windowless background agent, which is exactly what App Nap targets
    /// — and throttles its timers to as little as once a minute. That throttling
    /// is worst when the Mac is idle, i.e. precisely while the screen saver is
    /// running. `allowingIdleSystemSleep` keeps normal sleep behaviour intact.
    private var activityToken: NSObjectProtocol?

    func applicationDidFinishLaunching(_ n: Notification) {
        activityToken = ProcessInfo.processInfo.beginActivity(
            options: [.userInitiatedAllowingIdleSystemSleep],
            reason: "Keeping Spotify playback current for the screen saver")

        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let button = statusItem.button {
            button.image = NSImage(systemSymbolName: "square.grid.3x3.fill",
                                   accessibilityDescription: "AlbumCoverScreenSaver")
            button.image?.isTemplate = true
        }
        // The screen saver ships inside this app; put it where macOS looks for it.
        // Doing it on every launch is what carries a Sparkle update through to the
        // saver, since Sparkle can only replace the .app itself.
        SaverInstaller.installIfNewer()

        if Config.sourceKind == .local { Config.setAccountLabel("This Mac") }
        Poller.shared.onChange = { [weak self] in self?.rebuildMenu() }
        Poller.shared.start()
        rebuildMenu()

        // A build with a Client ID baked in has nothing to set up: the user just
        // signs in. Only builds without one need the registration walkthrough.
        if Config.sourceKind == .local {
            // Nothing to connect; it just works.
        } else if Config.sourceKind == .lastfm {
            if Config.lastFMUser.isEmpty {
                SetupWindowController.shared.onSaved = { Poller.shared.start() }
                SetupWindowController.shared.show()
            }
        } else if !Config.hasClientID {
            SetupWindowController.shared.onSaved = { [weak self] in self?.signIn() }
            SetupWindowController.shared.show()
        } else if !SpotifyAuth.shared.isSignedIn {
            promptFirstRun()
        }
    }

    // MARK: menu

    private func rebuildMenu() {
        let m = NSMenu()
        // Last.fm has no sign-in step: a username is the whole of it.
        let signedIn: Bool
        switch Config.sourceKind {
        case .local:   signedIn = true
        case .lastfm:  signedIn = !Config.lastFMUser.isEmpty
        case .spotify: signedIn = SpotifyAuth.shared.isSignedIn
        }

        let statusText: String
        if Config.sourceKind == .local && LocalPlayerSource.permissionDenied {
            statusText = "Automation access denied — see System Settings › Privacy"
        } else if Config.sourceKind == .spotify && !Config.hasClientID {
            statusText = "Setup needed — no Spotify Client ID"
        } else if Config.sourceKind == .lastfm && Config.lastFMUser.isEmpty {
            statusText = "Setup needed — no Last.fm username"
        } else if signedIn {
            statusText = Poller.shared.lastStatus
        } else {
            statusText = "Not connected"
        }
        let status = NSMenuItem(title: statusText,
                                action: nil, keyEquivalent: "")
        status.isEnabled = false
        m.addItem(status)

        let np = Poller.shared.nowPlaying
        if signedIn, np.isLive {
            let playing = NSMenuItem(
                title: "\u{266A} \(np.track) \u{2014} \(np.artist)",
                action: nil, keyEquivalent: "")
            playing.isEnabled = false
            m.addItem(playing)
        }
        m.addItem(.separator())

        // Once connected, name the account rather than repeating the setup prompt.
        let setupTitle: String
        if let account = Config.accountLabel, signedIn {
            setupTitle = account
        } else if Config.sourceKind == .local {
            setupTitle = "Music Source: This Mac\u{2026}"
        } else {
            setupTitle = "Choose Music Source\u{2026}"
        }
        m.addItem(item(setupTitle, #selector(openSetup)))

        let settings = item("Settings\u{2026}", #selector(openSettings))
        settings.keyEquivalent = ","
        settings.keyEquivalentModifierMask = .command
        m.addItem(settings)
        m.addItem(.separator())

        if signedIn {
            m.addItem(item("Refresh Now", #selector(refresh)))
            if Config.sourceKind != .local {
                m.addItem(item("Rebuild Archive from Top Albums", #selector(rebuild)))
            }
            m.addItem(.separator())
        }
        if Config.sourceKind != .local {
            let name = Config.sourceKind.title
            m.addItem(item(signedIn ? "Disconnect \(name)" : "Connect \(name)…",
                           signedIn ? #selector(signOut) : #selector(signIn)))
            m.addItem(.separator())
        }
        m.addItem(item("Open Screen Saver Settings…", #selector(openScreenSaverPrefs)))
        m.addItem(item("Reveal Art Cache in Finder", #selector(revealCache)))

        let launch = item("Open at Login", #selector(toggleLaunchAtLogin))
        if #available(macOS 13, *) {
            launch.state = SMAppService.mainApp.status == .enabled ? .on : .off
        } else {
            launch.isEnabled = false
        }
        m.addItem(launch)

        if Updater.shared.isAvailable {
            m.addItem(.separator())
            m.addItem(item("Check for Updates…", #selector(checkForUpdates)))
            let auto = item("Update Automatically", #selector(toggleAutoUpdate))
            auto.state = Updater.shared.automaticallyChecks ? .on : .off
            m.addItem(auto)
        }

        m.addItem(.separator())

        let info = Bundle.main.infoDictionary
        let short = info?["CFBundleShortVersionString"] as? String ?? "?"
        let build = info?["CFBundleVersion"] as? String ?? "?"
        let version = NSMenuItem(title: "Version \(short) (\(build))",
                                 action: nil, keyEquivalent: "")
        version.isEnabled = false
        m.addItem(version)

        m.addItem(item("Quit Album Cover Screen Saver", #selector(quit)))
        statusItem.menu = m
    }

    @objc private func checkForUpdates() { Updater.shared.checkForUpdates() }

    @objc private func toggleAutoUpdate() {
        Updater.shared.automaticallyChecks.toggle()
        rebuildMenu()
    }

    private func item(_ title: String, _ sel: Selector) -> NSMenuItem {
        let i = NSMenuItem(title: title, action: sel, keyEquivalent: "")
        i.target = self
        return i
    }

    // MARK: actions

    @objc private func signIn() {
        if Config.sourceKind == .lastfm {
            SetupWindowController.shared.onSaved = { Poller.shared.start() }
            SetupWindowController.shared.show()
            return
        }
        SpotifyAuth.shared.signIn { [weak self] result in
            switch result {
            case .success:
                Poller.shared.start()
                Poller.shared.fetchAccount()
            case .failure(let e):
                if Config.sourceKind == .local {
            // Nothing to connect; it just works.
        } else if Config.sourceKind == .lastfm {
            if Config.lastFMUser.isEmpty {
                SetupWindowController.shared.onSaved = { Poller.shared.start() }
                SetupWindowController.shared.show()
            }
        } else if !Config.hasClientID {
                    self?.openSetup()
                } else {
                    self?.alert("Couldn't sign in", e.localizedDescription)
                }
            }
            self?.rebuildMenu()
        }
    }

    @objc private func signOut() {
        SpotifyAuth.shared.signOut()
        Config.lastFMUser = ""
        Config.setAccountLabel(nil)
        rebuildMenu()
    }

    @objc private func openSettings() { SettingsWindowController.shared.show() }

    @objc private func openSetup() {
        SetupWindowController.shared.onSaved = { [weak self] in
            // Last.fm and This Mac need no sign-in; only Spotify does.
            if Config.sourceKind == .spotify {
                self?.signIn()
            } else {
                Poller.shared.start()
                Poller.shared.fetchAccount()
                self?.rebuildMenu()
            }
        }
        SetupWindowController.shared.show()
    }

    @objc private func quit() { NSApp.terminate(nil) }

    @objc private func refresh() { Poller.shared.poll() }

    @objc private func rebuild() { Poller.shared.poll(force: true) }

    @objc private func revealCache() {
        SharedStore.ensureDirs()
        NSWorkspace.shared.selectFile(nil, inFileViewerRootedAtPath: SharedStore.root.path)
    }

    @objc private func openScreenSaverPrefs() {
        let url = URL(string: "x-apple.systempreferences:com.apple.ScreenSaver-Settings.extension")!
        if !NSWorkspace.shared.open(url) {
            NSWorkspace.shared.open(URL(fileURLWithPath:
                "/System/Library/PreferencePanes/DesktopScreenEffectsPref.prefPane"))
        }
    }

    @objc private func toggleLaunchAtLogin() {
        guard #available(macOS 13, *) else { return }
        do {
            if SMAppService.mainApp.status == .enabled { try SMAppService.mainApp.unregister() }
            else { try SMAppService.mainApp.register() }
        } catch {
            alert("Couldn't change login item", error.localizedDescription)
        }
        rebuildMenu()
    }

    // MARK: helpers

    private func promptFirstRun() {
        let a = NSAlert()
        a.messageText = "Connect your Spotify account"
        a.informativeText = """
        Album Cover Screen Saver builds a growing archive of the album art you listen to, \
        and feeds it to the screen saver.

        Sign in once and it keeps itself up to date in the background.
        """
        a.addButton(withTitle: "Sign In")
        a.addButton(withTitle: "Later")
        if a.runModal() == .alertFirstButtonReturn { signIn() }
    }

    private func alert(_ title: String, _ body: String) {
        let a = NSAlert()
        a.messageText = title
        a.informativeText = body
        a.alertStyle = .warning
        a.runModal()
    }
}

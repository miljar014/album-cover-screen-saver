import AppKit

/// Sparkle, behind a thin wall.
///
/// Compiled with `-DSPARKLE` once `Vendor/Sparkle` exists, so the project still
/// builds — minus updating — on a machine that has not run `tools/release-setup.sh`.
/// Everything the rest of the app touches goes through this type, so the
/// `#if` lives in exactly one file.
#if SPARKLE
import Sparkle

final class Updater {
    static let shared = Updater()

    private let controller: SPUStandardUpdaterController

    private init() {
        // startingUpdater: true schedules the background check itself; the interval
        // and whether it runs at all come from the Info.plist keys and from
        // whatever the user last chose in the menu.
        controller = SPUStandardUpdaterController(startingUpdater: true,
                                                  updaterDelegate: nil,
                                                  userDriverDelegate: nil)
    }

    var isAvailable: Bool { true }

    /// A menu bar agent has no windows and no Dock tile, so Sparkle's dialog can
    /// open behind whatever the user is looking at. Activating first puts it in front.
    func checkForUpdates() {
        NSApp.activate(ignoringOtherApps: true)
        controller.updater.checkForUpdates()
    }

    var automaticallyChecks: Bool {
        get { controller.updater.automaticallyChecksForUpdates }
        set { controller.updater.automaticallyChecksForUpdates = newValue }
    }

    var lastCheck: Date? { controller.updater.lastUpdateCheckDate }
}

#else

final class Updater {
    static let shared = Updater()
    var isAvailable: Bool { false }
    func checkForUpdates() {}
    var automaticallyChecks: Bool {
        get { false }
        set { _ = newValue }
    }
    var lastCheck: Date? { nil }
}

#endif

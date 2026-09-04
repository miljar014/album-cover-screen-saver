import AppKit

/// Keeps the installed screen saver in step with the app.
///
/// The app ships the `.saver` inside its own Resources and copies it into
/// `~/Library/Screen Savers` whenever the bundled build is newer than what is
/// installed. That is what makes auto-update work at all: Sparkle can only
/// replace the `.app`, so the saver has to ride along inside it. It also turns
/// installation into a single drag — no second item, no Install script.
enum SaverInstaller {

    static let productName = "Album Cover Screen Saver"

    /// Older names this product shipped under, cleared out on the way past.
    private static let legacyNames = ["SpotifyCollage.saver", "AlbumCoverScreenSaver.saver"]

    static var bundledSaver: URL? {
        Bundle.main.url(forResource: productName, withExtension: "saver")
    }

    static var savedSaversDirectory: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Screen Savers", isDirectory: true)
    }

    static var installedSaver: URL {
        savedSaversDirectory.appendingPathComponent("\(productName).saver")
    }

    /// Build numbers are timestamps (`20260904210530`), so they compare as numbers.
    /// Falling back to 0 makes an unreadable or missing bundle look old, which is
    /// the safe direction: we reinstall rather than skip.
    private static func build(of bundle: URL) -> Double {
        guard let plist = NSDictionary(contentsOf:
                bundle.appendingPathComponent("Contents/Info.plist")),
              let v = plist["CFBundleVersion"] as? String else { return 0 }
        return Double(v) ?? 0
    }

    /// Copies the bundled saver over the installed one when it is newer.
    /// Returns true if anything was written.
    @discardableResult
    static func installIfNewer() -> Bool {
        guard let src = bundledSaver else { return false }
        let fm = FileManager.default

        for old in legacyNames {
            try? fm.removeItem(at: savedSaversDirectory.appendingPathComponent(old))
        }

        let dst = installedSaver
        if fm.fileExists(atPath: dst.path), build(of: dst) >= build(of: src) { return false }

        do {
            try fm.createDirectory(at: savedSaversDirectory, withIntermediateDirectories: true)
            // Stage beside the destination and swap, so a failure part-way through
            // cannot leave a half-copied bundle where macOS will try to load it.
            let staging = savedSaversDirectory
                .appendingPathComponent(".\(productName).saver.incoming")
            try? fm.removeItem(at: staging)
            try fm.copyItem(at: src, to: staging)
            if fm.fileExists(atPath: dst.path) {
                _ = try fm.replaceItemAt(dst, withItemAt: staging)
            } else {
                try fm.moveItem(at: staging, to: dst)
            }
            reloadScreenSaverHelpers()
            return true
        } catch {
            NSLog("[AlbumCover] saver install failed: \(error.localizedDescription)")
            return false
        }
    }

    /// macOS keeps the previously loaded `.saver` binary alive in these helpers, so
    /// a fresh copy on disk keeps running the old code until they restart. They are
    /// relaunched on demand, so ending them is safe and is the only way to make an
    /// update take effect without a logout.
    private static func reloadScreenSaverHelpers() {
        for name in ["legacyScreenSaver", "WallpaperAgent"] {
            let p = Process()
            p.executableURL = URL(fileURLWithPath: "/usr/bin/pkill")
            p.arguments = ["-f", name]
            p.standardError = FileHandle.nullDevice
            try? p.run()
        }
    }

    /// True when the saver has never been installed — the app uses this to point a
    /// first-run user at System Settings.
    static var isInstalled: Bool {
        FileManager.default.fileExists(atPath: installedSaver.path)
    }
}

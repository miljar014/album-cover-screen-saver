import Foundation

/// One album that appeared in the user's listening history.
struct AlbumEntry: Codable, Hashable {
    /// Sources without stable album ids (Last.fm) get a deterministic one derived
    /// from artist and title. It doubles as the art cache filename, so it must be
    /// filesystem-safe.
    static func makeID(artist: String, album: String) -> String {
        let raw = "\(artist)|\(album)".lowercased()
        let safe = raw.map { $0.isLetter || $0.isNumber ? String($0) : "-" }.joined()
        return "lfm-" + String(safe.prefix(80))
    }

    let id: String              // Spotify album id
    var name: String
    var artist: String
    var imageURL: String        // remote art URL (300px preferred)
    var firstSeen: Date         // when it entered the archive
    var lastPlayed: Date        // most recent play we know about
    var playCount: Int          // times observed in recently-played
}

/// Persistent, ever-growing archive of album art the user has listened to.
struct Archive: Codable {
    var albums: [String: AlbumEntry] = [:]
    /// Cursor: ms timestamp of the newest play we've already ingested.
    var lastCursorMs: Int = 0
    var updated: Date = .distantPast

    /// Newest-first, for "hero" and recency-weighted rendering.
    var byRecency: [AlbumEntry] { albums.values.sorted { $0.lastPlayed > $1.lastPlayed } }
}

/// What Spotify is playing right now, written by the companion app so the
/// screen saver can feature it without ever talking to Spotify itself.
struct NowPlaying: Codable {
    var albumID: String = ""
    var albumName: String = ""
    var artist: String = ""
    var track: String = ""
    var imageURL: String = ""
    var isPlaying: Bool = false
    var updated: Date = .distantPast
    var progressMs: Int = 0
    var durationMs: Int = 0

    /// How far through the track we are, 0...1.
    ///
    /// The app only refreshes this every 15 seconds, so the elapsed time since
    /// that snapshot is added back in. Without it the tonearm would sit still and
    /// then jump; with it the motion is continuous.
    var progressFraction: Double {
        guard durationMs > 0 else { return 0 }
        let since = isPlaying ? max(0, Date().timeIntervalSince(updated)) * 1000 : 0
        return min(1, max(0, (Double(progressMs) + since) / Double(durationMs)))
    }

    /// Playback signals go stale fast — a paused Mac shouldn't keep showing a
    /// track from an hour ago as "now playing".
    var isLive: Bool {
        isPlaying && !albumID.isEmpty && Date().timeIntervalSince(updated) < 90
    }
}

/// Which renderer the screen saver uses, chosen in its Options sheet.
enum CollageMode: String, Codable, CaseIterable {
    case mosaic     = "mosaic"      // dense grid, tiles flip in place
    case drift      = "drift"       // covers drift and scale across a dark field
    case wall       = "wall"        // builds up from empty, dissolves, rebuilds
    case hero       = "hero"        // one large featured album + grid around it
    case vinyl      = "vinyl"       // mid-century turntable, art as the record label
    case ambient    = "ambient"     // cover floating in a field of its own colours
    case cassette   = "cassette"    // 1980s tape deck, reels tracking the song
    case crate      = "crate"       // thumbing through a record crate
    case gallery    = "gallery"     // framed covers under a moving spotlight
    case coverflow  = "coverflow"   // 3D carousel with reflections
    case jukebox    = "jukebox"     // neon Wurlitzer arch
    case starfield  = "starfield"   // covers orbiting the current album
    case crt        = "crt"         // phosphor terminal, cover as halftone
    case cdplayer   = "cdplayer"    // 90s CD player, jewel cases around it
    case newsstand  = "newsstand"   // the album as a newspaper front page
    case vaporwave  = "vaporwave"   // neon horizon and chrome type
    case polaroid   = "polaroid"    // instant photos pinned to cork
    case zoetrope   = "zoetrope"    // covers on a spinning slitted drum
    case subway     = "subway"      // posters on a tiled platform wall

    var title: String {
        switch self {
        case .mosaic: return "Mosaic Grid"
        case .drift:  return "Drifting Float"
        case .wall:   return "Slow-Building Wall"
        case .hero:   return "Hero + Grid"
        case .vinyl:    return "Record Player"
        case .ambient:  return "Ambient Field"
        case .cassette: return "Cassette Deck"
        case .crate:    return "Crate Digging"
        case .gallery:   return "Gallery Wall"
        case .coverflow: return "Cover Flow"
        case .jukebox:   return "Neon Jukebox"
        case .starfield: return "Starfield Orbit"
        case .crt:       return "CRT Terminal"
        case .cdplayer:  return "CD Player"
        case .newsstand: return "Newsstand"
        case .vaporwave: return "Vaporwave Grid"
        case .polaroid:  return "Polaroid Corkboard"
        case .zoetrope:  return "Zoetrope"
        case .subway:    return "Subway Platform"
        }
    }
}

struct Settings: Codable {
    var mode: CollageMode = .mosaic

    // --- shared across every style ---
    /// Target cell size in points. Smaller = more, denser tiles.
    var tileSize: Double = 200
    /// Seconds between visual changes. Lower = busier.
    var tempo: Double = 4.0
    /// Bias selection toward recently played albums (0 = uniform, 1 = strongly recent).
    var recencyBias: Double = 0.4
    var showTrackLabel: Bool = true

    // --- Mosaic Grid ---
    /// How many tiles flip together on each change.
    var flipsAtOnce: Int = 1
    /// Seconds one tile takes to flip.
    var flipDuration: Double = 0.75
    /// Promote some covers to 2x2 blocks so the wall reads as a mosaic rather
    /// than a uniform checkerboard.
    var mosaicRandomSize: Bool = true

    // --- Drifting Float ---
    var driftCount: Int = 16
    var driftSpeed: Double = 1.0      // multiplier on base drift speed
    var driftScale: Double = 1.0      // multiplier on cover size (fixed-size mode)
    /// Vary cover size and tie speed to it, so nearer covers sweep past faster.
    var driftRandomSize: Bool = true

    // --- Slow-Building Wall ---
    var wallHold: Double = 6.0        // seconds held once the wall is full
    var wallBuildSpeed: Double = 1.0  // multiplier on fill/dissolve rate

    // --- Hero + Grid ---
    var heroSize: Double = 0.56       // fraction of screen height
    var heroDim: Double = 0.35        // how much the background grid is dimmed
    var heroInterval: Double = 12.0   // seconds between hero swaps
    /// Pin the featured cover to whatever Spotify is playing right now.
    var heroFollowNowPlaying: Bool = true

    // --- Record Player ---
    /// Seconds a record plays before the arm lifts and the next one drops.
    /// Only used when not following live playback — when Spotify is playing, the
    /// arm tracks the real position in the track instead.
    var vinylSecondsPerRecord: Double = 180
    /// Drawn platter speed. A real LP turns at 33⅓, but on a large screen that
    /// reads as frantic rather than hypnotic, so the default is slower.
    var vinylRPM: Double = 9
    var vinylFollowNowPlaying: Bool = true
    /// How many records accumulate on the table before the oldest is retired.
    var vinylSleeveCount: Int = 26
    /// Multiplier on sleeve size.
    var vinylSleeveScale: Double = 1.0
    /// Record Player needs music. When nothing is playing, this style is shown
    /// instead.
    var vinylFallbackMode: CollageMode = .mosaic

    // --- shared by the single-album styles (Ambient, Cassette, Crate, Gallery) ---
    /// Seconds an album is featured when Spotify isn't playing. While it is,
    /// these styles follow the current track instead.
    var featureSeconds: Double = 30
    /// Ambient Field: how fast the colour field drifts.
    var ambientMotion: Double = 1.0
    /// Crate Digging: how many sleeves are in the crate.
    var crateCount: Int = 16
    /// Gallery Wall: frames across.
    var galleryColumns: Int = 4
    /// Cover Flow: how many covers ride the carousel.
    var flowCount: Int = 16
    /// Starfield: how many covers orbit, and how fast.
    var orbitCount: Int = 14
    var orbitSpeed: Double = 1.0
    /// CRT Terminal: amber phosphor when true, green when false.
    var crtAmber: Bool = true
    /// CD Player: jewel cases scattered around the deck.
    var cdCaseCount: Int = 9
    /// Polaroid Corkboard: photos pinned up.
    var polaroidCount: Int = 9
    /// Zoetrope: covers around the drum.
    var zoetropeCount: Int = 14

    static let `default` = Settings()

    /// Decoded leniently: a settings file written by an older build is missing
    /// the newer keys, and synthesised Codable would reject the whole file and
    /// silently reset every preference. Each key falls back to its default.
    init() {}

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = Settings()
        mode           = (try? c.decodeIfPresent(CollageMode.self, forKey: .mode) ?? d.mode) ?? d.mode
        tileSize       = try c.decodeIfPresent(Double.self, forKey: .tileSize)       ?? d.tileSize
        tempo          = try c.decodeIfPresent(Double.self, forKey: .tempo)          ?? d.tempo
        recencyBias    = try c.decodeIfPresent(Double.self, forKey: .recencyBias)    ?? d.recencyBias
        showTrackLabel = try c.decodeIfPresent(Bool.self,   forKey: .showTrackLabel) ?? d.showTrackLabel
        flipsAtOnce    = try c.decodeIfPresent(Int.self,    forKey: .flipsAtOnce)    ?? d.flipsAtOnce
        flipDuration   = try c.decodeIfPresent(Double.self, forKey: .flipDuration)   ?? d.flipDuration
        mosaicRandomSize = try c.decodeIfPresent(Bool.self, forKey: .mosaicRandomSize) ?? d.mosaicRandomSize
        driftCount     = try c.decodeIfPresent(Int.self,    forKey: .driftCount)     ?? d.driftCount
        driftSpeed     = try c.decodeIfPresent(Double.self, forKey: .driftSpeed)     ?? d.driftSpeed
        driftScale     = try c.decodeIfPresent(Double.self, forKey: .driftScale)     ?? d.driftScale
        driftRandomSize = try c.decodeIfPresent(Bool.self, forKey: .driftRandomSize)  ?? d.driftRandomSize
        wallHold       = try c.decodeIfPresent(Double.self, forKey: .wallHold)       ?? d.wallHold
        wallBuildSpeed = try c.decodeIfPresent(Double.self, forKey: .wallBuildSpeed) ?? d.wallBuildSpeed
        heroSize       = try c.decodeIfPresent(Double.self, forKey: .heroSize)       ?? d.heroSize
        heroDim        = try c.decodeIfPresent(Double.self, forKey: .heroDim)        ?? d.heroDim
        heroInterval   = try c.decodeIfPresent(Double.self, forKey: .heroInterval)   ?? d.heroInterval
        heroFollowNowPlaying = try c.decodeIfPresent(Bool.self, forKey: .heroFollowNowPlaying)
                               ?? d.heroFollowNowPlaying
        vinylSecondsPerRecord = try c.decodeIfPresent(Double.self, forKey: .vinylSecondsPerRecord)
                                ?? d.vinylSecondsPerRecord
        vinylRPM = try c.decodeIfPresent(Double.self, forKey: .vinylRPM) ?? d.vinylRPM
        vinylFollowNowPlaying = try c.decodeIfPresent(Bool.self, forKey: .vinylFollowNowPlaying)
                                ?? d.vinylFollowNowPlaying
        vinylSleeveCount = try c.decodeIfPresent(Int.self, forKey: .vinylSleeveCount)
                           ?? d.vinylSleeveCount
        vinylSleeveScale = try c.decodeIfPresent(Double.self, forKey: .vinylSleeveScale)
                           ?? d.vinylSleeveScale
        vinylFallbackMode = (try? c.decodeIfPresent(CollageMode.self, forKey: .vinylFallbackMode)
                             ?? d.vinylFallbackMode) ?? d.vinylFallbackMode
        featureSeconds  = try c.decodeIfPresent(Double.self, forKey: .featureSeconds)  ?? d.featureSeconds
        ambientMotion   = try c.decodeIfPresent(Double.self, forKey: .ambientMotion)   ?? d.ambientMotion
        crateCount      = try c.decodeIfPresent(Int.self,    forKey: .crateCount)      ?? d.crateCount
        galleryColumns  = try c.decodeIfPresent(Int.self,    forKey: .galleryColumns)  ?? d.galleryColumns
        flowCount       = try c.decodeIfPresent(Int.self,    forKey: .flowCount)       ?? d.flowCount
        orbitCount      = try c.decodeIfPresent(Int.self,    forKey: .orbitCount)      ?? d.orbitCount
        orbitSpeed      = try c.decodeIfPresent(Double.self, forKey: .orbitSpeed)      ?? d.orbitSpeed
        crtAmber        = try c.decodeIfPresent(Bool.self,   forKey: .crtAmber)        ?? d.crtAmber
        cdCaseCount     = try c.decodeIfPresent(Int.self,    forKey: .cdCaseCount)     ?? d.cdCaseCount
        polaroidCount   = try c.decodeIfPresent(Int.self,    forKey: .polaroidCount)   ?? d.polaroidCount
        zoetropeCount   = try c.decodeIfPresent(Int.self,    forKey: .zoetropeCount)   ?? d.zoetropeCount
    }
}

/// Where the companion app and the screen saver meet.
///
/// Two locations are involved, and neither is reliable on its own:
///
/// * `~/Library/Application Support/AlbumCoverScreenSaver` — the app's own folder.
///   Always writable by the app; readable by the sandboxed saver only via the
///   read-only temporary-exception entitlement in Saver.entitlements.
/// * The saver's sandbox container — always readable by the saver, but macOS can
///   refuse to let another app write into it, silently.
///
/// So the app writes to both, and the saver reads whichever actually holds the
/// newest data.
enum SharedStore {
    static let folderName = "AlbumCoverScreenSaver"
    static let saverContainer =
        "Library/Containers/com.apple.ScreenSaver.Engine.legacyScreenSaver/Data/Library/Application Support"

    /// The user's real home directory. `homeDirectoryForCurrentUser` returns the
    /// sandbox container when called from inside the saver; the passwd entry
    /// gives the true path in both processes.
    static var realHome: URL {
        if let pw = getpwuid(getuid()) {
            let dir = String(cString: pw.pointee.pw_dir)
            if !dir.isEmpty { return URL(fileURLWithPath: dir) }
        }
        return FileManager.default.homeDirectoryForCurrentUser
    }

    static var appSupportRoot: URL {
        realHome.appendingPathComponent("Library/Application Support", isDirectory: true)
                .appendingPathComponent(folderName, isDirectory: true)
    }

    static var containerRoot: URL {
        #if SAVER_TARGET
        let base = FileManager.default.urls(for: .applicationSupportDirectory,
                                            in: .userDomainMask).first!
        return base.appendingPathComponent(folderName, isDirectory: true)
        #else
        return realHome.appendingPathComponent(saverContainer, isDirectory: true)
                       .appendingPathComponent(folderName, isDirectory: true)
        #endif
    }

    static var roots: [URL] { [appSupportRoot, containerRoot] }

    /// Newest modification time for a shared file across every root.
    static func newestStamp(of name: String) -> Date {
        roots.map { stamp($0.appendingPathComponent(name)) }.max() ?? .distantPast
    }

    private static func stamp(_ u: URL) -> Date {
        let attrs = try? FileManager.default.attributesOfItem(atPath: u.path)
        return (attrs?[.modificationDate] as? Date) ?? .distantPast
    }

    /// Root holding the newest readable archive. Cached — the saver asks for art
    /// paths hundreds of times a second and this touches the filesystem.
    private static var cachedRoot: URL?

    static func refreshRoot() {
        var best: (Date, URL)?
        for r in roots {
            let a = r.appendingPathComponent("archive.json")
            guard FileManager.default.isReadableFile(atPath: a.path) else { continue }
            let d = stamp(a)
            if best == nil || d > best!.0 { best = (d, r) }
        }
        cachedRoot = best?.1
    }

    static var root: URL {
        #if SAVER_TARGET
        if let c = cachedRoot { return c }
        refreshRoot()
        return cachedRoot ?? containerRoot
        #else
        return appSupportRoot
        #endif
    }

    static var artDir: URL { root.appendingPathComponent("art", isDirectory: true) }
    static var archiveURL: URL { root.appendingPathComponent("archive.json") }
    static var settingsURL: URL { root.appendingPathComponent("settings.json") }
    static func artFile(for albumID: String) -> URL {
        artDir.appendingPathComponent("\(albumID).jpg")
    }

    /// Moves data written under the app's previous name. Without this the rename
    /// would silently orphan the archive and the whole art cache.
    static func migrateLegacyFolders() {
        let fm = FileManager.default
        for root in roots {
            let legacy = root.deletingLastPathComponent()
                             .appendingPathComponent("SpotifyCollage", isDirectory: true)
            guard fm.fileExists(atPath: legacy.path),
                  !fm.fileExists(atPath: root.path) else { continue }
            try? fm.moveItem(at: legacy, to: root)
        }
    }

    static func ensureDirs() {
        for r in roots {
            try? FileManager.default.createDirectory(
                at: r.appendingPathComponent("art", isDirectory: true),
                withIntermediateDirectories: true)
        }
    }

    // MARK: writing

    /// Last failure per path, so a blocked write shows up in doctor.log rather
    /// than disappearing into a `try?`.
    ///
    /// Guarded by a lock: album art downloads run four at a time on a background
    /// queue and every one of them can append here. Mutating a Swift Array from
    /// several threads corrupts memory, which crashed the app with a segfault
    /// rather than any kind of error message.
    private static let errorLock = NSLock()
    private static var _writeErrors: [String] = []

    static var writeErrors: [String] {
        errorLock.lock(); defer { errorLock.unlock() }
        return _writeErrors
    }

    @discardableResult
    static func write(_ data: Data, named name: String) -> Int {
        var ok = 0
        var failures: [String] = []
        for r in roots {
            let u = r.appendingPathComponent(name)
            do {
                try FileManager.default.createDirectory(at: u.deletingLastPathComponent(),
                                                        withIntermediateDirectories: true)
                try data.write(to: u, options: .atomic)
                ok += 1
            } catch {
                failures.append("\(u.path) -> \(error.localizedDescription)")
            }
        }
        if !failures.isEmpty { recordFailures(failures) }
        return ok
    }

    private static func recordFailures(_ failures: [String]) {
        errorLock.lock()
        _writeErrors.append(contentsOf: failures)
        let text = _writeErrors.suffix(40).joined(separator: "\n") + "\n"
        errorLock.unlock()

        let log = appSupportRoot.appendingPathComponent("write-errors.log")
        try? FileManager.default.createDirectory(at: appSupportRoot,
                                                 withIntermediateDirectories: true)
        try? text.data(using: .utf8)?.write(to: log, options: .atomic)
    }

    /// Album art goes to every root, so whichever one the saver ends up reading
    /// has the covers to go with its archive.
    @discardableResult
    static func writeArt(_ data: Data, for albumID: String) -> Int {
        write(data, named: "art/\(albumID).jpg")
    }

    static func hasArt(_ albumID: String) -> Bool {
        roots.allSatisfy {
            FileManager.default.fileExists(
                atPath: $0.appendingPathComponent("art/\(albumID).jpg").path)
        }
    }

    // MARK: reading

    static func loadArchive() -> Archive {
        var best: (Date, Archive)?
        for r in roots {
            let u = r.appendingPathComponent("archive.json")
            guard let data = try? Data(contentsOf: u),
                  let a = try? JSONDecoder.iso.decode(Archive.self, from: data) else { continue }
            let d = stamp(u)
            if best == nil || d > best!.0 { best = (d, a) }
        }
        return best?.1 ?? Archive()
    }

    @discardableResult
    static func saveArchive(_ a: Archive) -> Int {
        guard let data = try? JSONEncoder.iso.encode(a) else { return 0 }
        return write(data, named: "archive.json")
    }

    static func loadSettings() -> Settings {
        var best: (Date, Settings)?
        for r in roots {
            let u = r.appendingPathComponent("settings.json")
            guard let data = try? Data(contentsOf: u),
                  let s = try? JSONDecoder.iso.decode(Settings.self, from: data) else { continue }
            let d = stamp(u)
            if best == nil || d > best!.0 { best = (d, s) }
        }
        return best?.1 ?? .default
    }

    static func loadNowPlaying() -> NowPlaying {
        var best: (Date, NowPlaying)?
        for r in roots {
            let u = r.appendingPathComponent("nowplaying.json")
            guard let data = try? Data(contentsOf: u),
                  let n = try? JSONDecoder.iso.decode(NowPlaying.self, from: data) else { continue }
            let d = stamp(u)
            if best == nil || d > best!.0 { best = (d, n) }
        }
        return best?.1 ?? NowPlaying()
    }

    @discardableResult
    static func saveNowPlaying(_ n: NowPlaying) -> Int {
        guard let data = try? JSONEncoder.iso.encode(n) else { return 0 }
        return write(data, named: "nowplaying.json")
    }

    @discardableResult
    static func saveSettings(_ s: Settings) -> Int {
        guard let data = try? JSONEncoder.iso.encode(s) else { return 0 }
        return write(data, named: "settings.json")
    }
}

extension JSONEncoder {
    static var iso: JSONEncoder {
        let e = JSONEncoder(); e.dateEncodingStrategy = .iso8601; return e
    }
}
extension JSONDecoder {
    static var iso: JSONDecoder {
        let d = JSONDecoder(); d.dateDecodingStrategy = .iso8601; return d
    }
}

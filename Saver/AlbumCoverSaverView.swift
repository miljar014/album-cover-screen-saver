import ScreenSaver
import AppKit

/// The screen saver never talks to Spotify. It reads the archive and art cache
/// that the companion app maintains inside this process's sandbox container,
/// which means it also works fine offline.
@objc(AlbumCoverSaverView)
final class AlbumCoverSaverView: ScreenSaverView {

    // state shared by all renderers
    var settings = Settings.default
    var albums: [AlbumEntry] = []          // newest-first
    var albumIndexByID: [String: Int] = [:]
    var nowPlaying = NowPlaying()
    var phase: TimeInterval = 0            // seconds since animation started
    private var lastArchiveStamp: Date = .distantPast
    private var lastReloadCheck: TimeInterval = -100
    private var lastNowPlayingCheck: TimeInterval = -100
    private var lastNowPlayingStamp: Date = .distantPast
    private var lastLiveAt: TimeInterval = -1000
    private var configWindow: NSWindow?

    // per-mode scratch state, reset when the mode or size changes
    var tiles: [Tile] = []
    var floaters: [Floater] = []
    var wallOrder: [Int] = []
    var wallCycleStart: TimeInterval = 0
    var heroIndex: Int = 0
    var heroPrevIndex: Int = 0
    var heroSwapAt: TimeInterval = 0
    var heroFadeStart: TimeInterval = -1
    var nextFlipAt: TimeInterval = 0
    // record player
    var vinylAngle: CGFloat = 0
    var vinylIndex: Int = 0
    var vinylPrevIndex: Int = 0
    var vinylChangeStart: TimeInterval = -1
    var vinylStartedAt: TimeInterval = 0
    var sleeves: [Sleeve] = []
    /// Items pinned to a persistent board, oldest first.
    var table: [TableSleeve] = []
    var tableOwner: CollageMode?
    /// Album currently in the middle of the corkboard, tracked by id so a change
    /// is noticed even while the featured index is mid-animation.
    var lastCentreAlbumID: String?

    // shared by the single-album styles
    var featIndex = 0
    var featPrevIndex = 0
    var featChangeStart: TimeInterval = -1
    var featStartedAt: TimeInterval = 0
    var crateOffset: CGFloat = 0
    var crateItems: [Int] = []
    var galleryItems: [Int] = []
    var flowItems: [Int] = []
    var flowPos: CGFloat = 0
    var orbiters: [Orbiter] = []
    var bubbles: [Bubble] = []
    var spotPhase: CGFloat = 0

    private var layoutMode: CollageMode?
    private var layoutSize: NSSize = .zero

    // MARK: lifecycle

    override init?(frame: NSRect, isPreview: Bool) {
        super.init(frame: frame, isPreview: isPreview)
        animationTimeInterval = 1.0 / 30.0
        wantsLayer = true
        layer?.backgroundColor = NSColor.black.cgColor
        reloadData(force: true)
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) unused") }

    override func startAnimation() {
        super.startAnimation()
        reloadData(force: true)
    }

    override func stopAnimation() {
        super.stopAnimation()
        ImageStore.shared.purge()
        PaletteStore.purge()
        HalftoneStore.purge()
        BlurStore.purge()
        CorkTexture.purge()
        // NOTE: an earlier version called exit(0) here — the common workaround for
        // macOS 14+ leaking ScreenSaverView instances. Don't. macOS treats the
        // host process dying as the module having crashed and silently reverts the
        // user's screen saver choice to the previous one. Releasing the image cache
        // is enough; the process is short-lived anyway.
    }

    override func animateOneFrame() {
        phase += animationTimeInterval
        if phase - lastReloadCheck > 3 { reloadData(force: false) }
        // Track changes should appear immediately, so this is checked far more
        // often than the archive — it's one stat() unless the file actually moved.
        if phase - lastNowPlayingCheck > 0.25 { refreshNowPlaying() }
        ensureLayout()
        advance()
        setNeedsDisplay(bounds)
    }

    override func draw(_ rect: NSRect) {
        NSColor.black.setFill()
        bounds.fill()
        guard !albums.isEmpty else { drawEmptyState(); return }
        switch activeMode {
        case .mosaic: drawMosaic()
        case .drift:  drawDrift()
        case .wall:   drawWall()
        case .hero:   drawHero()
        case .vinyl:    drawVinyl()
        case .ambient:  drawAmbient()
        case .cassette: drawCassette()
        case .crate:    drawCrate()
        case .gallery:   drawGallery()
        case .coverflow: drawCoverFlow()
        case .jukebox:   drawJukebox()
        case .starfield: drawStarfield()
        case .crt:       drawCRT()
        case .cdplayer:  drawCDPlayer()
        case .newsstand: drawNewsstand()
        case .vaporwave: drawVaporwave()
        case .polaroid:  drawPolaroid()
        case .zoetrope:  drawZoetrope()
        case .subway:    drawSubway()
        }
    }

    // MARK: data

    private func reloadData(force: Bool) {
        lastReloadCheck = phase
        // Re-pick which shared location has the freshest data before reading.
        SharedStore.refreshRoot()
        settings = SharedStore.loadSettings()
        nowPlaying = SharedStore.loadNowPlaying()
        if nowPlaying.isLive { lastLiveAt = phase }

        let attrs = try? FileManager.default.attributesOfItem(atPath: SharedStore.archiveURL.path)
        let stamp = (attrs?[.modificationDate] as? Date) ?? .distantPast
        guard force || stamp > lastArchiveStamp else { return }
        lastArchiveStamp = stamp

        let archive = SharedStore.loadArchive()
        // Only keep albums whose art actually made it to disk.
        albums = archive.byRecency.filter {
            FileManager.default.fileExists(atPath: SharedStore.artFile(for: $0.id).path)
        }
        albumIndexByID = Dictionary(uniqueKeysWithValues:
            albums.enumerated().map { ($0.element.id, $0.offset) })
        layoutMode = nil   // force a relayout with the new pool
    }

    private func refreshNowPlaying() {
        lastNowPlayingCheck = phase
        let stamp = SharedStore.newestStamp(of: "nowplaying.json")
        guard stamp > lastNowPlayingStamp else { return }
        lastNowPlayingStamp = stamp
        nowPlaying = SharedStore.loadNowPlaying()
        if nowPlaying.isLive { lastLiveAt = phase }
    }

    private func ensureLayout() {
        guard layoutMode != activeMode || layoutSize != bounds.size else { return }
        layoutMode = activeMode
        layoutSize = bounds.size
        buildLayout()
    }

    /// The style actually being drawn.
    ///
    /// Record Player has nothing to show without music, so when playback stops it
    /// hands over to the chosen fallback. A 20-second grace period means skipping
    /// a track or a brief pause doesn't yank the whole visual away and back.
    var activeMode: CollageMode {
        guard settings.mode == .vinyl else { return settings.mode }
        return (phase - lastLiveAt) < 20 ? .vinyl : settings.vinylFallbackMode
    }

    /// Index of the album Spotify is playing right now — only when the signal is
    /// fresh and we already have its cover on disk.
    var liveAlbumIndex: Int? {
        guard nowPlaying.isLive else { return nil }
        return albumIndexByID[nowPlaying.albumID]
    }
    var liveHeroIndex: Int? { settings.heroFollowNowPlaying ? liveAlbumIndex : nil }
    var liveVinylIndex: Int? { settings.vinylFollowNowPlaying ? liveAlbumIndex : nil }

    // MARK: featured-album helpers (shared by Ambient / Cassette / Crate / Gallery)

    /// Progress through the current track, 0...1 — the real position when Spotify
    /// is playing, otherwise a timer.
    func featProgress(idleSpan: Double) -> CGFloat {
        if liveAlbumIndex != nil, nowPlaying.durationMs > 0 {
            return CGFloat(nowPlaying.progressFraction)
        }
        return min(1, max(0, CGFloat((phase - featStartedAt) / max(5, idleSpan))))
    }

    /// Moves the featured album on: follows live playback when there is any,
    /// otherwise rotates on a timer.
    func advanceFeatured(idleSpan: Double,
                         changeDuration: TimeInterval = AlbumCoverSaverView.featChangeDuration) {
        if featChangeStart >= 0 {
            if phase - featChangeStart >= changeDuration {
                featChangeStart = -1
                featStartedAt = phase
            }
            return
        }
        if let live = liveAlbumIndex {
            if live != featIndex {
                featPrevIndex = featIndex
                featIndex = live
                featChangeStart = phase
            }
            return
        }
        if phase - featStartedAt >= max(5, idleSpan) {
            featPrevIndex = featIndex
            featIndex = pickIndex(avoiding: [featIndex])
            featChangeStart = phase
        }
    }

    /// How long a featured-album swap takes. Long enough to read as a movement
    /// rather than a cut.
    static let featChangeDuration: TimeInterval = 1.35

    /// Linear 0→1 across a change; use `featFade` unless you need the raw value.
    var featFadeRaw: CGFloat {
        guard featChangeStart >= 0 else { return 1 }
        return min(1, max(0, CGFloat((phase - featChangeStart)
                                     / Self.featChangeDuration)))
    }

    /// Eased 0→1 across a featured-album change.
    var featFade: CGFloat { Ease.inOut(featFadeRaw) }

    /// Palette of whichever album is featured.
    var featPalette: ArtPalette {
        guard albums.indices.contains(featIndex) else { return .fallback }
        return PaletteStore.palette(for: albums[featIndex].id, image: image(featIndex))
    }

    /// Title / artist / album for the featured slot, live track when playing.
    var featText: (title: String, artist: String, sub: String) {
        if liveAlbumIndex == featIndex, !nowPlaying.track.isEmpty {
            return (nowPlaying.track, nowPlaying.artist, nowPlaying.albumName)
        }
        guard albums.indices.contains(featIndex) else { return ("", "", "") }
        return (albums[featIndex].name, albums[featIndex].artist, "")
    }

    // MARK: album selection

    /// Picks an album index, biased toward recent listening by `settings.recencyBias`.
    /// The archive is newest-first, so "recent" is simply the front of the array.
    func pickIndex() -> Int {
        guard albums.count > 1 else { return 0 }
        if Double.random(in: 0...1) < settings.recencyBias {
            let head = max(1, Int(Double(albums.count) * 0.2))
            return Int.random(in: 0..<head)
        }
        return Int.random(in: 0..<albums.count)
    }

    /// A distinct index, avoiding anything already on screen where possible.
    func pickIndex(avoiding used: Set<Int>) -> Int {
        guard albums.count > used.count else { return pickIndex() }
        for _ in 0..<24 {
            let i = pickIndex()
            if !used.contains(i) { return i }
        }
        return pickIndex()
    }

    func image(_ index: Int) -> NSImage? {
        guard albums.indices.contains(index) else { return nil }
        return ImageStore.shared.image(for: albums[index].id)
    }

    // MARK: empty state

    private func drawEmptyState() {
        let text = SpotifyAuthHint.message
        let p = NSMutableParagraphStyle(); p.alignment = .center
        let size: CGFloat = isPreview ? 9 : 22
        let attrs: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: size, weight: .medium),
            .foregroundColor: NSColor(white: 0.65, alpha: 1),
            .paragraphStyle: p,
        ]
        let box = NSRect(x: bounds.width * 0.15, y: bounds.midY - 60,
                         width: bounds.width * 0.7, height: 120)
        (text as NSString).draw(in: box, withAttributes: attrs)
    }

    // MARK: options sheet

    override var hasConfigureSheet: Bool { true }

    override var configureSheet: NSWindow? {
        if configWindow == nil { configWindow = ConfigSheet.make() }
        return configWindow
    }
}

enum SpotifyAuthHint {
    static let message = """
    No album art yet.

    Open Album Cover Screen Saver from your menu bar, sign in to Spotify,
    and the collage will fill in as your history is archived.

    Styles and options live in that app's Settings window.
    """
}

/// Small bounded cache — an archive can hold thousands of covers and we only
/// ever show a few dozen at once.
final class ImageStore {
    static let shared = ImageStore()
    private var cache: [String: NSImage] = [:]
    private var order: [String] = []
    private let capacity = 160

    func image(for id: String) -> NSImage? {
        if let img = cache[id] {
            if let i = order.firstIndex(of: id) { order.remove(at: i); order.append(id) }
            return img
        }
        guard let img = NSImage(contentsOf: SharedStore.artFile(for: id)) else { return nil }
        cache[id] = img
        order.append(id)
        while order.count > capacity, let oldest = order.first {
            order.removeFirst(); cache.removeValue(forKey: oldest)
        }
        return img
    }

    func purge() { cache.removeAll(); order.removeAll() }
}

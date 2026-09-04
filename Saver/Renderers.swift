import ScreenSaver
import AppKit

/// One cell of a grid layout.
struct Tile {
    var rect: NSRect
    var index: Int              // album index currently shown
    var nextIndex: Int = -1     // album index being flipped to
    var flipStart: TimeInterval = -1
    var flipDur: TimeInterval = 0.75   // per-tile, so flips don't move in lockstep
    var appearAt: TimeInterval = 0   // wall mode: offset within the cycle
}

/// One free-floating cover in drift mode.
struct Floater {
    var center: NSPoint
    var velocity: NSPoint
    var side: CGFloat
    var index: Int
    var angle: CGFloat
    /// 0 = far away (small, slow, hazy), 1 = close (large, fast, crisp).
    var depth: CGFloat = 0.5
}

private let heroFadeDuration: TimeInterval = 1.1

extension AlbumCoverSaverView {

    // MARK: - Layout

    /// Preview thumbnails are a fraction of screen size; scale the cell target
    /// down so the preview shows a representative grid rather than four covers.
    private var cellTarget: CGFloat {
        let t = CGFloat(settings.tileSize)
        return isPreview ? max(12, t / 6) : t
    }

    func buildLayout() {
        tiles.removeAll(); floaters.removeAll(); wallOrder.removeAll()
        guard !albums.isEmpty, bounds.width > 10, bounds.height > 10 else { return }

        switch activeMode {
        case .mosaic, .wall:
            let grid = settings.mosaicRandomSize
                ? makeVariedGrid(targetSide: cellTarget)
                : makeGrid(targetSide: cellTarget)
            tiles = assignAlbums(grid)
            if activeMode == .wall { startWallCycle() }
            nextFlipAt = phase + settings.tempo
        case .hero:
            tiles = assignAlbums(makeGrid(targetSide: cellTarget * 0.75))
            heroIndex = pickIndex()
            heroPrevIndex = heroIndex
            heroSwapAt = phase + settings.heroInterval
            heroFadeStart = -1
            nextFlipAt = phase + settings.tempo * 1.5
        case .vinyl:
            buildVinylSleeves()
            vinylIndex = liveVinylIndex ?? pickIndex()
            vinylPrevIndex = vinylIndex
            vinylChangeStart = -1
            vinylStartedAt = phase

        case .ambient, .cassette, .gallery:
            featIndex = liveAlbumIndex ?? pickIndex()
            featPrevIndex = featIndex
            featChangeStart = -1
            featStartedAt = phase

        case .coverflow:
            buildFlow()

        case .jukebox:
            buildBubbles()
            featIndex = liveAlbumIndex ?? pickIndex()
            featPrevIndex = featIndex
            featChangeStart = -1
            featStartedAt = phase

        case .starfield:
            buildOrbiters()
            featIndex = liveAlbumIndex ?? pickIndex()
            featPrevIndex = featIndex
            featChangeStart = -1
            featStartedAt = phase

        case .cdplayer:
            buildScatter(count: settings.cdCaseCount, sizeRange: 0.14...0.22,
                         keepOutCentre: NSPoint(x: bounds.width * 0.5, y: bounds.height * 0.52),
                         keepOutRadius: min(bounds.height * 0.36, bounds.width * 0.26) * 1.35)
            featIndex = liveAlbumIndex ?? pickIndex()
            featPrevIndex = featIndex; featChangeStart = -1; featStartedAt = phase

        case .polaroid:
            featIndex = liveAlbumIndex ?? pickIndex()
            featPrevIndex = featIndex; featChangeStart = -1; featStartedAt = phase
            buildPolaroidBoard()

        case .newsstand, .vaporwave, .zoetrope, .subway:
            featIndex = liveAlbumIndex ?? pickIndex()
            featPrevIndex = featIndex; featChangeStart = -1; featStartedAt = phase

        case .crt:
            featIndex = liveAlbumIndex ?? pickIndex()
            featPrevIndex = featIndex
            featChangeStart = -1
            featStartedAt = phase

        case .crate:
            featIndex = liveAlbumIndex ?? pickIndex()
            featPrevIndex = featIndex
            featChangeStart = -1
            featStartedAt = phase
            crateOffset = 0
            buildCrate()

        case .drift:
            let count = isPreview ? max(4, settings.driftCount / 3) : settings.driftCount
            var used = Set<Int>()
            floaters = (0..<count).map { _ in
                let i = pickIndex(avoiding: used); used.insert(i)
                return makeFloater(index: i, seeded: true)
            }
        }
    }

    /// Grid sized to this display so the cells tile it exactly — no partial row
    /// running off the bottom edge.
    ///
    /// A screen's aspect ratio almost never divides into whole square cells, so
    /// the cells come out slightly non-square. Covers are drawn aspect-fill, which
    /// crops a sliver rather than stretching the art; on album covers that reads as
    /// nothing at all, whereas a sliced-off bottom row is obvious.
    private func makeGrid(targetSide: CGFloat) -> [Tile] {
        let cols = max(3, Int((bounds.width / targetSide).rounded()))
        let cellW = bounds.width / CGFloat(cols)
        let rows = max(2, Int((bounds.height / cellW).rounded()))
        let cellH = bounds.height / CGFloat(rows)

        var out: [Tile] = []
        for r in 0..<rows {
            for c in 0..<cols {
                out.append(Tile(rect: NSRect(x: CGFloat(c) * cellW,
                                             y: CGFloat(r) * cellH,
                                             width: cellW, height: cellH),
                                index: 0))
            }
        }
        return out
    }

    /// Same exact-fit grid, but with some covers promoted to 2x2 blocks so the
    /// wall reads as a mosaic instead of a uniform checkerboard.
    ///
    /// Blocks are placed first and their cells marked taken; the leftover gaps are
    /// then filled with single cells. Doing it in that order guarantees the screen
    /// still tiles exactly, with no overlaps and no holes.
    private func makeVariedGrid(targetSide: CGFloat) -> [Tile] {
        let cols = max(3, Int((bounds.width / targetSide).rounded()))
        let cellW = bounds.width / CGFloat(cols)
        let rows = max(2, Int((bounds.height / cellW).rounded()))
        let cellH = bounds.height / CGFloat(rows)
        guard rows > 1, cols > 1 else { return makeGrid(targetSide: targetSide) }

        func rect(_ r: Int, _ c: Int, _ span: Int) -> NSRect {
            NSRect(x: CGFloat(c) * cellW, y: CGFloat(r) * cellH,
                   width: cellW * CGFloat(span), height: cellH * CGFloat(span))
        }

        var taken = Array(repeating: Array(repeating: false, count: cols), count: rows)
        var out: [Tile] = []

        // Roughly one big cover per seven cells: enough to break the grid up,
        // sparse enough that the big ones still feel like accents.
        for _ in 0..<max(1, (rows * cols) / 7) {
            let r = Int.random(in: 0..<(rows - 1))
            let c = Int.random(in: 0..<(cols - 1))
            guard !taken[r][c], !taken[r + 1][c], !taken[r][c + 1], !taken[r + 1][c + 1]
            else { continue }
            taken[r][c] = true;     taken[r + 1][c] = true
            taken[r][c + 1] = true; taken[r + 1][c + 1] = true
            out.append(Tile(rect: rect(r, c, 2), index: 0))
        }

        for r in 0..<rows {
            for c in 0..<cols where !taken[r][c] {
                out.append(Tile(rect: rect(r, c, 1), index: 0))
            }
        }
        return out
    }

    /// Fills a fresh grid with distinct albums where the archive allows it.
    private func assignAlbums(_ grid: [Tile]) -> [Tile] {
        var out = grid
        var used = Set<Int>()
        for i in out.indices {
            let idx = pickIndex(avoiding: used)
            used.insert(idx)
            out[i].index = idx
        }
        return out
    }

    private func makeFloater(index: Int, seeded: Bool) -> Floater {
        let depth: CGFloat = settings.driftRandomSize ? CGFloat.random(in: 0...1) : 0.5
        let side: CGFloat
        let base: CGFloat

        if settings.driftRandomSize {
            // Deliberately narrow band: large enough that the far covers are still
            // readable album art, small enough that the near ones don't dominate.
            side = bounds.height * (0.12 + 0.20 * depth)
            // Parallax is what actually sells depth — near covers must visibly
            // outrun far ones, not merely be bigger.
            base = 4 + 26 * depth
        } else {
            side = bounds.height * CGFloat.random(in: 0.16...0.34) * CGFloat(settings.driftScale)
            base = CGFloat.random(in: 6...20)
        }

        let speed = base * CGFloat(settings.driftSpeed)
        let dir = CGFloat.random(in: -0.5...0.5)
        let x = seeded ? CGFloat.random(in: 0...bounds.width) : -side
        return Floater(center: NSPoint(x: x, y: CGFloat.random(in: 0...bounds.height)),
                       velocity: NSPoint(x: speed, y: dir * speed),
                       side: side, index: index,
                       angle: CGFloat.random(in: -0.04...0.04),
                       depth: depth)
    }

    // MARK: - Per-frame updates

    func advance() {
        guard !albums.isEmpty else { return }
        switch activeMode {
        case .mosaic: advanceFlips(interval: settings.tempo)
        case .hero:   advanceFlips(interval: settings.tempo * 1.5); advanceHero()
        case .wall:   advanceWall()
        case .drift:  advanceDrift()
        case .vinyl:    advanceVinyl()
        case .ambient:  advanceFeatured(idleSpan: settings.featureSeconds)
        case .cassette: advanceFeatured(idleSpan: settings.featureSeconds)
        case .gallery:  advanceFeatured(idleSpan: settings.featureSeconds)
                        spotPhase += CGFloat(animationTimeInterval) * 0.09
        case .crate:     advanceCrate()
        case .coverflow: advanceFlow()
        case .jukebox:   advanceFeatured(idleSpan: settings.featureSeconds); advanceBubbles()
        case .starfield: advanceFeatured(idleSpan: settings.featureSeconds); advanceOrbiters()
        case .polaroid:  advancePolaroid()
        case .crt, .cdplayer, .newsstand, .vaporwave, .zoetrope, .subway:
            advanceFeatured(idleSpan: settings.featureSeconds)
        }
    }

    /// Retires finished flips and starts new ones at random.
    ///
    /// This used to fire a whole batch on a metronome, which is why several tiles
    /// turned in perfect unison. Arrivals are now an independent random process:
    /// the same average throughput, but no two flips are scheduled together and
    /// the gaps between them vary.
    private func advanceFlips(interval: TimeInterval) {
        for i in tiles.indices where tiles[i].flipStart >= 0 {
            let t = (phase - tiles[i].flipStart) / max(0.15, tiles[i].flipDur)
            if t >= 0.5, tiles[i].nextIndex >= 0 {
                tiles[i].index = tiles[i].nextIndex
                tiles[i].nextIndex = -1
            }
            if t >= 1 { tiles[i].flipStart = -1 }
        }
        guard !tiles.isEmpty, !albums.isEmpty else { return }

        // Expected flips this frame, spread as independent coin flips so that a
        // high rate produces overlapping-but-offset flips rather than a volley.
        let perSecond = Double(max(1, settings.flipsAtOnce)) / max(0.3, interval)
        var expected = perSecond * animationTimeInterval
        while expected > 0 {
            if Double.random(in: 0...1) < min(1, expected) { startFlip() }
            expected -= 1
        }
    }

    private func startFlip() {
        let target = Int.random(in: 0..<tiles.count)
        guard tiles[target].flipStart < 0 else { return }   // already turning
        let onScreen = Set(tiles.map(\.index))
        tiles[target].flipStart = phase
        tiles[target].nextIndex = pickIndex(avoiding: onScreen)
        // Vary the duration too: identical timing reads as machinery even when
        // the starts are staggered.
        tiles[target].flipDur = max(0.15, settings.flipDuration)
                              * Double.random(in: 0.72...1.38)
    }

    private func advanceHero() {
        if heroFadeStart >= 0, phase - heroFadeStart >= heroFadeDuration { heroFadeStart = -1 }

        // Live playback outranks the rotation: hold the feature on whatever is
        // actually playing, and crossfade when the track changes albums.
        if let live = liveHeroIndex {
            if live != heroIndex {
                heroPrevIndex = heroIndex
                heroIndex = live
                heroFadeStart = phase
            }
            heroSwapAt = phase + max(2, settings.heroInterval)
            return
        }

        guard phase >= heroSwapAt else { return }
        heroSwapAt = phase + max(2, settings.heroInterval)
        heroPrevIndex = heroIndex
        // The hero is the headline, so lean harder on recency than the grid does.
        heroIndex = albums.count > 3 ? Int.random(in: 0..<max(3, albums.count / 8)) : pickIndex()
        heroFadeStart = phase
    }

    func startWallCycle() {
        wallOrder = Array(tiles.indices).shuffled()
        wallCycleStart = phase
        let step = wallStep
        for (order, tileIdx) in wallOrder.enumerated() {
            tiles[tileIdx].appearAt = Double(order) * step
        }
    }

    var wallStep: TimeInterval {
        let base = settings.tempo / Double(max(tiles.count, 1)) * 3
        return max(0.02, base / max(0.15, settings.wallBuildSpeed))
    }
    private var wallFillTime: TimeInterval { Double(tiles.count) * wallStep }
    private var wallHoldTime: TimeInterval { max(1, settings.wallHold) }
    private var wallCycleTime: TimeInterval { wallFillTime + wallHoldTime + wallFillTime * 0.6 }

    private func advanceWall() {
        if phase - wallCycleStart > wallCycleTime {
            tiles = assignAlbums(tiles)
            startWallCycle()
        }
    }

    private func advanceDrift() {
        let dt = CGFloat(animationTimeInterval)
        for i in floaters.indices {
            floaters[i].center.x += floaters[i].velocity.x * dt
            floaters[i].center.y += floaters[i].velocity.y * dt
            let half = floaters[i].side / 2
            if floaters[i].center.x - half > bounds.width {
                let used = Set(floaters.map(\.index))
                floaters[i] = makeFloater(index: pickIndex(avoiding: used), seeded: false)
            }
            if floaters[i].center.y + half < 0 { floaters[i].center.y = bounds.height + half }
            if floaters[i].center.y - half > bounds.height { floaters[i].center.y = -half }
        }
    }

    // MARK: - Drawing

    func drawMosaic() {
        for tile in tiles { drawTile(tile) }
        drawLabel(for: albums.first)
    }

    func drawWall() {
        let t = phase - wallCycleStart
        for tile in tiles {
            let appear = tile.appearAt
            var alpha: CGFloat = 0
            if t >= appear { alpha = min(1, CGFloat((t - appear) / 0.6)) }
            // Dissolve phase: tiles leave in the same order they arrived.
            let dissolveStart = wallFillTime + wallHoldTime
            if t > dissolveStart {
                let out = (t - dissolveStart) / (wallFillTime * 0.6)
                let myOut = CGFloat(out * Double(tiles.count) - (appear / wallStep))
                alpha = min(alpha, max(0, 1 - myOut / 3))
            }
            guard alpha > 0.01 else { continue }
            var scaled = tile
            // Slight zoom-in as each tile lands.
            let grow = 0.94 + 0.06 * min(1, CGFloat((t - appear) / 0.6))
            scaled.rect = tile.rect.insetBy(dx: tile.rect.width * (1 - grow) / 2,
                                            dy: tile.rect.height * (1 - grow) / 2)
            drawTile(scaled, alpha: alpha)
        }
        drawLabel(for: albums.first)
    }

    func drawHero() {
        for tile in tiles { drawTile(tile, alpha: 0.45) }

        NSColor(white: 0, alpha: CGFloat(settings.heroDim)).setFill()
        bounds.fill()

        let frac = CGFloat(min(max(settings.heroSize, 0.2), 0.9))
        let side = min(bounds.height * frac, bounds.width * (frac * 0.75))
        let rect = NSRect(x: bounds.midX - side / 2, y: bounds.midY - side / 2,
                          width: side, height: side)
        var fade: CGFloat = 1
        if heroFadeStart >= 0 {
            fade = Ease.inOut(CGFloat((phase - heroFadeStart) / heroFadeDuration))
            drawImage(image(heroPrevIndex), in: rect, alpha: 1 - fade,
                      radius: side * 0.02, shadow: true)
        }
        drawImage(image(heroIndex), in: rect, alpha: fade, radius: side * 0.02, shadow: true)

        if liveHeroIndex != nil, !nowPlaying.track.isEmpty {
            drawLabel(title: nowPlaying.track,
                      subtitle: "\(nowPlaying.artist) \u{00B7} \(nowPlaying.albumName)",
                      badge: "NOW PLAYING")
        } else if albums.indices.contains(heroIndex) {
            drawLabel(for: albums[heroIndex])
        }
    }

    func drawDrift() {
        // Painter's algorithm: far covers first, so near ones pass in front.
        for f in floaters.sorted(by: { $0.depth < $1.depth }) {
            let rect = NSRect(x: f.center.x - f.side / 2, y: f.center.y - f.side / 2,
                              width: f.side, height: f.side)
            // Fade in at the left edge and out at the right.
            let edge = f.side
            var alpha: CGFloat = 1
            if f.center.x < edge { alpha = max(0, f.center.x / edge) }
            if f.center.x > bounds.width - edge {
                alpha = max(0, (bounds.width - f.center.x) / edge)
            }
            // Atmospheric perspective: distant covers sit back into the dark.
            if settings.driftRandomSize { alpha *= 0.55 + 0.45 * f.depth }
            guard alpha > 0.01 else { continue }
            let ctx = NSGraphicsContext.current!.cgContext
            ctx.saveGState()
            ctx.translateBy(x: f.center.x, y: f.center.y)
            ctx.rotate(by: f.angle)
            ctx.translateBy(x: -f.center.x, y: -f.center.y)
            drawImage(image(f.index), in: rect, alpha: alpha * 0.92,
                      radius: f.side * 0.03, shadow: true)
            ctx.restoreGState()
        }
        drawLabel(for: albums.first)
    }

    // MARK: drawing helpers

    private func drawTile(_ tile: Tile, alpha: CGFloat = 1) {
        var sx: CGFloat = 1
        if tile.flipStart >= 0 {
            let t = (phase - tile.flipStart) / max(0.15, tile.flipDur)
            sx = max(0.02, abs(cos(CGFloat(t) * .pi)))
        }
        let ctx = NSGraphicsContext.current!.cgContext
        ctx.saveGState()
        if sx < 1 {
            ctx.translateBy(x: tile.rect.midX, y: tile.rect.midY)
            ctx.scaleBy(x: sx, y: 1)
            ctx.translateBy(x: -tile.rect.midX, y: -tile.rect.midY)
        }
        // Hairline inset keeps neighbouring covers from visually bleeding together.
        drawImage(image(tile.index), in: tile.rect.insetBy(dx: 0.5, dy: 0.5),
                  alpha: alpha, radius: 0, shadow: false)
        ctx.restoreGState()
    }

    private func drawImage(_ img: NSImage?, in rect: NSRect, alpha: CGFloat,
                           radius: CGFloat, shadow: Bool) {
        guard let img, alpha > 0.01 else { return }
        if shadow {
            NSGraphicsContext.saveGraphicsState()
            let s = NSShadow()
            s.shadowColor = NSColor(white: 0, alpha: 0.55 * alpha)
            s.shadowBlurRadius = rect.width * 0.06
            s.shadowOffset = NSSize(width: 0, height: -rect.width * 0.02)
            s.set()
            NSColor(white: 0, alpha: alpha).setFill()
            NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius).fill()
            NSGraphicsContext.restoreGraphicsState()
        }

        NSGraphicsContext.saveGraphicsState()
        NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius).addClip()
        // Aspect-fill: scale the (square) cover to cover the cell and centre it,
        // letting the clip crop the overhang. Prevents stretching when the cell
        // isn't perfectly square.
        let cover = max(rect.width, rect.height)
        let square = NSRect(x: rect.midX - cover / 2, y: rect.midY - cover / 2,
                            width: cover, height: cover)
        img.draw(in: square, from: .zero, operation: .sourceOver, fraction: alpha)
        NSGraphicsContext.restoreGraphicsState()
    }

    private func drawLabel(for album: AlbumEntry?) {
        guard let album else { return }
        drawLabel(title: album.name, subtitle: album.artist, badge: nil)
    }

    private func drawLabel(title: String, subtitle: String, badge: String?) {
        guard settings.showTrackLabel, !isPreview else { return }
        let shadow = NSShadow()
        shadow.shadowColor = NSColor(white: 0, alpha: 0.9)
        shadow.shadowBlurRadius = 6
        shadow.shadowOffset = NSSize(width: 0, height: -1)

        let titleAttrs: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: 22, weight: .semibold),
            .foregroundColor: NSColor.white, .shadow: shadow]
        let subAttrs: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: 17, weight: .regular),
            .foregroundColor: NSColor(white: 0.82, alpha: 1), .shadow: shadow]

        if let badge {
            let badgeAttrs: [NSAttributedString.Key: Any] = [
                .font: NSFont.systemFont(ofSize: 11, weight: .bold),
                .foregroundColor: NSColor(red: 0.11, green: 0.84, blue: 0.38, alpha: 1),
                .kern: 1.6, .shadow: shadow]
            (badge as NSString).draw(at: NSPoint(x: 44, y: 96), withAttributes: badgeAttrs)
        }
        (subtitle as NSString).draw(at: NSPoint(x: 44, y: 44), withAttributes: subAttrs)
        (title as NSString).draw(at: NSPoint(x: 44, y: 68), withAttributes: titleAttrs)
    }
}

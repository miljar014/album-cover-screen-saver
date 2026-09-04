import ScreenSaver
import AppKit

/// A record that has accumulated on the table. Position is normalised to the
/// screen so it survives a resolution change, and identity is the album id
/// rather than an index into `albums`, which reorders as the archive grows.
struct TableSleeve {
    var albumID: String
    var nx: CGFloat
    var ny: CGFloat
    var sizeFrac: CGFloat
    var angle: CGFloat
    var discPeek: CGFloat
    var discDirection: CGFloat
    var addedAt: TimeInterval
}


extension AlbumCoverSaverView {

    /// Clears a persistent board when a different style takes it over. Deliberately
    /// NOT called on an archive refresh — that is what used to wipe the pile every
    /// time a new song was recorded.
    func claimTable(for mode: CollageMode) {
        if tableOwner != mode {
            table.removeAll()
            tableOwner = mode
        }
    }

    /// A free, normalised spot on the board. Normalised so a resolution change
    /// carries the pile with it rather than re-scattering it.
    func findTableSpot(sizeRange: ClosedRange<CGFloat>, scale: CGFloat,
                       keepOutCentre: NSPoint, keepOutRadius: CGFloat,
                       keepOutRect: NSRect?) -> (x: CGFloat, y: CGFloat, size: CGFloat)? {
        let W = bounds.width, H = bounds.height
        guard W > 10, H > 10 else { return nil }

        for attempt in 0..<900 {
            let sizeFrac = CGFloat.random(in: sizeRange) * scale
            let side = H * sizeFrac
            let half = side * 0.75
            guard W - half > half, H - half > half else { return nil }
            let c = NSPoint(x: CGFloat.random(in: half...(W - half)),
                            y: CGFloat.random(in: half...(H - half)))
            if keepOutRadius > 0,
               hypot(c.x - keepOutCentre.x, c.y - keepOutCentre.y) < keepOutRadius + half {
                continue
            }
            if let r = keepOutRect, r.insetBy(dx: -half * 0.4, dy: -half * 0.4).contains(c) {
                continue
            }
            // Later attempts accept more overlap rather than dropping the item.
            let slack: CGFloat = attempt > 600 ? 0.30 : 0.42
            if table.contains(where: {
                hypot($0.nx * W - c.x, $0.ny * H - c.y) < ($0.sizeFrac * H + side) * slack
            }) { continue }
            return (c.x / W, c.y / H, sizeFrac)
        }

        // Nothing clear enough. Rather than silently dropping the item — which
        // looked like songs simply not appearing — take the emptiest spot going,
        // at the small end of the size range so it fits more easily.
        let sizeFrac = sizeRange.lowerBound * scale
        let side = H * sizeFrac
        let half = side * 0.75
        guard W - half > half, H - half > half else { return nil }
        var best: (gap: CGFloat, x: CGFloat, y: CGFloat)?
        for _ in 0..<300 {
            let c = NSPoint(x: CGFloat.random(in: half...(W - half)),
                            y: CGFloat.random(in: half...(H - half)))
            // The centre keep-out still holds; overlap with neighbours does not.
            if keepOutRadius > 0,
               hypot(c.x - keepOutCentre.x, c.y - keepOutCentre.y) < keepOutRadius * 0.92 {
                continue
            }
            let gap = table.map { hypot($0.nx * W - c.x, $0.ny * H - c.y) }
                           .min() ?? .greatestFiniteMagnitude
            if best == nil || gap > best!.gap { best = (gap, c.x, c.y) }
        }
        guard let best else { return nil }
        return (best.x / W, best.y / H, sizeFrac)
    }

    /// Pins an album to the board if it isn't already there.
    @discardableResult
    func pinToTable(_ albumID: String,
                    spot: (x: CGFloat, y: CGFloat, size: CGFloat)?) -> Bool {
        guard !table.contains(where: { $0.albumID == albumID }), let spot else { return false }
        table.append(TableSleeve(albumID: albumID, nx: spot.x, ny: spot.y,
                                 sizeFrac: spot.size,
                                 angle: CGFloat.random(in: -0.24...0.24),
                                 discPeek: Double.random(in: 0...1) < 0.45
                                     ? CGFloat.random(in: 0.20...0.44) : 0,
                                 discDirection: CGFloat.random(in: -0.7...0.7),
                                 addedAt: phase))
        return true
    }

    /// Shared scatter used by the styles that strew covers around a centrepiece.
    /// Rejection sampling: keeps clear of an optional exclusion circle and avoids
    /// piling items on top of each other. Built once per layout so nothing
    /// shimmers between frames.
    func buildScatter(count: Int, sizeRange: ClosedRange<CGFloat>,
                      keepOutCentre: NSPoint, keepOutRadius: CGFloat) {
        sleeves.removeAll()
        let want = isPreview ? max(3, count / 3) : count
        guard want > 0, !albums.isEmpty, bounds.width > 10 else { return }
        let W = bounds.width, H = bounds.height

        var used = Set<Int>()
        var tries = 0
        while sleeves.count < want && tries < 2500 {
            tries += 1
            let side = H * CGFloat.random(in: sizeRange)
            let half = side * 0.78
            guard W - half > half, H - half > half else { break }
            let c = NSPoint(x: CGFloat.random(in: half...(W - half)),
                            y: CGFloat.random(in: half...(H - half)))
            if keepOutRadius > 0,
               hypot(c.x - keepOutCentre.x, c.y - keepOutCentre.y) < keepOutRadius + half * 0.7 {
                continue
            }
            if sleeves.contains(where: {
                hypot($0.centre.x - c.x, $0.centre.y - c.y) < ($0.side + side) * 0.44
            }) { continue }
            let i = pickIndex(avoiding: used); used.insert(i)
            sleeves.append(Sleeve(centre: c, side: side,
                                  angle: CGFloat.random(in: -0.20...0.20),
                                  index: i, discPeek: 0,
                                  discDirection: CGFloat.random(in: -0.7...0.7)))
        }
    }
}

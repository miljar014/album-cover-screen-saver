import ScreenSaver
import AppKit

extension AlbumCoverSaverView {

    /// Thumbing through a record crate: a fanned stack of sleeves sliding past,
    /// with the front one pulled forward.
    func buildCrate() {
        crateItems.removeAll()
        guard !albums.isEmpty else { return }
        let want = max(4, min(40, settings.crateCount)) + 2
        var used = Set<Int>()
        crateItems = (0..<want).map { i in
            if i == 0, let live = liveAlbumIndex { used.insert(live); return live }
            let idx = pickIndex(avoiding: used)
            used.insert(idx)
            return idx
        }
        featIndex = crateItems.first ?? 0
        featPrevIndex = featIndex
    }

    func advanceCrate() {
        guard !crateItems.isEmpty else { buildCrate(); return }
        let spacing = crateSpacing

        // Live playback jumps straight to the record being played rather than
        // waiting for it to come round in the stack.
        if let live = liveAlbumIndex, crateItems.first != live {
            crateItems.insert(live, at: 0)
            crateOffset = spacing
            if crateItems.count > max(6, settings.crateCount + 2) { crateItems.removeLast() }
        }

        let dwell = max(3, settings.featureSeconds / 6)
        crateOffset += CGFloat(animationTimeInterval) * spacing / CGFloat(dwell)
        if crateOffset >= spacing {
            crateOffset -= spacing
            if liveAlbumIndex == nil {
                crateItems.removeFirst()
                crateItems.append(pickIndex(avoiding: Set(crateItems)))
            }
        }
        featIndex = crateItems.first ?? 0
        featStartedAt = phase
    }

    private var crateSpacing: CGFloat { min(bounds.height * 0.44, bounds.width * 0.30) * 0.15 }

    func drawCrate() {
        let W = bounds.width, H = bounds.height
        let pal = featPalette
        guard !crateItems.isEmpty else { return }

        NSGradient(starting: pal.deep.withBrightness(0.15),
                   ending: pal.deep.withBrightness(0.04))?
            .draw(in: NSBezierPath(rect: bounds), angle: -90)

        let side = min(H * 0.46, W * 0.30)
        let spacing = crateSpacing
        let baseY = H * 0.34
        let frontX = W * 0.13

        // Back to front, so each sleeve overlaps the one behind it.
        for i in stride(from: crateItems.count - 1, through: 0, by: -1) {
            let idx = crateItems[i]
            let t = CGFloat(i)
            let x = frontX + t * spacing - crateOffset
            guard x < W * 1.05 else { continue }

            // Sleeves lean back and shrink with depth; the front one stands up.
            let depth = min(1, t / 12)
            let scale = 1 - depth * 0.10
            let s = side * scale
            let y = baseY + depth * H * 0.03
            let rect = NSRect(x: x, y: y, width: s, height: s)

            let ctx = NSGraphicsContext.current!.cgContext
            ctx.saveGState()
            ctx.translateBy(x: rect.midX, y: rect.midY)
            ctx.rotate(by: -0.055 - depth * 0.03)
            ctx.translateBy(x: -rect.midX, y: -rect.midY)

            drawCover(idx, in: rect, alpha: 1, radius: 2, shadowAlpha: 0.55)
            // Depth haze, plus a lit edge where the next sleeve would catch light.
            pal.deep.withAlphaComponent(0.20 + depth * 0.45).setFill()
            NSBezierPath(rect: rect).fill()
            pal.accent.withAlphaComponent(0.20).setStroke()
            let edge = NSBezierPath()
            edge.move(to: NSPoint(x: rect.minX, y: rect.minY))
            edge.line(to: NSPoint(x: rect.minX, y: rect.maxY))
            edge.lineWidth = 2
            edge.stroke()
            ctx.restoreGState()
        }

        // Crate front, hiding where the sleeves meet the floor.
        let crate = NSRect(x: 0, y: 0, width: W, height: baseY + side * 0.30)
        NSGradient(starting: pal.consoleLight, ending: pal.console.withBrightness(0.14))?
            .draw(in: NSBezierPath(rect: crate), angle: -90)
        pal.accent.withAlphaComponent(0.45).setFill()
        NSBezierPath(rect: NSRect(x: 0, y: crate.maxY - 3, width: W, height: 3)).fill()

        guard settings.showTrackLabel else { return }
        let text = featText
        let colW = W * 0.42
        let x = W * 0.54
        let shadow = softShadow(0.75, blur: 16)
        var top = H * 0.78

        drawText(liveAlbumIndex == featIndex ? "NOW PLAYING" : "IN THE CRATE",
                 x: x, top: top, width: colW, font: retroFont(max(10, H * 0.017), bold: true),
                 color: pal.accent, kern: 3, shadow: shadow)
        top -= H * 0.048

        let titleFont = fittedFont(text.title, width: colW, maxHeight: H * 0.16,
                                   base: max(20, H * 0.048), bold: true)
        top -= drawText(text.title, x: x, top: top, width: colW, font: titleFont,
                        color: pal.text, shadow: shadow) + H * 0.014
        top -= drawText(text.artist, x: x, top: top, width: colW,
                        font: retroFont(max(13, H * 0.028)),
                        color: pal.accent, shadow: shadow)
        if !text.sub.isEmpty {
            top -= H * 0.010
            drawText(text.sub, x: x, top: top, width: colW,
                     font: retroFont(max(11, H * 0.021)),
                     color: pal.textMuted, shadow: shadow)
        }
    }
}

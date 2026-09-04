import ScreenSaver
import AppKit

extension AlbumCoverSaverView {

    func buildFlow() {
        flowItems.removeAll()
        guard !albums.isEmpty else { return }
        let want = max(5, min(60, settings.flowCount))
        var used = Set<Int>()
        flowItems = (0..<want).map { _ in
            let i = pickIndex(avoiding: used); used.insert(i); return i
        }
        if let live = liveAlbumIndex { flowItems[0] = live }
        flowPos = 0
        featIndex = flowItems[0]
        featStartedAt = phase
    }

    func advanceFlow() {
        guard !flowItems.isEmpty else { buildFlow(); return }
        let dwell = max(2.5, settings.featureSeconds / 8)

        // Live playback: drop the record being played into the next slot so the
        // carousel arrives at it rather than ignoring it.
        if let live = liveAlbumIndex, flowItems[centreSlot] != live {
            let next = (centreSlot + 1) % flowItems.count
            flowItems[next] = live
        }
        flowPos += CGFloat(animationTimeInterval) / CGFloat(dwell)
        if flowPos >= CGFloat(flowItems.count) { flowPos -= CGFloat(flowItems.count) }
        featIndex = flowItems[centreSlot]
    }

    private var centreSlot: Int {
        guard !flowItems.isEmpty else { return 0 }
        return Int(flowPos.rounded()) % flowItems.count
    }

    /// A 3D carousel faked in 2D: covers squash horizontally and shear vertically
    /// with distance from centre, which reads as rotation without needing SceneKit.
    func drawCoverFlow() {
        let W = bounds.width, H = bounds.height
        let pal = featPalette
        guard !flowItems.isEmpty else { return }

        NSGradient(starting: pal.console.withBrightness(0.22),
                   ending: pal.deep.withBrightness(0.04))?
            .draw(in: NSBezierPath(rect: bounds), angle: -90)

        let side = min(H * 0.40, W * 0.28)
        let cy = H * 0.56
        let gap = side * 0.30
        let ctx = NSGraphicsContext.current!.cgContext

        // Farthest first so nearer covers overlap them.
        let visible = 5
        var order: [(off: CGFloat, idx: Int)] = []
        for d in -visible...visible {
            let slot = ((centreSlot + d) % flowItems.count + flowItems.count) % flowItems.count
            let off = CGFloat(d) - (flowPos - flowPos.rounded())
            order.append((off, flowItems[slot]))
        }
        order.sort { abs($0.off) > abs($1.off) }

        for (off, idx) in order {
            let a = min(abs(off), CGFloat(visible))
            let squash = max(0.16, cos(min(1.25, a * 0.52)))
            let scale = 1 / (1 + a * 0.17)
            let s = side * scale
            let dir: CGFloat = off < 0 ? -1 : 1
            let x = W * 0.5 + dir * (a == 0 ? 0 : (side * 0.34 + (a - 1) * gap) * (a < 1 ? a : 1))
                    + off * (a < 1 ? side * 0.34 : 0)
            let alpha = max(0, 1 - a * 0.16)

            ctx.saveGState()
            ctx.translateBy(x: x, y: cy)
            ctx.scaleBy(x: squash, y: 1)
            // Shear tilts the vertical edges — the cue the eye reads as turning.
            ctx.concatenate(CGAffineTransform(a: 1, b: -dir * 0.10 * a / CGFloat(visible),
                                              c: 0, d: 1, tx: 0, ty: 0))

            let rect = NSRect(x: -s / 2, y: -s / 2, width: s, height: s)
            drawCover(idx, in: rect, alpha: alpha, radius: 3, shadowAlpha: 0.6)
            if a > 0 {
                pal.deep.withAlphaComponent(min(0.55, a * 0.13)).setFill()
                NSBezierPath(rect: rect).fill()
            }

            // Reflection.
            let refl = NSRect(x: -s / 2, y: -s / 2 - s * 0.52 - 3, width: s, height: s * 0.52)
            NSGraphicsContext.saveGraphicsState()
            NSBezierPath(rect: refl).addClip()
            ctx.saveGState()
            ctx.translateBy(x: 0, y: refl.maxY + refl.minY)
            ctx.scaleBy(x: 1, y: -1)
            drawCover(idx, in: NSRect(x: -s / 2, y: refl.minY, width: s, height: s),
                      alpha: alpha * 0.28, radius: 3)
            ctx.restoreGState()
            NSGraphicsContext.restoreGraphicsState()
            NSGradient(colors: [pal.deep.withAlphaComponent(0.15),
                                pal.deep.withAlphaComponent(1)])?
                .draw(in: NSBezierPath(rect: refl), angle: -90)

            ctx.restoreGState()
        }

        guard settings.showTrackLabel else { return }
        let text = featText
        let colW = W * 0.62
        let x = W * 0.5 - colW / 2
        let shadow = softShadow(0.75, blur: 14)
        var top = cy - side * 0.5 - side * 0.56 - H * 0.03
        let titleFont = fittedFont(text.title, width: colW, maxHeight: H * 0.10,
                                   base: max(18, H * 0.040), bold: true)
        top -= drawText(text.title, x: x, top: top, width: colW, font: titleFont,
                        color: pal.text, shadow: shadow, centred: true) + H * 0.012
        drawText(text.artist, x: x, top: top, width: colW,
                 font: retroFont(max(12, H * 0.025)),
                 color: pal.accent, shadow: shadow, centred: true)
    }
}

import ScreenSaver
import AppKit

extension AlbumCoverSaverView {

    /// Covers ranged around the inside of a spinning slitted drum.
    func drawZoetrope() {
        let W = bounds.width, H = bounds.height
        let pal = featPalette
        let m = min(W, H)

        NSGradient(colors: [pal.console.withBrightness(0.20), NSColor.black])?
            .draw(fromCenter: NSPoint(x: W * 0.5, y: H * 0.62), radius: 0,
                  toCenter: NSPoint(x: W * 0.5, y: H * 0.62), radius: m, options: [])

        let cx = W * 0.5, cy = H * 0.56
        let R = min(m * 0.36, W * 0.34)
        let squash: CGFloat = 0.30           // the drum seen from slightly above
        let cardH = R * 0.62
        let count = max(6, min(30, settings.zoetropeCount))
        let spin = -CGFloat(phase) * 0.45

        // Drum floor.
        NSGradient(starting: pal.console.withBrightness(0.30),
                   ending: pal.console.withBrightness(0.10))?
            .draw(in: NSBezierPath(ovalIn: NSRect(x: cx - R, y: cy - R * squash - cardH * 0.55,
                                                  width: R * 2, height: R * 2 * squash)),
                  angle: -90)

        guard !albums.isEmpty else { return }

        // Cards around the inner wall, far ones first.
        var items: [(depth: CGFloat, i: Int, x: CGFloat, y: CGFloat, w: CGFloat)] = []
        for k in 0..<count {
            let a = spin + CGFloat(k) * 2 * .pi / CGFloat(count)
            let idx = albums.indices.contains(k) ? k : (k % albums.count)
            let x = cx + cos(a) * R
            let y = cy + sin(a) * R * squash
            let depth = (sin(a) + 1) / 2                   // 1 = back of the drum
            items.append((depth, idx, x, y, cardH * (1.15 - depth * 0.42)))
        }
        for it in items.sorted(by: { $0.depth > $1.depth }) {
            let s = it.w
            let rect = NSRect(x: it.x - s / 2, y: it.y, width: s, height: s)
            drawCover(it.i, in: rect, alpha: 1, radius: 1, shadowAlpha: 0.4)
            NSColor(white: 0, alpha: it.depth * 0.60).setFill()
            NSBezierPath(rect: rect).fill()
        }

        // Slits: the front wall of the drum, rotating with it.
        for k in 0..<count {
            let a = spin + (CGFloat(k) + 0.5) * 2 * .pi / CGFloat(count)
            guard sin(a) < 0.1 else { continue }           // only the near wall
            let x = cx + cos(a) * R * 1.02
            let y = cy + sin(a) * R * squash
            let wdt = max(2, R * 0.055 * abs(cos(a)))
            NSColor(red: 0.09, green: 0.07, blue: 0.06, alpha: 0.96).setFill()
            NSBezierPath(rect: NSRect(x: x - wdt / 2, y: y - cardH * 0.16,
                                      width: wdt, height: cardH * 1.28)).fill()
        }

        // Rim.
        let rim = NSBezierPath(ovalIn: NSRect(x: cx - R * 1.03,
                                              y: cy - R * squash * 1.03 + cardH * 1.06,
                                              width: R * 2.06, height: R * 2.06 * squash))
        rim.lineWidth = max(2, R * 0.020)
        pal.accent.withAlphaComponent(0.7).setStroke()
        rim.stroke()

        guard settings.showTrackLabel, albums.indices.contains(featIndex) else { return }
        let text = featText
        let colW = W * 0.7
        let x = W * 0.5 - colW / 2
        let shadow = softShadow(0.8, blur: 16)
        var top = cy - R * squash - cardH * 0.7
        let titleFont = fittedFont(text.title, width: colW, maxHeight: H * 0.10,
                                   base: max(18, H * 0.042), bold: true)
        top -= drawText(text.title, x: x, top: top, width: colW, font: titleFont,
                        color: pal.text, shadow: shadow, centred: true) + H * 0.012
        drawText(text.artist, x: x, top: top, width: colW,
                 font: retroFont(max(12, H * 0.025)), color: pal.accent,
                 shadow: shadow, centred: true)
    }
}

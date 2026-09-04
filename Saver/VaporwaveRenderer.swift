import ScreenSaver
import AppKit

extension AlbumCoverSaverView {

    /// Neon horizon, chrome type, endless grid.
    func drawVaporwave() {
        let W = bounds.width, H = bounds.height
        let pal = featPalette
        let horizon = H * 0.46

        let magenta = NSColor(red: 0.98, green: 0.20, blue: 0.62, alpha: 1)
        let cyan = NSColor(red: 0.25, green: 0.94, blue: 0.98, alpha: 1)
        let violet = NSColor(red: 0.24, green: 0.08, blue: 0.36, alpha: 1)

        // Sky.
        NSGradient(colors: [NSColor(red: 0.05, green: 0.02, blue: 0.14, alpha: 1),
                            violet, magenta.withAlphaComponent(0.75)])?
            .draw(in: NSBezierPath(rect: NSRect(x: 0, y: horizon,
                                                width: W, height: H - horizon)), angle: -90)

        // Sun, banded the way every one of these is.
        let sunR = min(W, H) * 0.20
        let sunC = NSPoint(x: W * 0.5, y: horizon + sunR * 0.62)
        NSGraphicsContext.saveGraphicsState()
        NSBezierPath(ovalIn: NSRect(x: sunC.x - sunR, y: sunC.y - sunR,
                                    width: sunR * 2, height: sunR * 2)).addClip()
        NSGradient(colors: [NSColor(red: 1, green: 0.90, blue: 0.35, alpha: 1), magenta])?
            .draw(in: NSBezierPath(rect: NSRect(x: sunC.x - sunR, y: sunC.y - sunR,
                                                width: sunR * 2, height: sunR * 2)), angle: -90)
        // Slots widen toward the bottom.
        var sy = sunC.y - sunR * 0.10
        var band: CGFloat = 2
        while sy > sunC.y - sunR {
            NSColor(red: 0.10, green: 0.03, blue: 0.20, alpha: 0.9).setFill()
            NSBezierPath(rect: NSRect(x: sunC.x - sunR, y: sy, width: sunR * 2, height: band)).fill()
            sy -= band * 2.4
            band *= 1.25
        }
        NSGraphicsContext.restoreGraphicsState()

        // Ground and perspective grid.
        NSGradient(colors: [NSColor(red: 0.06, green: 0.01, blue: 0.12, alpha: 1),
                            NSColor(red: 0.16, green: 0.02, blue: 0.26, alpha: 1)])?
            .draw(in: NSBezierPath(rect: NSRect(x: 0, y: 0, width: W, height: horizon)),
                  angle: -90)

        cyan.withAlphaComponent(0.75).setStroke()
        for i in -14...14 {
            let g = NSBezierPath()
            g.move(to: NSPoint(x: W * 0.5 + CGFloat(i) * W * 0.02, y: horizon))
            g.line(to: NSPoint(x: W * 0.5 + CGFloat(i) * W * 0.30, y: 0))
            g.lineWidth = 1.4
            g.stroke()
        }
        // Horizontal lines accelerate toward the viewer, so the ground moves.
        let z = CGFloat(phase).truncatingRemainder(dividingBy: 1)
        var k: CGFloat = 0
        while k < 22 {
            let t = (k + z) / 22
            let y = horizon * (1 - t * t)
            let a = max(0, 0.75 - t * 0.6)
            cyan.withAlphaComponent(a).setStroke()
            let g = NSBezierPath()
            g.move(to: NSPoint(x: 0, y: y)); g.line(to: NSPoint(x: W, y: y))
            g.lineWidth = 1.4
            g.stroke()
            k += 1
        }
        magenta.withAlphaComponent(0.9).setFill()
        NSBezierPath(rect: NSRect(x: 0, y: horizon - 1.5, width: W, height: 3)).fill()

        guard albums.indices.contains(featIndex) else { return }

        // Cover floating over the horizon with a neon frame.
        let side = min(H * 0.34, W * 0.26)
        let bob = CGFloat(sin(Double(phase) * 0.7)) * H * 0.008
        let rect = NSRect(x: W * 0.5 - side / 2, y: horizon + H * 0.10 + bob,
                          width: side, height: side)
        NSGradient(colors: [cyan.withAlphaComponent(0.5), cyan.withAlphaComponent(0)])?
            .draw(fromCenter: NSPoint(x: rect.midX, y: rect.midY), radius: side * 0.5,
                  toCenter: NSPoint(x: rect.midX, y: rect.midY), radius: side * 1.15,
                  options: [])
        drawFeaturedCover(in: rect, radius: 2, shadowAlpha: 0.7)
        let fr = NSBezierPath(rect: rect)
        fr.lineWidth = 3
        magenta.setStroke(); fr.stroke()
        fr.lineWidth = 1
        cyan.setStroke(); fr.stroke()

        guard settings.showTrackLabel else { return }
        let text = featText
        let colW = W * 0.8
        let x = W * 0.5 - colW / 2
        let font = fittedFont(text.title.uppercased(), width: colW, maxHeight: H * 0.11,
                              base: max(20, H * 0.052), bold: true)
        // Chromatic fringing: the same word in cyan and magenta, offset.
        let off = max(2, H * 0.004)
        drawText(text.title.uppercased(), x: x - off, top: rect.minY - H * 0.03,
                 width: colW, font: font, color: cyan.withAlphaComponent(0.9),
                 kern: 4, centred: true)
        drawText(text.title.uppercased(), x: x + off, top: rect.minY - H * 0.03,
                 width: colW, font: font, color: magenta.withAlphaComponent(0.9),
                 kern: 4, centred: true)
        let h = drawText(text.title.uppercased(), x: x, top: rect.minY - H * 0.03,
                         width: colW, font: font, color: .white, kern: 4, centred: true)
        drawText(text.artist.uppercased(), x: x, top: rect.minY - H * 0.03 - h - H * 0.014,
                 width: colW, font: retroFont(max(12, H * 0.024)),
                 color: pal.accent, kern: 6, centred: true)
    }
}

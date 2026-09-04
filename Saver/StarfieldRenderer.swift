import ScreenSaver
import AppKit

/// One album orbiting the current record.
struct Orbiter {
    var index: Int
    var radius: CGFloat     // fraction of the smaller screen dimension
    var angle: CGFloat
    var speed: CGFloat
    var size: CGFloat
}

extension AlbumCoverSaverView {

    func buildOrbiters() {
        orbiters.removeAll()
        guard !albums.isEmpty else { return }
        let want = isPreview ? 5 : max(3, min(40, settings.orbitCount))
        var used = Set<Int>()
        orbiters = (0..<want).map { _ in
            let i = pickIndex(avoiding: used); used.insert(i)
            let r = CGFloat.random(in: 0.20...0.52)
            return Orbiter(index: i, radius: r,
                           angle: CGFloat.random(in: 0...(2 * .pi)),
                           // Inner orbits are faster, as they are in the sky.
                           speed: (0.30 / (r + 0.18)) * (Bool.random() ? 1 : 0.85),
                           size: CGFloat.random(in: 0.075...0.135))
        }
    }

    func advanceOrbiters() {
        let k = CGFloat(animationTimeInterval) * CGFloat(max(0.05, settings.orbitSpeed))
        for i in orbiters.indices { orbiters[i].angle += orbiters[i].speed * k }
    }

    func drawStarfield() {
        let W = bounds.width, H = bounds.height
        let pal = featPalette
        let m = min(W, H)

        NSGradient(colors: [pal.deep.withBrightness(0.13), NSColor.black])?
            .draw(fromCenter: NSPoint(x: W * 0.5, y: H * 0.5), radius: 0,
                  toCenter: NSPoint(x: W * 0.5, y: H * 0.5), radius: m * 0.95, options: [])

        // Stars, deterministic so they don't crawl, drifting slowly for parallax.
        for i in 0..<220 {
            let f = fractS(sin(Double(i) * 12.9898) * 43758.5453)
            let g = fractS(sin(Double(i) * 78.233) * 12345.6789)
            let depth = CGFloat(fractS(sin(Double(i) * 39.77) * 5647.31))
            let drift = CGFloat(phase) * (2 + depth * 10)
            let x = (CGFloat(f) * W + drift).truncatingRemainder(dividingBy: W)
            let y = CGFloat(g) * H
            let r = 0.5 + depth * 1.6
            NSColor(white: 1, alpha: 0.15 + depth * 0.55).setFill()
            NSBezierPath(ovalIn: NSRect(x: x, y: y, width: r, height: r)).fill()
        }

        guard albums.indices.contains(featIndex) else { return }
        let centre = NSPoint(x: W * 0.5, y: H * 0.54)
        let sunSide = min(H * 0.30, W * 0.22)

        // Behind first, then the sun, then in front — sin(angle) is the depth cue.
        let behind = orbiters.filter { sin($0.angle) >= 0 }
        let front  = orbiters.filter { sin($0.angle) < 0 }
        for o in behind { drawOrbiter(o, centre: centre, m: m, pal: pal) }

        // Glow around the featured album.
        NSGradient(colors: [pal.accent.withAlphaComponent(0.42),
                            pal.accent.withAlphaComponent(0)])?
            .draw(fromCenter: centre, radius: sunSide * 0.45,
                  toCenter: centre, radius: sunSide * 1.5, options: [])

        let rect = NSRect(x: centre.x - sunSide / 2, y: centre.y - sunSide / 2,
                          width: sunSide, height: sunSide)
        drawFeaturedCover(in: rect, radius: 6, shadowAlpha: 0.5)

        for o in front { drawOrbiter(o, centre: centre, m: m, pal: pal) }

        guard settings.showTrackLabel else { return }
        let text = featText
        let colW = W * 0.6
        let x = W * 0.5 - colW / 2
        let shadow = softShadow(0.85, blur: 18)
        var top = centre.y - sunSide * 0.5 - H * 0.045
        let titleFont = fittedFont(text.title, width: colW, maxHeight: H * 0.11,
                                   base: max(18, H * 0.042), bold: true)
        top -= drawText(text.title, x: x, top: top, width: colW, font: titleFont,
                        color: pal.text, shadow: shadow, centred: true) + H * 0.012
        drawText(text.artist, x: x, top: top, width: colW,
                 font: retroFont(max(12, H * 0.026)),
                 color: pal.accent, shadow: shadow, centred: true)
    }

    private func drawOrbiter(_ o: Orbiter, centre: NSPoint, m: CGFloat, pal: ArtPalette) {
        // Flattened orbit: a circle seen at a shallow angle reads as a plane.
        let x = centre.x + cos(o.angle) * m * o.radius
        let y = centre.y + sin(o.angle) * m * o.radius * 0.34
        let depth = (sin(o.angle) + 1) / 2                 // 1 = far, 0 = near
        let s = m * o.size * (1.25 - depth * 0.5)
        let rect = NSRect(x: x - s / 2, y: y - s / 2, width: s, height: s)
        drawCover(o.index, in: rect, alpha: 1, radius: 2, shadowAlpha: 0.5)
        NSColor(white: 0, alpha: depth * 0.45).setFill()
        NSBezierPath(rect: rect).fill()
    }

    private func fractS(_ x: Double) -> Double { x - x.rounded(.down) }
}

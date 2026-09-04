import ScreenSaver
import AppKit

struct Bubble {
    var tube: Int          // 0 = left, 1 = right
    var y: CGFloat         // 0...1 up the tube
    var x: CGFloat         // -1...1 across it
    var r: CGFloat
    var speed: CGFloat
    var tint: Int          // which of the cover's colours this bubble carries
}

extension AlbumCoverSaverView {

    func buildBubbles() {
        bubbles = (0..<44).map { _ in
            Bubble(tube: Bool.random() ? 0 : 1,
                   y: CGFloat.random(in: 0...1),
                   x: CGFloat.random(in: -0.6...0.6),
                   r: CGFloat.random(in: 0.18...0.5),
                   speed: CGFloat.random(in: 0.05...0.16),
                   tint: Int.random(in: 0..<6))
        }
    }

    func advanceBubbles() {
        let dt = CGFloat(animationTimeInterval)
        for i in bubbles.indices {
            bubbles[i].y += bubbles[i].speed * dt
            // Wobble as they rise, like real jukebox tubes.
            bubbles[i].x += sin(CGFloat(phase) * 2 + CGFloat(i)) * dt * 0.10
            if bubbles[i].y > 1 {
                bubbles[i].y = 0
                bubbles[i].x = CGFloat.random(in: -0.6...0.6)
                bubbles[i].r = CGFloat.random(in: 0.18...0.5)
                bubbles[i].tint = Int.random(in: 0..<6)
            }
        }
    }

    /// Multiple passes of decreasing width build a glow around a hot core — the
    /// cheapest convincing neon in 2D.
    private func neon(_ path: NSBezierPath, colour: NSColor, width: CGFloat, pulse: CGFloat) {
        for (w, a) in [(width * 5.5, 0.05), (width * 3.2, 0.10),
                       (width * 1.9, 0.20), (width, 0.85)] {
            path.lineWidth = w
            colour.withAlphaComponent(a * pulse).setStroke()
            path.stroke()
        }
        path.lineWidth = max(1, width * 0.38)
        NSColor(white: 1, alpha: 0.8 * pulse).setStroke()
        path.stroke()
    }

    func drawJukebox() {
        let W = bounds.width, H = bounds.height
        let pal = featPalette

        NSGradient(starting: pal.deep.withBrightness(0.12), ending: NSColor.black)?
            .draw(in: NSBezierPath(rect: bounds), angle: -90)

        let cabW = min(W * 0.62, H * 1.05)
        let cabX = W * 0.5 - cabW / 2
        let archR = cabW / 2
        let shoulderY = H * 0.50

        // Cabinet: arched top over straight sides.
        let body = NSBezierPath()
        body.move(to: NSPoint(x: cabX, y: H * 0.06))
        body.line(to: NSPoint(x: cabX, y: shoulderY))
        body.appendArc(withCenter: NSPoint(x: W * 0.5, y: shoulderY),
                       radius: archR, startAngle: 180, endAngle: 0, clockwise: true)
        body.line(to: NSPoint(x: cabX + cabW, y: H * 0.06))
        body.close()

        NSGraphicsContext.saveGraphicsState()
        let sh = NSShadow()
        sh.shadowColor = NSColor(white: 0, alpha: 0.8)
        sh.shadowBlurRadius = cabW * 0.10
        sh.set()
        NSGradient(starting: pal.console.withBrightness(0.34),
                   ending: pal.console.withBrightness(0.10))?.draw(in: body, angle: -75)
        NSGraphicsContext.restoreGraphicsState()

        // Neon tubing tracing the arch, breathing slowly.
        let pulse = 0.75 + 0.25 * CGFloat(sin(Double(phase) * 1.3))
        let tube1 = NSBezierPath()
        tube1.appendArc(withCenter: NSPoint(x: W * 0.5, y: shoulderY),
                        radius: archR * 0.93, startAngle: 178, endAngle: 2, clockwise: true)
        neon(tube1, colour: pal.accent, width: max(2, cabW * 0.010), pulse: pulse)

        let tube2 = NSBezierPath()
        tube2.appendArc(withCenter: NSPoint(x: W * 0.5, y: shoulderY),
                        radius: archR * 0.80, startAngle: 178, endAngle: 2, clockwise: true)
        neon(tube2, colour: pal.metal.withBrightness(0.85, saturation: 2),
             width: max(1.5, cabW * 0.007), pulse: 1.75 - pulse)

        // Bubble tubes down each shoulder.
        for t in 0..<2 {
            let tx = t == 0 ? cabX + cabW * 0.075 : cabX + cabW * 0.925
            let tw = cabW * 0.045
            let y0 = H * 0.09, y1 = shoulderY + archR * 0.10
            let tubeRect = NSRect(x: tx - tw / 2, y: y0, width: tw, height: y1 - y0)
            let tubePath = NSBezierPath(roundedRect: tubeRect,
                                        xRadius: tw / 2, yRadius: tw / 2)
            NSColor(white: 0.02, alpha: 0.85).setFill()
            tubePath.fill()
            neon(tubePath, colour: pal.accent, width: max(1, cabW * 0.004), pulse: 0.5)

            NSGraphicsContext.saveGraphicsState()
            tubePath.addClip()
            for b in bubbles where b.tube == t {
                let r = tw * b.r
                let bx = tx + b.x * (tw * 0.30)
                let by = y0 + b.y * (y1 - y0)
                let colour = pal.swatches.isEmpty
                    ? pal.accent
                    : pal.swatches[b.tint % pal.swatches.count]
                let box = NSRect(x: bx - r, y: by - r, width: r * 2, height: r * 2)

                // Glow, body, then an off-centre highlight — the three things
                // that make a filled circle read as lit glass.
                NSGradient(colors: [colour.withAlphaComponent(0.55),
                                    colour.withAlphaComponent(0)])?
                    .draw(fromCenter: NSPoint(x: bx, y: by), radius: r * 0.4,
                          toCenter: NSPoint(x: bx, y: by), radius: r * 2.1, options: [])
                colour.withAlphaComponent(0.80).setFill()
                NSBezierPath(ovalIn: box).fill()
                NSColor(white: 1, alpha: 0.60).setFill()
                NSBezierPath(ovalIn: NSRect(x: bx - r * 0.30, y: by + r * 0.10,
                                            width: r * 0.55, height: r * 0.55)).fill()
            }
            NSGraphicsContext.restoreGraphicsState()
        }

        guard albums.indices.contains(featIndex) else { return }

        // Display window with the record on show.
        let side = min(cabW * 0.44, H * 0.30)
        let cy = shoulderY + archR * 0.18
        let rect = NSRect(x: W * 0.5 - side / 2, y: cy - side / 2, width: side, height: side)
        let well = rect.insetBy(dx: -side * 0.09, dy: -side * 0.09)
        NSColor(white: 0.03, alpha: 0.95).setFill()
        NSBezierPath(roundedRect: well, xRadius: 8, yRadius: 8).fill()
        drawFeaturedCover(in: rect, radius: 4)
        let glass = NSBezierPath(roundedRect: well, xRadius: 8, yRadius: 8)
        glass.lineWidth = 2
        pal.accent.withAlphaComponent(0.8 * pulse).setStroke()
        glass.stroke()

        guard settings.showTrackLabel else { return }

        // Selection strips, as on the real thing.
        let text = featText
        let stripW = cabW * 0.74
        let stripX = W * 0.5 - stripW / 2
        var y = well.minY - H * 0.045
        let rowH = H * 0.042

        for (i, album) in albums.prefix(5).enumerated() {
            let r = NSRect(x: stripX, y: y - rowH, width: stripW, height: rowH * 0.86)
            let isNow = i == 0 && liveAlbumIndex == featIndex
            NSColor(red: 0.95, green: 0.92, blue: 0.84, alpha: isNow ? 1 : 0.82).setFill()
            NSBezierPath(roundedRect: r, xRadius: 2, yRadius: 2).fill()
            if isNow {
                pal.accent.setFill()
                NSBezierPath(rect: NSRect(x: r.minX, y: r.minY,
                                          width: r.width * 0.012, height: r.height)).fill()
            }
            let label = isNow ? "\(text.title) — \(text.artist)"
                              : "\(album.name) — \(album.artist)"
            let ps = NSMutableParagraphStyle()
            ps.lineBreakMode = .byTruncatingTail
            (label as NSString).draw(
                in: r.insetBy(dx: r.width * 0.03, dy: r.height * 0.22),
                withAttributes: [.font: retroFont(max(9, rowH * 0.42), bold: isNow),
                                 .foregroundColor: NSColor(white: 0.14, alpha: 1),
                                 .paragraphStyle: ps])
            y -= rowH
        }
    }
}

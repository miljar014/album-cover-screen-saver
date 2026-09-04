import ScreenSaver
import AppKit

extension AlbumCoverSaverView {

    /// A 90s portable CD player seen from above, lid open, jewel cases strewn
    /// around it.
    func drawCDPlayer() {
        let W = bounds.width, H = bounds.height
        let pal = featPalette
        let p = featProgress(idleSpan: settings.featureSeconds)

        NSGradient(starting: pal.deep.withBrightness(0.16),
                   ending: pal.deep.withBrightness(0.05))?
            .draw(in: NSBezierPath(rect: bounds), angle: -90)

        drawJewelCases(pal)

        guard albums.indices.contains(featIndex) else { return }

        let R = min(H * 0.32, W * 0.23)
        let c = NSPoint(x: W * 0.5, y: H * 0.53)

        // Player body: graphite plastic with a brushed lid.
        let body = NSRect(x: c.x - R * 1.42, y: c.y - R * 1.42,
                          width: R * 2.84, height: R * 2.84)
        let bodyPath = NSBezierPath(roundedRect: body, xRadius: R * 0.30, yRadius: R * 0.30)
        NSGraphicsContext.saveGraphicsState()
        let sh = NSShadow()
        sh.shadowColor = NSColor(white: 0, alpha: 0.75)
        sh.shadowBlurRadius = R * 0.30
        sh.shadowOffset = NSSize(width: 0, height: -R * 0.10)
        sh.set()
        NSGradient(starting: pal.metal.withBrightness(0.58),
                   ending: pal.metal.withBrightness(0.24))?.draw(in: bodyPath, angle: -70)
        NSGraphicsContext.restoreGraphicsState()

        // Disc well.
        func circle(_ r: CGFloat) -> NSBezierPath {
            NSBezierPath(ovalIn: NSRect(x: c.x - r, y: c.y - r, width: r * 2, height: r * 2))
        }
        NSColor(white: 0.05, alpha: 0.9).setFill()
        circle(R * 1.14).fill()

        // The CD: silver, with diffraction. Rotation is what sells it.
        let ctx = NSGraphicsContext.current!.cgContext
        ctx.saveGState()
        ctx.translateBy(x: c.x, y: c.y)
        ctx.rotate(by: -CGFloat(phase) * 1.15)
        ctx.translateBy(x: -c.x, y: -c.y)

        NSGradient(starting: NSColor(white: 0.86, alpha: 1),
                   ending: NSColor(white: 0.55, alpha: 1))?.draw(in: circle(R), angle: -60)

        // Rainbow diffraction: thin arcs sweeping the hue wheel.
        for i in 0..<64 {
            let a0 = CGFloat(i) * 360 / 64
            let hue = CGFloat(i) / 64
            let arc = NSBezierPath()
            arc.appendArc(withCenter: c, radius: R * 0.74,
                          startAngle: a0, endAngle: a0 + 6)
            arc.lineWidth = R * 0.44
            NSColor(hue: hue, saturation: 0.75, brightness: 1, alpha: 0.085).setStroke()
            arc.stroke()
        }

        // Printed label carries the artwork; the clear inner ring is the giveaway
        // that it's a CD and not a record.
        let labelR = R * 0.46
        drawFeaturedCover(in: NSRect(x: c.x - labelR, y: c.y - labelR,
                                     width: labelR * 2, height: labelR * 2))
        NSGraphicsContext.saveGraphicsState()
        circle(labelR).addClip()
        NSColor(white: 1, alpha: 0.10).setFill()
        NSBezierPath(rect: bounds).fill()
        NSGraphicsContext.restoreGraphicsState()

        NSColor(white: 0.80, alpha: 0.9).setFill()
        circle(R * 0.19).fill()
        NSColor(white: 0.72, alpha: 1).setFill()
        circle(R * 0.145).fill()
        NSColor(white: 0.05, alpha: 1).setFill()
        circle(R * 0.085).fill()
        ctx.restoreGState()

        let discEdge = circle(R)
        discEdge.lineWidth = 1
        NSColor(white: 1, alpha: 0.25).setStroke()
        discEdge.stroke()

        // Lid rim and hinge.
        let rim = circle(R * 1.16)
        rim.lineWidth = R * 0.05
        pal.metal.withBrightness(0.70).setStroke()
        rim.stroke()

        // LCD strip and transport buttons.
        let lcd = NSRect(x: body.minX + body.width * 0.12, y: body.minY + body.height * 0.055,
                         width: body.width * 0.50, height: body.height * 0.115)
        NSColor(red: 0.44, green: 0.55, blue: 0.42, alpha: 1).setFill()
        NSBezierPath(roundedRect: lcd, xRadius: 3, yRadius: 3).fill()
        let text = featText
        let ps = NSMutableParagraphStyle(); ps.lineBreakMode = .byTruncatingTail
        ("\u{25B6} " + text.title.uppercased() as NSString).draw(
            in: lcd.insetBy(dx: lcd.width * 0.04, dy: lcd.height * 0.24),
            withAttributes: [.font: NSFont.monospacedSystemFont(ofSize: max(8, lcd.height * 0.44),
                                                                weight: .bold),
                             .foregroundColor: NSColor(white: 0.10, alpha: 0.85),
                             .paragraphStyle: ps])
        // Progress ticks along the LCD, the way a track counter behaved.
        NSColor(white: 0.10, alpha: 0.5).setFill()
        NSBezierPath(rect: NSRect(x: lcd.minX + 3, y: lcd.minY + 2,
                                  width: (lcd.width - 6) * p, height: 2)).fill()

        for i in 0..<3 {
            let bx = body.maxX - body.width * (0.16 + CGFloat(i) * 0.11)
            let br = body.width * 0.035
            NSGradient(starting: pal.metal.withBrightness(0.75),
                       ending: pal.metal.withBrightness(0.35))?
                .draw(in: NSBezierPath(ovalIn: NSRect(x: bx - br, y: lcd.midY - br,
                                                      width: br * 2, height: br * 2)),
                      angle: -70)
        }

        guard settings.showTrackLabel else { return }
        let colW = W * 0.86
        let shadow = softShadow(0.75, blur: 14)
        var top = body.minY - H * 0.030
        let titleFont = fittedFont(text.title, width: colW, maxHeight: H * 0.09,
                                   base: max(17, H * 0.038), bold: true)
        top -= drawText(text.title, x: W * 0.5 - colW / 2, top: top, width: colW,
                        font: titleFont, color: pal.text, shadow: shadow,
                        centred: true) + H * 0.010
        drawText(text.artist, x: W * 0.5 - colW / 2, top: top, width: colW,
                 font: retroFont(max(12, H * 0.024)), color: pal.accent,
                 shadow: shadow, centred: true)
    }

    /// Jewel cases: cover behind clear plastic, with the spine and a glare stripe.
    private func drawJewelCases(_ pal: ArtPalette) {
        let ctx = NSGraphicsContext.current!.cgContext
        for s in sleeves {
            ctx.saveGState()
            ctx.translateBy(x: s.centre.x, y: s.centre.y)
            ctx.rotate(by: s.angle)
            let half = s.side / 2
            let rect = NSRect(x: -half, y: -half, width: s.side, height: s.side)

            NSGraphicsContext.saveGraphicsState()
            let sh = NSShadow()
            sh.shadowColor = NSColor(white: 0, alpha: 0.6)
            sh.shadowBlurRadius = s.side * 0.13
            sh.shadowOffset = NSSize(width: 0, height: -s.side * 0.035)
            sh.set()
            NSColor(white: 0.10, alpha: 0.95).setFill()
            NSBezierPath(rect: rect).fill()
            NSGraphicsContext.restoreGraphicsState()

            drawCover(s.index, in: rect.insetBy(dx: s.side * 0.035, dy: s.side * 0.035))
            pal.deep.withAlphaComponent(0.42).setFill()
            NSBezierPath(rect: rect).fill()

            // Spine down the left, and a diagonal glare across the plastic.
            NSColor(white: 0.85, alpha: 0.16).setFill()
            NSBezierPath(rect: NSRect(x: rect.minX, y: rect.minY,
                                      width: s.side * 0.085, height: s.side)).fill()
            let glare = NSBezierPath()
            glare.move(to: NSPoint(x: rect.minX, y: rect.minY + s.side * 0.30))
            glare.line(to: NSPoint(x: rect.minX + s.side * 0.55, y: rect.maxY))
            glare.line(to: NSPoint(x: rect.minX + s.side * 0.78, y: rect.maxY))
            glare.line(to: NSPoint(x: rect.minX, y: rect.minY + s.side * 0.07))
            glare.close()
            NSColor(white: 1, alpha: 0.09).setFill()
            glare.fill()

            NSColor(white: 1, alpha: 0.10).setStroke()
            let edge = NSBezierPath(rect: rect); edge.lineWidth = 1; edge.stroke()
            ctx.restoreGState()
        }
    }
}

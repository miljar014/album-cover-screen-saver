import ScreenSaver
import AppKit

extension AlbumCoverSaverView {

    /// Covers as platform posters on a tiled wall, with a train sliding through.
    func drawSubway() {
        let W = bounds.width, H = bounds.height
        let pal = featPalette
        let platformY = H * 0.26

        NSColor(red: 0.10, green: 0.10, blue: 0.11, alpha: 1).setFill()
        bounds.fill()

        // Tiled wall.
        let tile = H * 0.075
        let grout = NSColor(red: 0.55, green: 0.54, blue: 0.50, alpha: 1)
        grout.setFill()
        NSBezierPath(rect: NSRect(x: 0, y: platformY, width: W, height: H - platformY)).fill()
        var ty = platformY
        var row = 0
        while ty < H {
            var tx: CGFloat = (row % 2 == 0) ? 0 : -tile / 2   // running bond
            while tx < W {
                let r = NSRect(x: tx + 1.5, y: ty + 1.5, width: tile - 3, height: tile - 3)
                let shade = 0.86 + CGFloat(frac(sin(Double(row * 71 + Int(tx)) * 12.9898)
                                                * 43758.5453)) * 0.10
                NSColor(red: shade, green: shade * 0.99, blue: shade * 0.94, alpha: 1).setFill()
                NSBezierPath(roundedRect: r, xRadius: 2, yRadius: 2).fill()
                tx += tile
            }
            ty += tile
            row += 1
        }

        // Platform edge and floor.
        NSColor(red: 0.20, green: 0.20, blue: 0.21, alpha: 1).setFill()
        NSBezierPath(rect: NSRect(x: 0, y: 0, width: W, height: platformY)).fill()
        NSColor(red: 0.85, green: 0.76, blue: 0.20, alpha: 0.9).setFill()
        NSBezierPath(rect: NSRect(x: 0, y: platformY - H * 0.012,
                                  width: W, height: H * 0.012)).fill()

        guard albums.indices.contains(featIndex) else { return }
        let text = featText

        // Station name plate carries the artist.
        let plate = NSRect(x: W * 0.06, y: H * 0.80, width: W * 0.40, height: H * 0.11)
        NSColor(red: 0.06, green: 0.16, blue: 0.42, alpha: 1).setFill()
        NSBezierPath(roundedRect: plate, xRadius: 4, yRadius: 4).fill()
        NSColor.white.setStroke()
        let pb = NSBezierPath(roundedRect: plate.insetBy(dx: 5, dy: 5), xRadius: 3, yRadius: 3)
        pb.lineWidth = 2; pb.stroke()
        let ps = NSMutableParagraphStyle(); ps.alignment = .center
        ps.lineBreakMode = .byTruncatingTail
        (text.artist.uppercased() as NSString).draw(
            in: plate.insetBy(dx: 14, dy: plate.height * 0.28),
            withAttributes: [.font: retroFont(max(12, plate.height * 0.34), bold: true),
                             .foregroundColor: NSColor.white, .kern: 2,
                             .paragraphStyle: ps])

        // Poster frames along the wall; the current album is the lit one.
        let pw = min(W * 0.19, H * 0.30)
        let slots = max(2, Int(W / (pw * 1.5)))
        for i in 0..<slots {
            let cxp = W * 0.10 + (W * 0.82) * CGFloat(i) / CGFloat(max(1, slots - 1))
            let isNow = i == slots / 2
            let w = isNow ? pw * 1.22 : pw
            let r = NSRect(x: cxp - w / 2, y: platformY + H * 0.12, width: w, height: w * 1.25)
            NSColor(red: 0.13, green: 0.13, blue: 0.14, alpha: 1).setFill()
            NSBezierPath(rect: r.insetBy(dx: -w * 0.035, dy: -w * 0.035)).fill()

            let idx = isNow ? featIndex
                            : (albums.indices.contains(i + 1) ? i + 1 : i % albums.count)
            drawCover(idx, in: r.insetBy(dx: w * 0.03, dy: w * 0.03))
            if isNow {
                // Backlit poster box: the only bright thing on the platform.
                NSGradient(colors: [NSColor(white: 1, alpha: 0.16), NSColor(white: 1, alpha: 0)])?
                    .draw(fromCenter: NSPoint(x: r.midX, y: r.midY), radius: w * 0.4,
                          toCenter: NSPoint(x: r.midX, y: r.midY), radius: w * 1.3, options: [])
                pal.accent.setStroke()
                let g = NSBezierPath(rect: r.insetBy(dx: -w * 0.035, dy: -w * 0.035))
                g.lineWidth = 2; g.stroke()
                let ps2 = NSMutableParagraphStyle(); ps2.alignment = .center
                ps2.lineBreakMode = .byTruncatingTail
                (text.title as NSString).draw(
                    in: NSRect(x: r.minX, y: r.minY - H * 0.045,
                               width: r.width, height: H * 0.04),
                    withAttributes: [.font: retroFont(max(10, H * 0.021), bold: true),
                                     .foregroundColor: NSColor.white,
                                     .paragraphStyle: ps2])
            } else {
                NSColor(white: 0, alpha: 0.45).setFill()
                NSBezierPath(rect: r).fill()
            }
        }

        // A train sweeps the foreground every so often.
        let cycle: CGFloat = 26
        let t = CGFloat(phase).truncatingRemainder(dividingBy: cycle) / cycle
        guard t < 0.34 else { return }
        let trainX = -W * 1.1 + (W * 2.4) * (t / 0.34)
        let body = NSRect(x: trainX, y: -H * 0.02, width: W * 1.05, height: platformY * 1.06)
        NSGradient(starting: NSColor(red: 0.30, green: 0.32, blue: 0.35, alpha: 1),
                   ending: NSColor(red: 0.13, green: 0.14, blue: 0.16, alpha: 1))?
            .draw(in: NSBezierPath(roundedRect: body, xRadius: 10, yRadius: 10), angle: -90)
        var wx = body.minX + W * 0.05
        while wx < body.maxX - W * 0.05 {
            let win = NSRect(x: wx, y: body.minY + body.height * 0.42,
                             width: W * 0.075, height: body.height * 0.36)
            NSColor(red: 0.95, green: 0.90, blue: 0.70, alpha: 0.85).setFill()
            NSBezierPath(roundedRect: win, xRadius: 3, yRadius: 3).fill()
            wx += W * 0.11
        }
    }
}

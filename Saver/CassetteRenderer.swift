import ScreenSaver
import AppKit

extension AlbumCoverSaverView {

    /// An '80s tape deck. The reels wind for real: the supply reel empties as the
    /// take-up fills, and each turns at the speed its own radius implies.
    func drawCassette() {
        let W = bounds.width, H = bounds.height
        let pal = featPalette
        let tape = tapeState

        NSGradient(starting: pal.deep.withBrightness(0.14),
                   ending: pal.deep.withBrightness(0.04))?
            .draw(in: NSBezierPath(rect: bounds), angle: -90)

        // Deck face: brushed metal.
        let deck = NSRect(x: W * 0.09, y: H * 0.28, width: W * 0.82, height: H * 0.44)
        let deckPath = NSBezierPath(roundedRect: deck, xRadius: deck.height * 0.05,
                                    yRadius: deck.height * 0.05)
        NSGraphicsContext.saveGraphicsState()
        let sh = NSShadow()
        sh.shadowColor = NSColor(white: 0, alpha: 0.65)
        sh.shadowBlurRadius = deck.height * 0.08
        sh.shadowOffset = NSSize(width: 0, height: -deck.height * 0.03)
        sh.set()
        NSGradient(starting: pal.metal.withBrightness(0.62),
                   ending: pal.metal.withBrightness(0.32))?.draw(in: deckPath, angle: -80)
        NSGraphicsContext.restoreGraphicsState()

        // Brushed striations.
        NSGraphicsContext.saveGraphicsState()
        deckPath.addClip()
        for i in 0..<70 {
            let y = deck.minY + deck.height * CGFloat(i) / 70
            let line = NSBezierPath()
            line.move(to: NSPoint(x: deck.minX, y: y))
            line.line(to: NSPoint(x: deck.maxX, y: y))
            line.lineWidth = 1
            NSColor(white: i % 2 == 0 ? 1 : 0, alpha: 0.035).setStroke()
            line.stroke()
        }
        NSGraphicsContext.restoreGraphicsState()

        // Smoked window.
        let win = NSRect(x: deck.minX + deck.width * 0.045,
                         y: deck.minY + deck.height * 0.20,
                         width: deck.width * 0.60, height: deck.height * 0.66)
        let winPath = NSBezierPath(roundedRect: win, xRadius: 6, yRadius: 6)
        NSColor(white: 0.05, alpha: 0.92).setFill()
        winPath.fill()

        guard albums.indices.contains(featIndex) else { return }

        // The cassette itself: the cover printed across the shell.
        let cass = win.insetBy(dx: win.width * 0.035, dy: win.height * 0.07)
        drawFeaturedCover(in: cass, radius: 4)
        pal.deep.withAlphaComponent(0.30).setFill()
        NSBezierPath(roundedRect: cass, xRadius: 4, yRadius: 4).fill()

        // Reels. Wound tape covers area, not radius, so the packs grow and shrink
        // as square roots: the take-up starts fast and slows, the supply does the
        // reverse. Turns fall straight out of that same geometry, which is what
        // ties the reels to the song — they are driven by playback position, so
        // they stop when the music stops and jump when you seek, exactly like the
        // tonearm on the record player.
        let hubY = cass.midY - cass.height * 0.04
        let maxR = cass.height * 0.30
        let hubR = maxR * 0.36
        let t = min(1, max(0, tape.progress))
        let ring = maxR * maxR - hubR * hubR
        let supplyR = sqrt(maxR * maxR - t * ring)
        let takeupR = sqrt(hubR * hubR + t * ring)
        let supplyC = NSPoint(x: cass.minX + cass.width * 0.29, y: hubY)
        let takeupC = NSPoint(x: cass.minX + cass.width * 0.71, y: hubY)

        // Whole-tape revolutions across one track. Scaling by the track's own length
        // keeps the deck at a steady ~0.1–0.3 rev/s whether it is a 90-second interlude
        // or an eight-minute closer. Turns are linear in radius (each wrap adds one
        // tape thickness), so this is the physical relationship, not an approximation.
        let perRadius = 2 * CGFloat.pi * CGFloat(tape.span) * 0.15 / max(0.001, maxR - hubR)

        // Tape first, so each pack prints over the end of its own span.
        drawTapePath(in: cass, supply: supplyC, supplyR: supplyR,
                     takeup: takeupC, takeupR: takeupR, pal: pal)

        drawReel(at: supplyC, tape: supplyR, hub: hubR,
                 angle: perRadius * (maxR - supplyR), pal: pal)
        drawReel(at: takeupC, tape: takeupR, hub: hubR,
                 angle: perRadius * (takeupR - hubR), pal: pal)

        // Shell edge and window glare.
        let shell = NSBezierPath(roundedRect: cass, xRadius: 4, yRadius: 4)
        shell.lineWidth = 2
        pal.metal.withAlphaComponent(0.5).setStroke()
        shell.stroke()
        NSGraphicsContext.saveGraphicsState()
        winPath.addClip()
        NSGradient(colors: [NSColor(white: 1, alpha: 0.10), NSColor(white: 1, alpha: 0)])?
            .draw(in: NSBezierPath(rect: NSRect(x: win.minX, y: win.midY,
                                                width: win.width, height: win.height / 2)),
                  angle: -90)
        NSGraphicsContext.restoreGraphicsState()

        drawTransport(in: NSRect(x: win.maxX + deck.width * 0.035, y: win.minY,
                                 width: deck.maxX - win.maxX - deck.width * 0.075,
                                 height: win.height), pal: pal)

        guard settings.showTrackLabel else { return }
        let text = featText
        let colW = W * 0.82
        let shadow = softShadow(0.7, blur: 14)
        var top = deck.minY - H * 0.035
        let titleFont = fittedFont(text.title, width: colW, maxHeight: H * 0.10,
                                   base: max(18, H * 0.042), bold: true)
        top -= drawText(text.title, x: deck.minX, top: top, width: colW,
                        font: titleFont, color: pal.text, shadow: shadow) + H * 0.012
        drawText([text.artist, text.sub].filter { !$0.isEmpty }.joined(separator: "  ·  "),
                 x: deck.minX, top: top, width: colW,
                 font: retroFont(max(12, H * 0.024)), color: pal.accent, shadow: shadow)
    }

    /// How far the tape has wound, and over how long.
    ///
    /// The reels are the length of the song, so this has to be the *track* clock,
    /// not the featured-album clock: `featProgress` falls back to "seconds per
    /// album" (30s by default), which winds a whole C90 in half a minute. It also
    /// takes the live position whenever anything is playing, even when that album
    /// never made it into the archive and so has no featured index to match.
    ///
    /// With nothing playing it winds over a typical song length instead, on the
    /// animation clock rather than the album clock, so the tape keeps running
    /// across album changes rather than snapping back to the start every 30s.
    private var tapeState: (progress: CGFloat, span: Double) {
        if nowPlaying.isLive, nowPlaying.durationMs > 0 {
            return (CGFloat(nowPlaying.progressFraction),
                    Double(nowPlaying.durationMs) / 1000)
        }
        let span = 210.0
        return (CGFloat(phase.truncatingRemainder(dividingBy: span) / span), span)
    }

    /// The exposed tape run: off the outer edge of the supply pack, down around a
    /// guide roller, across the head opening, and back up onto the take-up pack.
    /// Both ends are true tangents of the *wound* radius, so the span shifts on its
    /// own as one pack empties into the other — the tape follows playback rather
    /// than being painted on.
    private func drawTapePath(in cass: NSRect, supply: NSPoint, supplyR: CGFloat,
                              takeup: NSPoint, takeupR: CGFloat, pal: ArtPalette) {
        let guideR = cass.height * 0.032
        let guideY = cass.minY + cass.height * 0.150
        let gl = NSPoint(x: cass.minX + cass.width * 0.130, y: guideY)
        let gr = NSPoint(x: cass.maxX - cass.width * 0.130, y: guideY)
        let width = max(1.5, cass.height * 0.020)

        // Tangent from an external point to a circle; of the two solutions keep the
        // one on the outside of the shell, which is the side real tape leaves from.
        func tangent(on c: NSPoint, r: CGFloat, from p: NSPoint, keepLeft: Bool) -> NSPoint {
            let d = max(r + 0.001, hypot(p.x - c.x, p.y - c.y))
            let base = atan2(p.y - c.y, p.x - c.x)
            let off = acos(min(1, r / d))
            let a = NSPoint(x: c.x + cos(base + off) * r, y: c.y + sin(base + off) * r)
            let b = NSPoint(x: c.x + cos(base - off) * r, y: c.y + sin(base - off) * r)
            return (keepLeft ? a.x < b.x : a.x > b.x) ? a : b
        }

        let tl = tangent(on: supply, r: supplyR, from: gl, keepLeft: true)
        let tr = tangent(on: takeup, r: takeupR, from: gr, keepLeft: false)

        let path = NSBezierPath()
        path.move(to: tl)
        path.line(to: NSPoint(x: gl.x - guideR, y: gl.y))
        path.appendArc(withCenter: gl, radius: guideR,
                       startAngle: 180, endAngle: 270, clockwise: false)
        path.line(to: NSPoint(x: gr.x, y: gr.y - guideR))
        path.appendArc(withCenter: gr, radius: guideR,
                       startAngle: 270, endAngle: 360, clockwise: false)
        path.line(to: tr)
        path.lineCapStyle = .round
        path.lineJoinStyle = .round

        path.lineWidth = width * 1.9
        NSColor(white: 0, alpha: 0.35).setStroke()
        path.stroke()
        path.lineWidth = width
        NSColor(red: 0.22, green: 0.14, blue: 0.10, alpha: 1).setStroke()
        path.stroke()
        path.lineWidth = width * 0.28
        NSColor(white: 1, alpha: 0.13).setStroke()
        path.stroke()

        // Head opening in the shell's lower edge, with the capstan pair beside it.
        let headW = cass.width * 0.16, headH = cass.height * 0.14
        let head = NSRect(x: cass.midX - headW / 2, y: guideY - headH * 0.55,
                          width: headW, height: headH)
        NSColor(white: 0.05, alpha: 0.85).setFill()
        NSBezierPath(roundedRect: head, xRadius: 2, yRadius: 2).fill()
        pal.metal.withAlphaComponent(0.55).setFill()
        NSBezierPath(rect: NSRect(x: head.midX - headW * 0.12, y: head.minY,
                                  width: headW * 0.24, height: headH * 0.62)).fill()

        // Guide rollers on top of the tape they carry.
        for g in [gl, gr] {
            let r = NSRect(x: g.x - guideR, y: g.y - guideR, width: guideR * 2, height: guideR * 2)
            NSGradient(starting: pal.metal.withBrightness(0.78),
                       ending: pal.metal.withBrightness(0.30))?
                .draw(in: NSBezierPath(ovalIn: r), angle: -70)
            NSColor(white: 0.04, alpha: 0.9).setFill()
            NSBezierPath(ovalIn: r.insetBy(dx: guideR * 0.62, dy: guideR * 0.62)).fill()
        }
    }

    private func drawReel(at c: NSPoint, tape: CGFloat, hub: CGFloat,
                          angle: CGFloat, pal: ArtPalette) {
        func circle(_ r: CGFloat) -> NSBezierPath {
            NSBezierPath(ovalIn: NSRect(x: c.x - r, y: c.y - r, width: r * 2, height: r * 2))
        }
        // Reel well.
        NSColor(white: 0.04, alpha: 0.95).setFill()
        circle(tape * 1.06).fill()
        // Wound tape.
        NSGradient(starting: NSColor(red: 0.30, green: 0.19, blue: 0.13, alpha: 1),
                   ending: NSColor(red: 0.16, green: 0.10, blue: 0.07, alpha: 1))?
            .draw(in: circle(tape), angle: -60)

        // The caller hands us the angle the tape has actually wound, so the empty
        // reel visibly outruns the full one without either being on a free clock.
        // Counter-clockwise, to match the tape running out of the supply pack's
        // left side and onto the take-up pack's right.
        let ctx = NSGraphicsContext.current!.cgContext
        ctx.saveGState()
        ctx.translateBy(x: c.x, y: c.y)
        ctx.rotate(by: angle)
        ctx.translateBy(x: -c.x, y: -c.y)
        pal.metal.withAlphaComponent(0.85).setFill()
        circle(hub).fill()
        NSColor(white: 0.06, alpha: 1).setFill()
        for i in 0..<6 {
            let a = CGFloat(i) * .pi / 3
            let t = NSBezierPath()
            t.move(to: c)
            t.line(to: NSPoint(x: c.x + cos(a) * hub * 0.92, y: c.y + sin(a) * hub * 0.92))
            t.lineWidth = hub * 0.30
            NSColor(white: 0.06, alpha: 1).setStroke()
            t.stroke()
        }
        circle(hub * 0.30).fill()
        ctx.restoreGState()
    }

    /// Transport keys, in the piano-key style of an '80s deck: rewind, play/pause,
    /// fast forward. Cosmetic only — the play key rides down while the tape runs.
    private func drawTransport(in box: NSRect, pal: ArtPalette) {
        let running = nowPlaying.isLive

        // Recessed sub-plate the keys sit in.
        let plate = box.insetBy(dx: 0, dy: box.height * 0.20)
        let platePath = NSBezierPath(roundedRect: plate, xRadius: plate.height * 0.14,
                                     yRadius: plate.height * 0.14)
        NSGradient(starting: pal.metal.withBrightness(0.18),
                   ending: pal.metal.withBrightness(0.34))?.draw(in: platePath, angle: -90)
        platePath.lineWidth = 1
        NSColor(white: 0, alpha: 0.45).setStroke()
        platePath.stroke()

        let gap = plate.width * 0.055
        let keyW = (plate.width - gap * 4) / 3
        let keyH = plate.height * 0.62
        let baseY = plate.minY + plate.height * 0.20

        for i in 0..<3 {
            let pressed = (i == 1 && running)
            let drop = pressed ? keyH * 0.12 : 0
            let key = NSRect(x: plate.minX + gap + (keyW + gap) * CGFloat(i),
                             y: baseY - drop, width: keyW, height: keyH)
            let r = keyW * 0.16
            let path = NSBezierPath(roundedRect: key, xRadius: r, yRadius: r)

            // Key skirt, so a raised key reads as a physical block.
            if !pressed {
                let skirt = NSBezierPath(roundedRect: key.offsetBy(dx: 0, dy: -keyH * 0.10),
                                         xRadius: r, yRadius: r)
                NSColor(white: 0.04, alpha: 0.75).setFill()
                skirt.fill()
            }

            NSGradient(starting: pal.metal.withBrightness(pressed ? 0.34 : 0.72),
                       ending: pal.metal.withBrightness(pressed ? 0.20 : 0.40))?
                .draw(in: path, angle: -90)
            path.lineWidth = 1
            NSColor(white: 1, alpha: pressed ? 0.10 : 0.22).setStroke()
            path.stroke()

            let ink = NSColor(white: pressed ? 0.86 : 0.10, alpha: 1)
            drawTransportIcon(i, in: key.insetBy(dx: keyW * 0.26, dy: keyH * 0.30),
                              paused: !running, color: ink)
        }

        // Play lamp.
        let lampR = plate.height * 0.055
        let lamp = NSRect(x: plate.midX - lampR, y: plate.maxY - plate.height * 0.14 - lampR,
                          width: lampR * 2, height: lampR * 2)
        let on = NSColor(red: 0.95, green: 0.24, blue: 0.20, alpha: 1)
        (running ? on : NSColor(red: 0.30, green: 0.09, blue: 0.08, alpha: 1)).setFill()
        NSBezierPath(ovalIn: lamp).fill()
        if running {
            on.withAlphaComponent(0.28).setFill()
            NSBezierPath(ovalIn: lamp.insetBy(dx: -lampR * 1.4, dy: -lampR * 1.4)).fill()
        }
    }

    /// 0 = rewind, 1 = play/pause, 2 = fast forward.
    private func drawTransportIcon(_ kind: Int, in r: NSRect, paused: Bool, color: NSColor) {
        color.setFill()

        func triangle(_ box: NSRect, pointsRight: Bool) {
            let t = NSBezierPath()
            if pointsRight {
                t.move(to: NSPoint(x: box.minX, y: box.minY))
                t.line(to: NSPoint(x: box.minX, y: box.maxY))
                t.line(to: NSPoint(x: box.maxX, y: box.midY))
            } else {
                t.move(to: NSPoint(x: box.maxX, y: box.minY))
                t.line(to: NSPoint(x: box.maxX, y: box.maxY))
                t.line(to: NSPoint(x: box.minX, y: box.midY))
            }
            t.close()
            t.fill()
        }

        switch kind {
        case 1 where paused:
            let barW = r.width * 0.30
            NSBezierPath(rect: NSRect(x: r.minX, y: r.minY, width: barW, height: r.height)).fill()
            NSBezierPath(rect: NSRect(x: r.maxX - barW, y: r.minY,
                                      width: barW, height: r.height)).fill()
        case 1:
            triangle(NSRect(x: r.minX + r.width * 0.12, y: r.minY,
                            width: r.width * 0.80, height: r.height), pointsRight: true)
        default:
            // A pair of chevrons, the way the panel legend on a deck prints them.
            let w = r.width * 0.54
            let a = NSRect(x: r.minX, y: r.minY, width: w, height: r.height)
            let b = NSRect(x: r.maxX - w, y: r.minY, width: w, height: r.height)
            triangle(a, pointsRight: kind == 2)
            triangle(b, pointsRight: kind == 2)
        }
    }
}

import ScreenSaver
import AppKit

/// A record leaning around the deck: the cover, and sometimes the disc halfway
/// out of it.
struct Sleeve {
    var centre: NSPoint
    var side: CGFloat
    var angle: CGFloat
    var index: Int
    var discPeek: CGFloat       // 0 = disc tucked in; otherwise how far it slides out
    var discDirection: CGFloat  // radians — which way it slides
}

/// Lift → swap → lower, roughly the pace of a real changer.
private let changeDuration: TimeInterval = 1.9

extension AlbumCoverSaverView {

    // MARK: - Motion

    func advanceVinyl() {
        syncTable()
        // Negative because AppKit's y-axis points up and records turn clockwise.
        let revsPerSecond = max(0.5, settings.vinylRPM) / 60
        vinylAngle -= CGFloat(animationTimeInterval * revsPerSecond * 2 * .pi)

        if vinylChangeStart >= 0 {
            if phase - vinylChangeStart >= changeDuration {
                vinylChangeStart = -1
                vinylStartedAt = phase
            }
            return
        }
        if let live = liveVinylIndex {
            if live != vinylIndex { beginVinylChange(to: live) }
            return
        }
        if phase - vinylStartedAt >= max(20, settings.vinylSecondsPerRecord) {
            beginVinylChange(to: pickIndex(avoiding: [vinylIndex]))
        }
    }

    private func beginVinylChange(to index: Int) {
        vinylPrevIndex = vinylIndex
        vinylIndex = index
        vinylChangeStart = phase
    }

    /// 0 = arm down on the record, 1 = parked clear of it.
    private var armLift: CGFloat {
        guard vinylChangeStart >= 0 else { return 0 }
        let t = phase - vinylChangeStart
        // Eased so the arm lifts and lowers with weight instead of snapping.
        if t < 0.5 { return Ease.inOut(CGFloat(t / 0.5)) }
        if t < 1.0 { return 1 }
        return 1 - Ease.inOut(CGFloat((t - 1.0) / 0.9))
    }

    /// How far through the record we are, 0...1.
    ///
    /// When Spotify is playing this is the true position in the track, so the arm
    /// crosses the record exactly as the song plays. Otherwise it falls back to a
    /// timer.
    private var playProgress: CGFloat {
        if liveVinylIndex != nil, nowPlaying.durationMs > 0 {
            return CGFloat(nowPlaying.progressFraction)
        }
        let span = max(20, settings.vinylSecondsPerRecord)
        return min(1, max(0, CGFloat((phase - vinylStartedAt) / span)))
    }

    // MARK: - The table

    /// Seeds the table from recent listening the first time, then leaves it
    /// alone. Records are added as they play and only ever removed when the pile
    /// outgrows its limit — so the table is a record of the session, not a fresh
    /// random handful every time the archive updates.
    func buildVinylSleeves() {
        claimTable(for: .vinyl)
        guard !albums.isEmpty else { return }
        let cap = tableCap
        guard cap > 0 else { table.removeAll(); return }

        if table.isEmpty {
            // Newest first, dropped in reverse so the most recent lands on top.
            for album in albums.prefix(cap).reversed() { addToTable(album.id) }
        }
        syncTable()
    }

    private var tableCap: Int {
        isPreview ? 5 : max(0, min(40, settings.vinylSleeveCount))
    }

    /// Adds whatever is on the platter, and retires the oldest once full.
    func syncTable() {
        let cap = tableCap
        if cap == 0 { table.removeAll(); return }
        if albums.indices.contains(vinylIndex) {
            addToTable(albums[vinylIndex].id)
        }
        while table.count > cap { table.removeFirst() }
    }

    private func addToTable(_ albumID: String) {
        pinToTable(albumID, spot: findSpot())
    }

    /// Vinyl's keep-out: the platter, and the text column on the right.
    private func findSpot() -> (x: CGFloat, y: CGFloat, size: CGFloat)? {
        let W = bounds.width, H = bounds.height
        let R = min(H * 0.34, W * 0.25)
        return findTableSpot(
            sizeRange: 0.15...0.25,
            scale: CGFloat(max(0.4, settings.vinylSleeveScale)),
            keepOutCentre: NSPoint(x: W * (isPreview ? 0.42 : 0.33), y: H * 0.50),
            keepOutRadius: R * 1.40,
            keepOutRect: NSRect(x: W * 0.56, y: H * 0.18, width: W * 0.42, height: H * 0.64))
    }

    private func drawSleeves(_ pal: ArtPalette) {
        let ctx = NSGraphicsContext.current!.cgContext
        let W = bounds.width, H = bounds.height
        for t in table {
            guard let idx = albumIndexByID[t.albumID] else { continue }
            let side = t.sizeFrac * H

            // Newly played records drop onto the table rather than blinking in.
            let age = phase - t.addedAt
            var drop: CGFloat = 1, lift: CGFloat = 0, alpha: CGFloat = 1
            if age < 0.7 {
                let k = Ease.out(CGFloat(age / 0.7))
                drop = 1.3 - 0.3 * k
                lift = (1 - k) * H * 0.03
                alpha = Ease.out(CGFloat(age / 0.45))
            }

            let s = Sleeve(centre: NSPoint(x: t.nx * W, y: t.ny * H + lift),
                           side: side * drop, angle: t.angle, index: idx,
                           discPeek: t.discPeek, discDirection: t.discDirection)

            ctx.saveGState()
            ctx.setAlpha(alpha)
            ctx.translateBy(x: s.centre.x, y: s.centre.y)
            ctx.rotate(by: s.angle)

            let half = s.side / 2
            let rect = NSRect(x: -half, y: -half, width: s.side, height: s.side)

            // The disc first, so the sleeve prints over it and it reads as being
            // tucked inside rather than sitting on top.
            if s.discPeek > 0 {
                let dc = NSPoint(x: cos(s.discDirection) * s.side * s.discPeek,
                                 y: sin(s.discDirection) * s.side * s.discPeek)
                let dr = half * 0.95
                func disc(_ r: CGFloat) -> NSBezierPath {
                    NSBezierPath(ovalIn: NSRect(x: dc.x - r, y: dc.y - r,
                                                width: r * 2, height: r * 2))
                }
                NSGraphicsContext.saveGraphicsState()
                let ds = NSShadow()
                ds.shadowColor = NSColor(white: 0, alpha: 0.55)
                ds.shadowBlurRadius = s.side * 0.10
                ds.shadowOffset = NSSize(width: 0, height: -s.side * 0.02)
                ds.set()
                NSColor(white: 0.05, alpha: 1).setFill()
                disc(dr).fill()
                NSGraphicsContext.restoreGraphicsState()

                var rr = dr * 0.90
                while rr > dr * 0.42 {
                    NSColor(white: 0.30, alpha: 0.16).setStroke()
                    let p = disc(rr); p.lineWidth = 1; p.stroke()
                    rr -= dr * 0.07
                }
                let lr = dr * 0.35
                if let img = image(s.index) {
                    NSGraphicsContext.saveGraphicsState()
                    disc(lr).addClip()
                    img.draw(in: NSRect(x: dc.x - lr, y: dc.y - lr,
                                        width: lr * 2, height: lr * 2),
                             from: .zero, operation: .sourceOver, fraction: 0.9)
                    NSGraphicsContext.restoreGraphicsState()
                }
                pal.accent.withAlphaComponent(0.5).setStroke()
                let lring = disc(lr); lring.lineWidth = 1.2; lring.stroke()
            }

            // The sleeve itself.
            NSGraphicsContext.saveGraphicsState()
            let sh = NSShadow()
            sh.shadowColor = NSColor(white: 0, alpha: 0.6)
            sh.shadowBlurRadius = s.side * 0.14
            sh.shadowOffset = NSSize(width: 0, height: -s.side * 0.04)
            sh.set()
            NSColor(white: 0.06, alpha: 1).setFill()
            NSBezierPath(rect: rect).fill()
            NSGraphicsContext.restoreGraphicsState()

            image(s.index)?.draw(in: rect, from: .zero, operation: .sourceOver, fraction: 1)

            // Push the whole pile back so the deck stays the subject.
            pal.deep.withAlphaComponent(0.46).setFill()
            NSBezierPath(rect: rect).fill()
            NSColor(white: 1, alpha: 0.05).setStroke()
            let edge = NSBezierPath(rect: rect); edge.lineWidth = 1; edge.stroke()

            ctx.restoreGState()
        }
    }

    // MARK: - Drawing

    func drawVinyl() {
        let W = bounds.width, H = bounds.height
        let pal = albums.indices.contains(vinylIndex)
            ? PaletteStore.palette(for: albums[vinylIndex].id, image: image(vinylIndex))
            : ArtPalette.fallback

        drawConsole(pal)
        drawSleeves(pal)

        let R = min(H * 0.34, W * 0.25)
        let centre = NSPoint(x: W * (isPreview ? 0.42 : 0.33), y: H * 0.50)

        drawPlatter(centre: centre, radius: R, pal)

        var fade: CGFloat = 1
        if vinylChangeStart >= 0 {
            let t = phase - vinylChangeStart
            if t < 0.55 { fade = 0 }
            else if t < 1.0 { fade = Ease.inOut(CGFloat((t - 0.55) / 0.45)) }
        }
        if fade < 1 {
            drawRecord(centre: centre, radius: R, index: vinylPrevIndex, alpha: 1 - fade, pal)
        }
        if fade > 0 {
            drawRecord(centre: centre, radius: R, index: vinylIndex, alpha: fade, pal)
        }

        drawTonearm(centre: centre, radius: R, pal)
        if !isPreview { drawRecordInfo(pal) }
    }

    // MARK: console

    private func drawConsole(_ pal: ArtPalette) {
        // The room the deck sits in: the cover itself, blurred out of focus, so
        // the whole screen belongs to the record being played.
        var haveBackdrop = false
        if albums.indices.contains(vinylIndex),
           let blur = BlurStore.blurred(for: albums[vinylIndex].id, image: image(vinylIndex)) {
            let cover = max(bounds.width, bounds.height) * 1.15
            blur.draw(in: NSRect(x: bounds.midX - cover / 2, y: bounds.midY - cover / 2,
                                 width: cover, height: cover),
                      from: .zero, operation: .sourceOver, fraction: 1)
            // Knocked back enough to sit behind the deck, but not so far that it
            // reads as a plain dark border. An earlier version also drew a heavy
            // vignette here, which blacked out the margin — the only place this
            // backdrop is actually visible.
            pal.deep.withAlphaComponent(0.32).setFill()
            bounds.fill()
            haveBackdrop = true
        }
        if !haveBackdrop {
            NSGradient(starting: pal.deep.withBrightness(0.16),
                       ending: pal.deep.withBrightness(0.045))?
                .draw(in: NSBezierPath(rect: bounds), angle: -90)
        }

        let plinth = bounds.insetBy(dx: bounds.width * 0.085, dy: bounds.height * 0.095)
        let path = NSBezierPath(roundedRect: plinth,
                                xRadius: plinth.height * 0.05, yRadius: plinth.height * 0.05)

        NSGraphicsContext.saveGraphicsState()
        let sh = NSShadow()
        sh.shadowColor = NSColor(white: 0, alpha: 0.65)
        sh.shadowBlurRadius = plinth.height * 0.05
        sh.shadowOffset = NSSize(width: 0, height: -plinth.height * 0.012)
        sh.set()
        NSGradient(starting: pal.consoleLight, ending: pal.console)?.draw(in: path, angle: -70)
        NSGraphicsContext.restoreGraphicsState()

        // Grain, seeded from the stripe index so it stays put between frames.
        NSGraphicsContext.saveGraphicsState()
        path.addClip()
        let dark = pal.console.withBrightness(0.14)
        for i in 0..<64 {
            let f = fract(sin(Double(i) * 12.9898) * 43758.5453)
            let g = fract(sin(Double(i) * 78.233) * 12345.6789)
            let y = plinth.minY + plinth.height * CGFloat(Double(i) / 64.0 + f * 0.012)
            let line = NSBezierPath()
            line.move(to: NSPoint(x: plinth.minX, y: y))
            var x = plinth.minX
            while x < plinth.maxX {
                x += plinth.width / 12
                let wobble = CGFloat(sin(Double(x) * 0.004 + f * 6.28)) * plinth.height * 0.006
                line.line(to: NSPoint(x: x, y: y + wobble))
            }
            line.lineWidth = 1 + CGFloat(g) * 2
            dark.withAlphaComponent(0.06 + CGFloat(f) * 0.08).setStroke()
            line.stroke()
        }
        NSGraphicsContext.restoreGraphicsState()

        // Trim strip in the cover's accent — the detail that ties console to record.
        pal.accent.withAlphaComponent(0.5).setFill()
        NSBezierPath(rect: NSRect(x: plinth.minX, y: plinth.minY + plinth.height * 0.055,
                                  width: plinth.width,
                                  height: max(1, plinth.height * 0.006))).fill()

        path.lineWidth = 2
        pal.deep.withAlphaComponent(0.7).setStroke()
        path.stroke()
    }

    private func fract(_ x: Double) -> Double { x - x.rounded(.down) }

    // MARK: platter

    private func drawPlatter(centre c: NSPoint, radius R: CGFloat, _ pal: ArtPalette) {
        func circle(_ r: CGFloat) -> NSBezierPath {
            NSBezierPath(ovalIn: NSRect(x: c.x - r, y: c.y - r, width: r * 2, height: r * 2))
        }
        NSGraphicsContext.saveGraphicsState()
        let sh = NSShadow()
        sh.shadowColor = NSColor(white: 0, alpha: 0.75)
        sh.shadowBlurRadius = R * 0.16
        sh.shadowOffset = NSSize(width: 0, height: -R * 0.04)
        sh.set()
        pal.metal.setFill()
        circle(R * 1.16).fill()
        NSGraphicsContext.restoreGraphicsState()

        NSGradient(starting: pal.metal, ending: pal.metal.withBrightness(0.38))?
            .draw(in: circle(R * 1.16), angle: -60)
        pal.deep.withBrightness(0.10).setFill()
        circle(R * 1.07).fill()
    }

    // MARK: the record

    private func drawRecord(centre c: NSPoint, radius R: CGFloat, index: Int,
                            alpha: CGFloat, _ pal: ArtPalette) {
        guard alpha > 0.01 else { return }
        let ctx = NSGraphicsContext.current!.cgContext
        func circle(_ r: CGFloat) -> NSBezierPath {
            NSBezierPath(ovalIn: NSRect(x: c.x - r, y: c.y - r, width: r * 2, height: r * 2))
        }

        NSColor(white: 0.045, alpha: alpha).setFill()
        circle(R).fill()

        var r = R * 0.985
        var i = 0
        while r > R * 0.40 {
            let bright = i % 3 == 0
            NSColor(white: bright ? 0.22 : 0.10, alpha: alpha * 0.5).setStroke()
            let p = circle(r)
            p.lineWidth = bright ? 0.9 : 1.6
            p.stroke()
            r -= R * 0.0125
            i += 1
        }

        // Grooves are rotationally symmetric, so the spin only reads from these
        // sweeps and the label turning with them.
        ctx.saveGState()
        ctx.translateBy(x: c.x, y: c.y)
        ctx.rotate(by: vinylAngle)
        ctx.translateBy(x: -c.x, y: -c.y)

        for side in [CGFloat(0), CGFloat(180)] {
            let sweep = NSBezierPath()
            sweep.appendArc(withCenter: c, radius: R * 0.72,
                            startAngle: side - 22, endAngle: side + 22)
            sweep.lineWidth = R * 0.52
            pal.accent.withAlphaComponent(alpha * 0.05).setStroke()
            sweep.stroke()
        }

        let labelR = R * 0.345
        if let img = image(index) {
            NSGraphicsContext.saveGraphicsState()
            circle(labelR).addClip()
            let side = labelR * 2
            img.draw(in: NSRect(x: c.x - labelR, y: c.y - labelR, width: side, height: side),
                     from: .zero, operation: .sourceOver, fraction: alpha)
            NSGraphicsContext.restoreGraphicsState()
        } else {
            pal.console.withAlphaComponent(alpha).setFill()
            circle(labelR).fill()
        }
        let ring = circle(labelR)
        ring.lineWidth = R * 0.016
        pal.accent.withAlphaComponent(alpha * 0.85).setStroke()
        ring.stroke()

        pal.metal.withAlphaComponent(alpha).setFill()
        circle(R * 0.030).fill()
        NSColor(white: 0.02, alpha: alpha).setFill()
        circle(R * 0.020).fill()
        ctx.restoreGState()

        let edge = circle(R)
        edge.lineWidth = 1.5
        NSColor(white: 0.35, alpha: alpha * 0.5).setStroke()
        edge.stroke()
    }

    // MARK: tonearm

    private func drawTonearm(centre c: NSPoint, radius R: CGFloat, _ pal: ArtPalette) {
        let pivot = NSPoint(x: c.x + R * 1.45, y: c.y + R * 0.75)
        let armLength = R * 1.35

        // Real tonearms cross a modest band: lead-in groove to run-out, not edge
        // to spindle. Overstate it and the arm looks like it's racing.
        let playing = R * (0.94 - 0.44 * playProgress)
        let parked = R * 1.24
        let tipRadius = playing + (parked - playing) * armLift

        let dx = c.x - pivot.x, dy = c.y - pivot.y
        let d = max(0.001, hypot(dx, dy))
        let cosA = (armLength * armLength + d * d - tipRadius * tipRadius) / (2 * armLength * d)
        let a = acos(max(-1, min(1, cosA)))
        let angle = atan2(dy, dx) - a
        let tip = NSPoint(x: pivot.x + cos(angle) * armLength,
                          y: pivot.y + sin(angle) * armLength)

        let drop = armLift * R * 0.05
        let shadowPath = NSBezierPath()
        shadowPath.move(to: NSPoint(x: pivot.x, y: pivot.y - drop - 3))
        shadowPath.line(to: NSPoint(x: tip.x, y: tip.y - drop - 3))
        shadowPath.lineWidth = R * 0.045
        shadowPath.lineCapStyle = .round
        NSColor(white: 0, alpha: 0.35 - armLift * 0.15).setStroke()
        shadowPath.stroke()

        let back = NSPoint(x: pivot.x - cos(angle) * R * 0.34,
                           y: pivot.y - sin(angle) * R * 0.34)
        let cw = NSBezierPath()
        cw.move(to: pivot); cw.line(to: back)
        cw.lineWidth = R * 0.115
        cw.lineCapStyle = .round
        pal.metal.withBrightness(0.45).setStroke()
        cw.stroke()

        let tube = NSBezierPath()
        tube.move(to: pivot); tube.line(to: tip)
        tube.lineWidth = R * 0.040
        tube.lineCapStyle = .round
        pal.metal.withBrightness(0.45).setStroke()
        tube.stroke()
        tube.lineWidth = R * 0.018
        pal.metal.setStroke()
        tube.stroke()

        let hub = NSBezierPath(ovalIn: NSRect(x: pivot.x - R * 0.10, y: pivot.y - R * 0.10,
                                              width: R * 0.20, height: R * 0.20))
        NSGradient(starting: pal.metal, ending: pal.metal.withBrightness(0.35))?
            .draw(in: hub, angle: -60)
        pal.deep.withAlphaComponent(0.7).setStroke()
        hub.lineWidth = 1.5
        hub.stroke()

        let ctx = NSGraphicsContext.current!.cgContext
        ctx.saveGState()
        ctx.translateBy(x: tip.x, y: tip.y)
        ctx.rotate(by: angle)
        let shell = NSBezierPath(roundedRect: NSRect(x: -R * 0.10, y: -R * 0.055,
                                                     width: R * 0.17, height: R * 0.11),
                                 xRadius: R * 0.02, yRadius: R * 0.02)
        pal.deep.withBrightness(0.08).setFill(); shell.fill()
        pal.accent.setStroke()
        shell.lineWidth = 1.4
        shell.stroke()
        ctx.restoreGState()
    }

    // MARK: record info

    /// Typography in the record's own colours. Everything is measured first and
    /// the block centred, so nothing can run off the bottom of the screen.
    private func drawRecordInfo(_ pal: ArtPalette) {
        guard settings.showTrackLabel, albums.indices.contains(vinylIndex) else { return }
        let W = bounds.width, H = bounds.height
        let live = liveVinylIndex != nil && !nowPlaying.track.isEmpty
        let album = albums[vinylIndex]

        let kicker = live ? "NOW PLAYING" : "ON THE TURNTABLE"
        let title = live ? nowPlaying.track : album.name
        let artist = live ? nowPlaying.artist : album.artist
        let sub = live ? album.name : ""

        let x = W * 0.585
        let colW = W * 0.355

        // The record being played, propped up crooked behind its own credits.
        let sleeveSide = min(H * 0.62, W * 0.42)
        let sleeveRect = NSRect(x: x + colW * 0.5 - sleeveSide * 0.52,
                                y: H * 0.5 - sleeveSide / 2,
                                width: sleeveSide, height: sleeveSide)
        let ctx = NSGraphicsContext.current!.cgContext
        ctx.saveGState()
        ctx.translateBy(x: sleeveRect.midX, y: sleeveRect.midY)
        ctx.rotate(by: -0.075)
        ctx.translateBy(x: -sleeveRect.midX, y: -sleeveRect.midY)
        drawCover(vinylIndex, in: sleeveRect, alpha: 1, radius: 2, shadowAlpha: 0.7)
        ctx.restoreGState()

        // Scrim over the whole right side: darkens the propped sleeve enough for
        // the type to sit on it, and still fades the table out behind.
        if let scrim = NSGradient(colors: [pal.deep.withAlphaComponent(0),
                                           pal.deep.withAlphaComponent(0.93)]) {
            scrim.draw(in: NSBezierPath(rect: NSRect(x: W * 0.46, y: 0,
                                                     width: W * 0.54, height: H)),
                       angle: 0)
        }
        pal.deep.withAlphaComponent(0.35).setFill()
        NSBezierPath(rect: NSRect(x: W * 0.46, y: 0, width: W * 0.54, height: H)).fill()

        let shadow = NSShadow()
        shadow.shadowColor = NSColor(white: 0, alpha: 0.8)
        shadow.shadowBlurRadius = 12
        shadow.shadowOffset = NSSize(width: 0, height: -2)

        let kickerFont = retroFont(max(10, H * 0.017), bold: true)
        let titleFont = fittedFont(title, width: colW, maxHeight: H * 0.22,
                                   base: max(20, H * 0.050), bold: true)
        let artistFont = fittedFont(artist, width: colW, maxHeight: H * 0.10,
                                    base: max(14, H * 0.029), bold: false)
        let subFont = retroFont(max(11, H * 0.020))

        // Measure the whole block first so it can be centred rather than clipped.
        let gap = H * 0.022
        var total = measure(kicker, font: kickerFont, width: colW, kern: 3)
                  + gap * 1.4
                  + measure(title, font: titleFont, width: colW, kern: 0)
                  + gap * 0.8
                  + measure(artist, font: artistFont, width: colW, kern: 0)
        if !sub.isEmpty { total += gap * 0.5 + measure(sub, font: subFont, width: colW, kern: 0) }

        var top = H * 0.5 + total / 2

        top -= drawText(kicker, x: x, top: top, width: colW, font: kickerFont,
                        color: pal.accent, kern: 3, shadow: shadow) + gap * 1.4
        top -= drawText(title, x: x, top: top, width: colW, font: titleFont,
                        color: pal.text, kern: 0, shadow: shadow) + gap * 0.8
        top -= drawText(artist, x: x, top: top, width: colW, font: artistFont,
                        color: pal.accent, kern: 0, shadow: shadow)
        if !sub.isEmpty {
            top -= gap * 0.5
            drawText(sub, x: x, top: top, width: colW, font: subFont,
                     color: pal.textMuted, kern: 0, shadow: shadow)
        }
    }
}

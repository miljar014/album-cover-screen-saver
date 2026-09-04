import ScreenSaver
import AppKit

private func hand(_ size: CGFloat) -> NSFont {
    NSFont(name: "Bradley Hand", size: size)
        ?? NSFont(name: "Marker Felt", size: size)
        ?? .systemFont(ofSize: size, weight: .medium)
}

extension AlbumCoverSaverView {

    /// The board is pinned once, when the screen saver starts. After that the
    /// only thing that changes is that the record which just finished gets pinned
    /// up alongside the rest, and the new one takes the middle.
    func buildPolaroidBoard() {
        claimTable(for: .polaroid)
        guard !albums.isEmpty else { return }
        let cap = polaroidCap
        guard cap > 0 else { table.removeAll(); return }
        guard table.isEmpty else { return }

        // Skip whatever is playing — that one belongs in the centre, not the wall.
        let centre = liveAlbumIndex ?? featIndex
        for album in albums.prefix(cap + 1)
            where !(albums.indices.contains(centre) && albums[centre].id == album.id) {
            if table.count >= cap { break }
            pinToTable(album.id, spot: polaroidSpot())
        }
    }

    private var polaroidCap: Int {
        isPreview ? 4 : max(0, min(24, settings.polaroidCount))
    }

    /// Keeps clear of the middle, where the current photo hangs.
    private func polaroidSpot() -> (x: CGFloat, y: CGFloat, size: CGFloat)? {
        let W = bounds.width, H = bounds.height
        return findTableSpot(sizeRange: 0.17...0.26, scale: 1,
                             keepOutCentre: NSPoint(x: W * 0.5, y: H * 0.5),
                             keepOutRadius: min(H * 0.36, W * 0.26) * 0.82,
                             keepOutRect: nil)
    }

    func advancePolaroid() {
        advanceFeatured(idleSpan: settings.featureSeconds)
        if table.isEmpty { buildPolaroidBoard() }

        // Track the centre by album id straight off the playback signal, not off
        // the featured index. `advanceFeatured` freezes that index for 1.1s while
        // the swap animates, so a quick skip during that window used to vanish
        // without ever being pinned.
        let currentID: String?
        if nowPlaying.isLive, !nowPlaying.albumID.isEmpty {
            currentID = nowPlaying.albumID
        } else if albums.indices.contains(featIndex) {
            currentID = albums[featIndex].id
        } else {
            currentID = nil
        }
        guard let currentID else { return }

        if let previous = lastCentreAlbumID, previous != currentID {
            pinToTable(previous, spot: polaroidSpot())
            while table.count > polaroidCap { table.removeFirst() }
        }
        lastCentreAlbumID = currentID
    }

    /// Instant photos pinned to cork, swaying very slightly on their pins.
    func drawPolaroid() {
        let W = bounds.width, H = bounds.height
        let pal = featPalette

        // Wooden frame, then the cork inside it.
        let frameW = min(W, H) * 0.028
        NSGradient(starting: NSColor(red: 0.42, green: 0.27, blue: 0.14, alpha: 1),
                   ending: NSColor(red: 0.26, green: 0.16, blue: 0.08, alpha: 1))?
            .draw(in: NSBezierPath(rect: bounds), angle: -70)
        for i in 0..<40 {                       // frame grain
            let y = CGFloat(i) / 40 * H
            NSColor(white: 0, alpha: 0.05 + CGFloat(i % 3) * 0.02).setStroke()
            let l = NSBezierPath()
            l.move(to: NSPoint(x: 0, y: y)); l.line(to: NSPoint(x: W, y: y))
            l.lineWidth = 1.5
            l.stroke()
        }

        let board = bounds.insetBy(dx: frameW, dy: frameW)
        NSGraphicsContext.saveGraphicsState()
        NSBezierPath(rect: board).addClip()
        CorkTexture.image(size: board.size)
            .draw(in: board, from: .zero, operation: .sourceOver, fraction: 1)
        // The frame casts a shadow onto the cork, which is what gives the board
        // depth rather than looking like a printed border.
        for edge in 0..<4 {
            let d = frameW * 0.9
            let r: NSRect
            let angle: CGFloat
            switch edge {
            case 0: r = NSRect(x: board.minX, y: board.maxY - d, width: board.width, height: d); angle = -90
            case 1: r = NSRect(x: board.minX, y: board.minY, width: board.width, height: d); angle = 90
            case 2: r = NSRect(x: board.minX, y: board.minY, width: d, height: board.height); angle = 0
            default: r = NSRect(x: board.maxX - d, y: board.minY, width: d, height: board.height); angle = 180
            }
            NSGradient(colors: [NSColor(white: 0, alpha: 0.30), NSColor(white: 0, alpha: 0)])?
                .draw(in: NSBezierPath(rect: r), angle: angle)
        }
        NSGraphicsContext.restoreGraphicsState()

        let lip = NSBezierPath(rect: board)
        lip.lineWidth = 2
        NSColor(white: 0, alpha: 0.35).setStroke()
        lip.stroke()

        let W2 = bounds.width, H2 = bounds.height
        for (i, t) in table.enumerated() {
            guard let idx = albumIndexByID[t.albumID] else { continue }
            // Newly pinned photos settle in rather than appearing instantly.
            let age = phase - t.addedAt
            let k = age < 0.6 ? Ease.back(CGFloat(age / 0.6)) : 1
            let s = Sleeve(centre: NSPoint(x: t.nx * W2, y: t.ny * H2 + (1 - k) * H2 * 0.025),
                           side: t.sizeFrac * H2 * (1.2 - 0.2 * k),
                           angle: t.angle, index: idx, discPeek: 0, discDirection: 0)
            let ctx = NSGraphicsContext.current!.cgContext
            ctx.saveGState()
            ctx.setAlpha(k)
            drawPhoto(s, i: i, big: false, pal: pal)
            ctx.restoreGState()
        }

        guard albums.indices.contains(featIndex) else { return }
        // The current album gets the big photo, front and centre.
        let side = min(H * 0.36, W * 0.26)
        drawPhoto(Sleeve(centre: NSPoint(x: W * 0.5, y: H * 0.5), side: side,
                         angle: -0.03, index: featIndex, discPeek: 0, discDirection: 0),
                  i: 999, big: true, pal: pal)
    }

    private func drawPhoto(_ s: Sleeve, i: Int, big: Bool, pal: ArtPalette) {
        guard albums.indices.contains(s.index) else { return }
        let ctx = NSGraphicsContext.current!.cgContext
        // Photos hang from a single pin, so they rock rather than slide.
        let sway = CGFloat(sin(Double(phase) * 0.6 + Double(i) * 1.7)) * 0.012
        let w = s.side
        let border = w * 0.055
        let h = w + border * 4.4          // instant film's deep bottom margin

        ctx.saveGState()
        ctx.translateBy(x: s.centre.x, y: s.centre.y + h * 0.42)
        ctx.rotate(by: s.angle + sway)
        ctx.translateBy(x: 0, y: -h * 0.42)

        let card = NSRect(x: -w / 2 - border, y: -h / 2, width: w + border * 2, height: h)
        NSGraphicsContext.saveGraphicsState()
        let sh = NSShadow()
        sh.shadowColor = NSColor(white: 0, alpha: 0.55)
        sh.shadowBlurRadius = w * 0.11
        sh.shadowOffset = NSSize(width: 0, height: -w * 0.035)
        sh.set()
        NSColor(red: 0.97, green: 0.96, blue: 0.93, alpha: 1).setFill()
        NSBezierPath(rect: card).fill()
        NSGraphicsContext.restoreGraphicsState()

        let photo = NSRect(x: -w / 2, y: card.maxY - border - w, width: w, height: w)
        drawCover(s.index, in: photo)
        if !big {
            pal.deep.withAlphaComponent(0.20).setFill()
            NSBezierPath(rect: photo).fill()
        }

        let album = albums[s.index]
        let caption = big ? featText.title : album.name
        let ps = NSMutableParagraphStyle()
        ps.alignment = .center
        ps.lineBreakMode = .byTruncatingTail
        (caption as NSString).draw(
            in: NSRect(x: card.minX + 4, y: card.minY + h * 0.045,
                       width: card.width - 8, height: h * 0.11),
            withAttributes: [.font: hand(max(9, w * 0.10)),
                             .foregroundColor: NSColor(red: 0.18, green: 0.20, blue: 0.32, alpha: 1),
                             .paragraphStyle: ps])

        // Pin.
        let pinR = w * 0.045
        let pin = NSPoint(x: 0, y: card.maxY - pinR * 1.6)
        NSColor(white: 0, alpha: 0.3).setFill()
        NSBezierPath(ovalIn: NSRect(x: pin.x - pinR * 0.8, y: pin.y - pinR * 1.2,
                                    width: pinR * 1.6, height: pinR * 1.6)).fill()
        NSGradient(starting: pal.accent, ending: pal.accent.withBrightness(0.35))?
            .draw(in: NSBezierPath(ovalIn: NSRect(x: pin.x - pinR, y: pin.y - pinR,
                                                  width: pinR * 2, height: pinR * 2)),
                  angle: -60)
        NSColor(white: 1, alpha: 0.55).setFill()
        NSBezierPath(ovalIn: NSRect(x: pin.x - pinR * 0.35, y: pin.y + pinR * 0.05,
                                    width: pinR * 0.5, height: pinR * 0.5)).fill()
        ctx.restoreGState()
    }
}

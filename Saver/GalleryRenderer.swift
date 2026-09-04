import ScreenSaver
import AppKit

extension AlbumCoverSaverView {

    /// Framed covers on a gallery wall, with a spotlight drifting across them.
    func drawGallery() {
        let W = bounds.width, H = bounds.height
        let pal = featPalette

        NSGradient(starting: pal.console.withBrightness(0.26),
                   ending: pal.deep.withBrightness(0.07))?
            .draw(in: NSBezierPath(rect: bounds), angle: -90)

        let cols = max(2, min(7, settings.galleryColumns))
        let rows = 2
        let slots = cols * rows

        if galleryItems.count != slots {
            var used = Set<Int>()
            galleryItems = (0..<slots).map { _ in
                let i = pickIndex(avoiding: used); used.insert(i); return i
            }
        }
        // The featured record always hangs in the middle of the wall.
        let hero = slots / 2
        if albums.indices.contains(featIndex) { galleryItems[hero] = featIndex }

        let marginX = W * 0.07
        let cellW = (W - marginX * 2) / CGFloat(cols)
        let cellH = H * 0.36
        let topY = H * 0.60

        // Spotlight tracks slowly across the wall; the featured frame is where it
        // lingers, so live playback is always the brightest thing on screen.
        let roam = (sin(Double(spotPhase)) + 1) / 2
        let heroX = marginX + (CGFloat(hero % cols) + 0.5) * cellW
        let spotX = heroX * 0.6 + CGFloat(roam) * W * 0.4
        let spotY = H * 0.55

        NSGradient(colors: [NSColor(white: 1, alpha: 0.085), NSColor(white: 1, alpha: 0)])?
            .draw(fromCenter: NSPoint(x: spotX, y: spotY), radius: 0,
                  toCenter: NSPoint(x: spotX, y: spotY), radius: min(W, H) * 0.55, options: [])

        // Picture rail.
        pal.accent.withAlphaComponent(0.30).setFill()
        NSBezierPath(rect: NSRect(x: 0, y: topY + cellH * 0.06, width: W, height: 2)).fill()

        for slot in 0..<slots {
            let r = slot / cols, c = slot % cols
            let cx = marginX + (CGFloat(c) + 0.5) * cellW
            let cy = topY - CGFloat(r) * cellH - cellH * 0.42

            let art = min(cellW * 0.62, cellH * 0.52)
            let mat = art * 1.26
            let frameRect = NSRect(x: cx - mat / 2, y: cy - mat / 2, width: mat, height: mat)
            let artRect = NSRect(x: cx - art / 2, y: cy - art / 2, width: art, height: art)

            // Distance from the spotlight decides how lit each frame is.
            let dist = hypot(cx - spotX, cy - spotY) / (min(W, H) * 0.55)
            let lit = max(0.30, 1 - dist * 0.85)
            let isHero = slot == hero

            NSGraphicsContext.saveGraphicsState()
            let sh = NSShadow()
            sh.shadowColor = NSColor(white: 0, alpha: 0.6)
            sh.shadowBlurRadius = mat * 0.12
            sh.shadowOffset = NSSize(width: 0, height: -mat * 0.035)
            sh.set()
            (isHero ? pal.accent : pal.metal).withBrightness(isHero ? 0.55 : 0.40).setFill()
            NSBezierPath(rect: frameRect).fill()
            NSGraphicsContext.restoreGraphicsState()

            // Mat board.
            pal.deep.withBrightness(0.88, saturation: 0.15).setFill()
            NSBezierPath(rect: frameRect.insetBy(dx: mat * 0.035, dy: mat * 0.035)).fill()

            drawCover(galleryItems[slot], in: artRect, alpha: 1)
            NSColor(white: 0, alpha: 1 - lit).setFill()
            NSBezierPath(rect: artRect).fill()

            // Placard.
            guard settings.showTrackLabel, albums.indices.contains(galleryItems[slot]) else { continue }
            let a = albums[galleryItems[slot]]
            let live = isHero && liveAlbumIndex == featIndex && !nowPlaying.track.isEmpty
            let name = live ? nowPlaying.track : a.name
            let plaqueTop = frameRect.minY - cellH * 0.045
            let pw = cellW * 0.86
            let px = cx - pw / 2
            var t = plaqueTop
            t -= drawText(name, x: px, top: t, width: pw,
                          font: retroFont(max(9, H * 0.0165), bold: true),
                          color: pal.text.withAlphaComponent(0.35 + 0.65 * lit),
                          centred: true)
            drawText(live ? nowPlaying.artist : a.artist, x: px, top: t - 2, width: pw,
                     font: retroFont(max(8, H * 0.014)),
                     color: pal.accent.withAlphaComponent(0.35 + 0.65 * lit),
                     centred: true)
        }
    }
}

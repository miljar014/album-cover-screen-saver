import ScreenSaver
import AppKit

extension AlbumCoverSaverView {

    /// The cover suspended in a slowly drifting field of its own colours.
    func drawAmbient() {
        let W = bounds.width, H = bounds.height
        let pal = featPalette

        pal.deep.setFill()
        bounds.fill()

        // Four soft colour masses, each drifting on its own period so the field
        // never visibly repeats.
        let t = Double(phase) * 0.05 * max(0.05, settings.ambientMotion)
        let blobs: [(NSColor, CGFloat, CGFloat, CGFloat)] = [
            (pal.accent,                       0.30, 0.62, 0.62),
            (pal.consoleLight,                 0.72, 0.40, 0.70),
            (pal.console,                      0.50, 0.20, 0.58),
            (pal.accent.withBrightness(0.55),  0.86, 0.78, 0.50),
        ]
        for (i, blob) in blobs.enumerated() {
            let (colour, bx, by, br) = blob
            let f = Double(i + 1)
            let cx = W * (bx + 0.11 * CGFloat(sin(t * f * 0.73 + f)))
            let cy = H * (by + 0.11 * CGFloat(cos(t * f * 0.51 + f * 2)))
            let r = min(W, H) * br
            NSGradient(colors: [colour.withAlphaComponent(0.50),
                                colour.withAlphaComponent(0)])?
                .draw(fromCenter: NSPoint(x: cx, y: cy), radius: 0,
                      toCenter: NSPoint(x: cx, y: cy), radius: r, options: [])
        }

        guard albums.indices.contains(featIndex) else { return }

        let side = min(H * 0.42, W * 0.32)
        let bob = CGFloat(sin(Double(phase) * 0.45)) * H * 0.007
        let cy = H * 0.585 + bob
        let rect = NSRect(x: W * 0.5 - side / 2, y: cy - side / 2, width: side, height: side)

        // Reflection first, so the cover prints over its top edge.
        let refl = NSRect(x: rect.minX, y: rect.minY - side * 0.62 - H * 0.012,
                          width: side, height: side * 0.62)
        NSGraphicsContext.saveGraphicsState()
        NSBezierPath(rect: refl).addClip()
        let ctx = NSGraphicsContext.current!.cgContext
        ctx.translateBy(x: 0, y: refl.maxY + refl.minY)
        ctx.scaleBy(x: 1, y: -1)
        drawCover(featIndex, in: NSRect(x: rect.minX, y: refl.minY,
                                        width: side, height: side), alpha: 0.22, radius: 6)
        NSGraphicsContext.restoreGraphicsState()
        NSGradient(colors: [pal.deep.withAlphaComponent(0), pal.deep.withAlphaComponent(0.95)])?
            .draw(in: NSBezierPath(rect: refl), angle: -90)

        drawFeaturedCover(in: rect, radius: 8, shadowAlpha: 0.6)

        guard settings.showTrackLabel else { return }
        let text = featText
        let colW = W * 0.56
        let x = W * 0.5 - colW / 2
        let shadow = softShadow(0.7, blur: 16)
        var top = rect.minY - H * 0.055

        let titleFont = fittedFont(text.title, width: colW, maxHeight: H * 0.13,
                                   base: max(20, H * 0.052), bold: true)
        top -= drawText(text.title, x: x, top: top, width: colW, font: titleFont,
                        color: pal.text, shadow: shadow, centred: true) + H * 0.018
        top -= drawText(text.artist, x: x, top: top, width: colW,
                        font: retroFont(max(14, H * 0.030)),
                        color: pal.accent, shadow: shadow, centred: true)
        if !text.sub.isEmpty {
            top -= H * 0.010
            drawText(text.sub, x: x, top: top, width: colW,
                     font: retroFont(max(11, H * 0.021)),
                     color: pal.textMuted, shadow: shadow, centred: true)
        }
    }
}

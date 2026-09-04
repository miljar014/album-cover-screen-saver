import AppKit

/// A procedurally generated cork surface.
///
/// Rendered once into an image and cached: cork needs thousands of granules to
/// read as cork rather than as noise, and drawing that many shapes every frame
/// would cost far more than the whole rest of the scene.
enum CorkTexture {
    private static var cache: NSImage?
    private static var cachedSize: NSSize = .zero

    static func purge() { cache = nil; cachedSize = .zero }

    static func image(size: NSSize) -> NSImage {
        if let hit = cache,
           abs(cachedSize.width - size.width) < 2,
           abs(cachedSize.height - size.height) < 2 { return hit }

        let W = max(64, size.width), H = max(64, size.height)
        let img = NSImage(size: NSSize(width: W, height: H))
        img.lockFocus()

        let rect = NSRect(x: 0, y: 0, width: W, height: H)
        NSGradient(starting: NSColor(red: 0.71, green: 0.55, blue: 0.33, alpha: 1),
                   ending: NSColor(red: 0.56, green: 0.41, blue: 0.23, alpha: 1))?
            .draw(in: NSBezierPath(rect: rect), angle: -80)

        // Broad tonal mottling. Real cork is blotchy at a much larger scale than
        // its granules, and without this it reads as flat regardless of detail.
        for _ in 0..<70 {
            let r = CGFloat.random(in: min(W, H) * 0.07 ... min(W, H) * 0.30)
            let c = NSPoint(x: .random(in: 0...W), y: .random(in: 0...H))
            let light = Bool.random()
            let tone = light
                ? NSColor(red: 0.85, green: 0.70, blue: 0.46, alpha: 1)
                : NSColor(red: 0.36, green: 0.24, blue: 0.12, alpha: 1)
            NSGradient(colors: [tone.withAlphaComponent(light ? 0.10 : 0.13),
                                tone.withAlphaComponent(0)])?
                .draw(fromCenter: c, radius: 0, toCenter: c, radius: r, options: [])
        }

        // Granules: irregular, varied in tone, at cork's actual density.
        let granules = Int(W * H / 420)
        for _ in 0..<granules {
            let w = CGFloat.random(in: 2.5...11)
            let h = w * CGFloat.random(in: 0.45...1.6)
            let x = CGFloat.random(in: -w...W)
            let y = CGFloat.random(in: -h...H)
            let t = CGFloat.random(in: 0...1)
            let colour = NSColor(red: 0.42 + t * 0.42,
                                 green: 0.30 + t * 0.36,
                                 blue: 0.15 + t * 0.26,
                                 alpha: CGFloat.random(in: 0.10...0.34))
            colour.setFill()
            NSBezierPath(ovalIn: NSRect(x: x, y: y, width: w, height: h)).fill()
        }

        // Pits and pale flecks — the high-contrast specks the eye uses to judge
        // that a surface is granular rather than printed.
        for _ in 0..<(granules / 7) {
            let r = CGFloat.random(in: 1...3.4)
            NSColor(red: 0.20, green: 0.13, blue: 0.06,
                    alpha: CGFloat.random(in: 0.25...0.55)).setFill()
            NSBezierPath(ovalIn: NSRect(x: .random(in: 0...W), y: .random(in: 0...H),
                                        width: r, height: r * .random(in: 0.7...1.4))).fill()
        }
        for _ in 0..<(granules / 9) {
            let r = CGFloat.random(in: 1...3)
            NSColor(red: 0.93, green: 0.83, blue: 0.62,
                    alpha: CGFloat.random(in: 0.14...0.36)).setFill()
            NSBezierPath(ovalIn: NSRect(x: .random(in: 0...W), y: .random(in: 0...H),
                                        width: r, height: r)).fill()
        }

        img.unlockFocus()
        cache = img
        cachedSize = NSSize(width: W, height: H)
        return img
    }
}

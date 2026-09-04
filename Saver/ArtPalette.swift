import AppKit

extension NSColor {
    /// WCAG relative luminance — the basis for a real contrast check rather than
    /// eyeballing whether text will be readable.
    var relLuminance: CGFloat {
        guard let c = usingColorSpace(.sRGB) else { return 0 }
        func f(_ v: CGFloat) -> CGFloat {
            v <= 0.03928 ? v / 12.92 : pow((v + 0.055) / 1.055, 2.4)
        }
        return 0.2126 * f(c.redComponent) + 0.7152 * f(c.greenComponent)
             + 0.0722 * f(c.blueComponent)
    }

    func contrast(against other: NSColor) -> CGFloat {
        let a = relLuminance, b = other.relLuminance
        return (max(a, b) + 0.05) / (min(a, b) + 0.05)
    }

    func withBrightness(_ b: CGFloat, saturation sMul: CGFloat = 1) -> NSColor {
        guard let c = usingColorSpace(.sRGB) else { return self }
        return NSColor(hue: c.hueComponent,
                       saturation: min(1, c.saturationComponent * sMul),
                       brightness: max(0, min(1, b)), alpha: 1)
    }

    /// Nudges brightness (then saturation) until this colour is legible on `bg`.
    /// Keeps the hue, so it still reads as the album's colour.
    func readable(on bg: NSColor, target: CGFloat = 4.5) -> NSColor {
        guard let c = usingColorSpace(.sRGB) else { return self }
        var s = c.saturationComponent
        var b = c.brightnessComponent
        let h = c.hueComponent
        let lighten = bg.relLuminance < 0.22
        var out = self
        for _ in 0..<26 {
            if out.contrast(against: bg) >= target { return out }
            b = lighten ? min(1, b + 0.045) : max(0, b - 0.045)
            if lighten && b > 0.995 { s = max(0, s - 0.07) }   // out of headroom: toward white
            if !lighten && b < 0.005 { s = max(0, s - 0.07) }  // toward black
            out = NSColor(hue: h, saturation: s, brightness: b, alpha: 1)
        }
        return out
    }
}

/// A whole visual theme derived from one album cover, so the turntable, the
/// typography and the lighting all belong to the record being played.
struct ArtPalette {
    var accent: NSColor        // the cover's colour, guaranteed legible
    var deep: NSColor          // near-black, tinted — the room
    var console: NSColor       // the plinth
    var consoleLight: NSColor
    var metal: NSColor         // platter rim, tonearm
    var text: NSColor
    var textMuted: NSColor
    /// A handful of the cover's own colours, lifted to read as lit objects.
    var swatches: [NSColor]

    static let fallback = ArtPalette(
        accent: NSColor(red: 0.85, green: 0.63, blue: 0.17, alpha: 1),
        deep: NSColor(red: 0.09, green: 0.06, blue: 0.04, alpha: 1),
        console: NSColor(red: 0.34, green: 0.21, blue: 0.12, alpha: 1),
        consoleLight: NSColor(red: 0.48, green: 0.31, blue: 0.18, alpha: 1),
        metal: NSColor(red: 0.78, green: 0.77, blue: 0.75, alpha: 1),
        text: NSColor(white: 0.97, alpha: 1),
        textMuted: NSColor(white: 0.72, alpha: 1),
        swatches: [NSColor(red: 0.95, green: 0.72, blue: 0.25, alpha: 1),
                   NSColor(red: 0.90, green: 0.40, blue: 0.30, alpha: 1),
                   NSColor(red: 0.45, green: 0.80, blue: 0.85, alpha: 1),
                   NSColor(red: 0.70, green: 0.55, blue: 0.90, alpha: 1),
                   NSColor(red: 0.55, green: 0.85, blue: 0.55, alpha: 1)])
}

enum PaletteStore {
    private static var cache: [String: ArtPalette] = [:]
    static func purge() { cache.removeAll() }

    static func palette(for albumID: String, image: NSImage?) -> ArtPalette {
        if let p = cache[albumID] { return p }
        let p = build(from: image) ?? .fallback
        cache[albumID] = p
        return p
    }

    private static func build(from image: NSImage?) -> ArtPalette? {
        guard let image else { return nil }
        let n = 28
        guard let rep = NSBitmapImageRep(
            bitmapDataPlanes: nil, pixelsWide: n, pixelsHigh: n,
            bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
            colorSpaceName: .deviceRGB, bytesPerRow: n * 4, bitsPerPixel: 32)
        else { return nil }

        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
        image.draw(in: NSRect(x: 0, y: 0, width: n, height: n))
        NSGraphicsContext.restoreGraphicsState()
        guard let px = rep.bitmapData else { return nil }

        var sumR = 0.0, sumG = 0.0, sumB = 0.0, total = 0.0
        var best: (score: Double, r: Double, g: Double, b: Double) = (-1, 0.5, 0.4, 0.3)
        var buckets: [Int: (n: Int, r: Double, g: Double, b: Double)] = [:]

        for i in stride(from: 0, to: n * n * 4, by: 4) {
            let r = Double(px[i]) / 255, g = Double(px[i + 1]) / 255, b = Double(px[i + 2]) / 255
            sumR += r; sumG += g; sumB += b; total += 1
            let mx = max(r, g, b), mn = min(r, g, b)
            let sat = mx <= 0 ? 0 : (mx - mn) / mx
            // Vivid and mid-bright: what a person would name as "the cover's colour".
            let score = sat * 1.6 + mx * 0.4 - abs(mx - 0.65)
            if score > best.score { best = (score, r, g, b) }

            // Coarse colour buckets, for a spread of the cover's colours rather
            // than only its single most characterful one.
            let key = (Int(r * 4) << 8) | (Int(g * 4) << 4) | Int(b * 4)
            var e = buckets[key] ?? (0, 0, 0, 0)
            e.n += 1; e.r += r; e.g += g; e.b += b
            buckets[key] = e
        }
        guard total > 0 else { return nil }

        let avg = NSColor(red: CGFloat(sumR / total), green: CGFloat(sumG / total),
                          blue: CGFloat(sumB / total), alpha: 1)
        let raw = NSColor(red: CGFloat(best.r), green: CGFloat(best.g),
                          blue: CGFloat(best.b), alpha: 1)

        // The room is always dark: a bright cover shouldn't produce a glaring
        // screen at 3am, and dark keeps the artwork the brightest thing present.
        let deep = avg.withBrightness(0.10, saturation: 0.85)
        let console = avg.withBrightness(0.30, saturation: 0.95)
        let consoleLight = avg.withBrightness(0.44, saturation: 0.9)
        let metal = raw.withBrightness(0.80, saturation: 0.18)

        let text = NSColor.white.readable(on: deep, target: 12)
        let accent = raw.readable(on: deep, target: 5.0)

        // Lifted so they read as lit glass rather than muddy paint. Near-greys
        // are dropped — a grey bubble just looks like a bug.
        var swatches = buckets.values
            .sorted { $0.n > $1.n }
            .map { e -> NSColor in
                NSColor(red: CGFloat(e.r / Double(e.n)), green: CGFloat(e.g / Double(e.n)),
                        blue: CGFloat(e.b / Double(e.n)), alpha: 1)
                    .withBrightness(0.88, saturation: 1.5)
            }
            .filter { ($0.usingColorSpace(.sRGB)?.saturationComponent ?? 0) > 0.16 }
            .prefix(6)
            .map { $0 }
        if swatches.count < 3 { swatches = [accent, accent.withBrightness(0.7),
                                            metal.withBrightness(0.9, saturation: 3)] }

        return ArtPalette(accent: accent, deep: deep,
                          console: console, consoleLight: consoleLight,
                          metal: metal, text: text,
                          textMuted: text.withAlphaComponent(0.62),
                          swatches: swatches)
    }
}

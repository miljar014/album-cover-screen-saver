import ScreenSaver
import AppKit

/// Album art reduced to a luminance grid, so it can be redrawn as phosphor dots.
enum HalftoneStore {
    static let n = 46
    private static var cache: [String: [Double]] = [:]
    static func purge() { cache.removeAll() }

    static func grid(for id: String, image: NSImage?) -> [Double] {
        if let g = cache[id] { return g }
        var out = [Double](repeating: 0, count: n * n)
        if let image,
           let rep = NSBitmapImageRep(
               bitmapDataPlanes: nil, pixelsWide: n, pixelsHigh: n,
               bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
               colorSpaceName: .deviceRGB, bytesPerRow: n * 4, bitsPerPixel: 32) {
            NSGraphicsContext.saveGraphicsState()
            NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
            image.draw(in: NSRect(x: 0, y: 0, width: n, height: n))
            NSGraphicsContext.restoreGraphicsState()
            if let px = rep.bitmapData {
                for i in 0..<(n * n) {
                    let o = i * 4
                    out[i] = (0.2126 * Double(px[o]) + 0.7152 * Double(px[o + 1])
                              + 0.0722 * Double(px[o + 2])) / 255
                }
            }
        }
        cache[id] = out
        return out
    }
}

extension AlbumCoverSaverView {

    private var phosphor: NSColor {
        settings.crtAmber
            ? NSColor(red: 1.00, green: 0.72, blue: 0.26, alpha: 1)
            : NSColor(red: 0.38, green: 1.00, blue: 0.48, alpha: 1)
    }

    /// A phosphor terminal. The cover is rendered as halftone dots rather than
    /// pasted in, because a full-colour photo would break the illusion instantly.
    func drawCRT() {
        let W = bounds.width, H = bounds.height
        let green = phosphor

        NSColor(red: 0.02, green: 0.03, blue: 0.02, alpha: 1).setFill()
        bounds.fill()
        NSGradient(colors: [green.withAlphaComponent(0.05),
                            NSColor.clear])?
            .draw(fromCenter: NSPoint(x: W * 0.5, y: H * 0.5), radius: 0,
                  toCenter: NSPoint(x: W * 0.5, y: H * 0.5), radius: min(W, H) * 0.8,
                  options: [])

        guard albums.indices.contains(featIndex) else { return }

        // Halftone portrait.
        let n = HalftoneStore.n
        let grid = HalftoneStore.grid(for: albums[featIndex].id, image: image(featIndex))
        let side = min(H * 0.52, W * 0.34)
        let cell = side / CGFloat(n)
        let ox = W * 0.10
        let oy = H * 0.5 - side / 2
        let fade = featFade

        for r in 0..<n {
            for c in 0..<n {
                let v = grid[r * n + c]
                guard v > 0.06 else { continue }
                // Row 0 is the TOP of the cover, but y increases upward here, so
                // the row index has to be flipped or the image comes out inverted.
                let x = ox + CGFloat(c) * cell
                let y = oy + CGFloat(n - 1 - r) * cell
                let d = cell * CGFloat(0.30 + v * 0.78)
                green.withAlphaComponent(CGFloat(0.25 + v * 0.75) * fade).setFill()
                NSBezierPath(ovalIn: NSRect(x: x + (cell - d) / 2, y: y + (cell - d) / 2,
                                            width: d, height: d)).fill()
            }
        }

        // Readout, typed out a character at a time.
        let text = featText
        let mono = NSFont.monospacedSystemFont(ofSize: max(11, H * 0.026), weight: .medium)
        let monoSmall = NSFont.monospacedSystemFont(ofSize: max(9, H * 0.020), weight: .regular)
        let x = ox + side + W * 0.06
        let colW = W - x - W * 0.07

        var lines: [(String, NSFont, CGFloat)] = [
            ("SPOTIFYCOLLAGE v1.0", monoSmall, 0.45),
            ("READY.", monoSmall, 0.45),
            ("", monoSmall, 0.45),
            (liveAlbumIndex == featIndex ? "> NOW PLAYING" : "> FROM ARCHIVE", monoSmall, 0.8),
            ("", monoSmall, 0.45),
            (text.title.uppercased(), mono, 1.0),
            (text.artist.uppercased(), mono, 0.75),
        ]
        if !text.sub.isEmpty { lines.append((text.sub.uppercased(), monoSmall, 0.55)) }

        // Reveal budget grows with time since this album came up.
        var budget = Int(max(0, phase - featStartedAt) * 38)
        var top = H * 0.5 + CGFloat(lines.count) * H * 0.021
        var finished = true

        for (line, font, bright) in lines {
            let shown: String
            if budget >= line.count {
                shown = line
                budget -= max(1, line.count)
            } else {
                shown = String(line.prefix(max(0, budget)))
                budget = 0
                finished = false
            }
            let h = drawText(shown.isEmpty ? " " : shown, x: x, top: top, width: colW,
                             font: font, color: green.withAlphaComponent(bright))
            if !finished && !shown.isEmpty || (budget == 0 && !finished) {
                // Cursor sits at the end of whatever is still being typed.
                let w = (shown as NSString).size(withAttributes: [.font: font]).width
                green.withAlphaComponent(0.9).setFill()
                NSBezierPath(rect: NSRect(x: x + w + 2, y: top - h + h * 0.18,
                                          width: font.pointSize * 0.55,
                                          height: font.pointSize * 0.9)).fill()
                break
            }
            top -= h + H * 0.010
        }
        if finished, Int(phase * 2) % 2 == 0 {
            green.withAlphaComponent(0.9).setFill()
            NSBezierPath(rect: NSRect(x: x, y: top - mono.pointSize,
                                      width: mono.pointSize * 0.55,
                                      height: mono.pointSize * 0.9)).fill()
        }

        // Scanlines and vignette, applied last over everything.
        NSColor(white: 0, alpha: 0.22).setFill()
        var y: CGFloat = 0
        while y < H {
            NSBezierPath(rect: NSRect(x: 0, y: y, width: W, height: 1.5)).fill()
            y += 4
        }
        NSGradient(colors: [NSColor.clear, NSColor(white: 0, alpha: 0.55)])?
            .draw(fromCenter: NSPoint(x: W * 0.5, y: H * 0.5), radius: min(W, H) * 0.42,
                  toCenter: NSPoint(x: W * 0.5, y: H * 0.5), radius: max(W, H) * 0.78,
                  options: [])
    }
}

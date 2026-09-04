import ScreenSaver
import AppKit

private func serif(_ size: CGFloat, bold: Bool = false) -> NSFont {
    NSFont(name: bold ? "Didot-Bold" : "Didot", size: size)
        ?? NSFont(name: bold ? "TimesNewRomanPS-BoldMT" : "TimesNewRomanPSMT", size: size)
        ?? .systemFont(ofSize: size, weight: bold ? .bold : .regular)
}

extension AlbumCoverSaverView {

    /// The album as a broadsheet front page.
    func drawNewsstand() {
        let W = bounds.width, H = bounds.height
        let paper = NSColor(red: 0.93, green: 0.91, blue: 0.85, alpha: 1)
        let ink = NSColor(red: 0.11, green: 0.10, blue: 0.09, alpha: 1)
        let pal = featPalette

        paper.setFill()
        bounds.fill()
        // Aged blotches, seeded so the paper doesn't crawl.
        for i in 0..<90 {
            let f = frac(sin(Double(i) * 12.9898) * 43758.5453)
            let g = frac(sin(Double(i) * 78.233) * 12345.6789)
            let r = CGFloat(frac(sin(Double(i) * 4.117) * 991.7)) * H * 0.05 + 4
            NSColor(red: 0.75, green: 0.70, blue: 0.58, alpha: 0.05).setFill()
            NSBezierPath(ovalIn: NSRect(x: CGFloat(f) * W - r, y: CGFloat(g) * H - r,
                                        width: r * 2, height: r * 2)).fill()
        }

        guard albums.indices.contains(featIndex) else { return }
        let text = featText
        let m = W * 0.06
        let colW = W - m * 2

        func rule(_ y: CGFloat, _ h: CGFloat) {
            ink.withAlphaComponent(0.85).setFill()
            NSBezierPath(rect: NSRect(x: m, y: y, width: colW, height: h)).fill()
        }

        // Masthead: the artist is the paper.
        var top = H - H * 0.055
        let mast = fittedFont(text.artist.uppercased(), width: colW, maxHeight: H * 0.12,
                              base: H * 0.095, bold: true)
        let ps = NSMutableParagraphStyle(); ps.alignment = .center
        let mh = measure(text.artist.uppercased(), font: mast, width: colW, kern: 2)
        (text.artist.uppercased() as NSString).draw(
            with: NSRect(x: m, y: top - mh, width: colW, height: mh),
            options: [.usesLineFragmentOrigin],
            attributes: [.font: mast, .foregroundColor: ink, .kern: 2, .paragraphStyle: ps])
        top -= mh + H * 0.012

        rule(top, 3); top -= H * 0.016
        let dateline = liveAlbumIndex == featIndex ? "LATE EDITION  ·  NOW PLAYING"
                                                   : "FROM THE ARCHIVE  ·  COLLECTED EDITION"
        (dateline as NSString).draw(
            with: NSRect(x: m, y: top - H * 0.022, width: colW, height: H * 0.022),
            options: [.usesLineFragmentOrigin],
            attributes: [.font: serif(max(9, H * 0.016)), .foregroundColor: ink,
                         .kern: 3, .paragraphStyle: ps])
        top -= H * 0.028
        rule(top, 1); top -= H * 0.030

        // Headline.
        let head = fittedFont(text.title, width: colW, maxHeight: H * 0.17,
                              base: H * 0.075, bold: true)
        top -= drawText(text.title, x: m, top: top, width: colW, font: head,
                        color: ink) + H * 0.020
        rule(top, 1); top -= H * 0.028

        // Lead photograph, halftoned like newsprint.
        let photoW = colW * 0.46
        let photo = NSRect(x: m, y: top - photoW, width: photoW, height: photoW)
        let n = HalftoneStore.n
        let grid = HalftoneStore.grid(for: albums[featIndex].id, image: image(featIndex))
        let cell = photoW / CGFloat(n)
        for r in 0..<n {
            for c in 0..<n {
                let v = 1 - grid[r * n + c]          // ink is the inverse of light
                guard v > 0.05 else { continue }
                let d = cell * CGFloat(0.25 + v * 0.95)
                ink.withAlphaComponent(0.92).setFill()
                // Row 0 is the top of the cover; y increases upward, so flip it.
                NSBezierPath(ovalIn: NSRect(x: photo.minX + CGFloat(c) * cell + (cell - d) / 2,
                                            y: photo.minY + CGFloat(n - 1 - r) * cell + (cell - d) / 2,
                                            width: d, height: d)).fill()
            }
        }
        ink.withAlphaComponent(0.9).setStroke()
        let frame = NSBezierPath(rect: photo); frame.lineWidth = 1.5; frame.stroke()
        drawText(text.sub.isEmpty ? albums[featIndex].name : text.sub,
                 x: photo.minX, top: photo.minY - H * 0.008, width: photoW,
                 font: serif(max(9, H * 0.016)), color: ink.withAlphaComponent(0.75))

        // Greeked columns beside the photo — suggested text, not real text.
        let colX = photo.maxX + colW * 0.05
        let cW = (W - m - colX)
        let colCount = 2
        let gutter = cW * 0.06
        let each = (cW - gutter * CGFloat(colCount - 1)) / CGFloat(colCount)
        for ci in 0..<colCount {
            var ly = top - H * 0.006
            let cx = colX + CGFloat(ci) * (each + gutter)
            var line = 0
            while ly > photo.minY - H * 0.02 {
                let f = frac(sin(Double(line * 7 + ci * 31) * 12.9898) * 43758.5453)
                let w = each * CGFloat(0.55 + f * 0.45)
                ink.withAlphaComponent(line % 9 == 0 ? 0.55 : 0.30).setFill()
                NSBezierPath(rect: NSRect(x: cx, y: ly, width: w,
                                          height: max(1, H * 0.0035))).fill()
                ly -= H * 0.0115
                line += 1
            }
        }

        // A colour bar from the cover — the one hint of the album's own palette.
        pal.accent.setFill()
        NSBezierPath(rect: NSRect(x: m, y: H * 0.035, width: colW, height: H * 0.006)).fill()
    }

    func frac(_ x: Double) -> Double { x - x.rounded(.down) }
}

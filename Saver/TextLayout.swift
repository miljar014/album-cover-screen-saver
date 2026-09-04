import ScreenSaver
import AppKit

/// Futura ships with macOS and suits the period styles; the system font is a
/// perfectly good fallback.
func retroFont(_ size: CGFloat, bold: Bool = false) -> NSFont {
    NSFont(name: bold ? "Futura-Bold" : "Futura-Medium", size: size)
        ?? .systemFont(ofSize: size, weight: bold ? .bold : .medium)
}

func softShadow(_ alpha: CGFloat = 0.8, blur: CGFloat = 12) -> NSShadow {
    let s = NSShadow()
    s.shadowColor = NSColor(white: 0, alpha: alpha)
    s.shadowBlurRadius = blur
    s.shadowOffset = NSSize(width: 0, height: -2)
    return s
}

extension AlbumCoverSaverView {

    func measure(_ text: String, font: NSFont, width: CGFloat, kern: CGFloat = 0) -> CGFloat {
        let ps = NSMutableParagraphStyle()
        ps.lineBreakMode = .byWordWrapping
        var attrs: [NSAttributedString.Key: Any] = [.font: font, .paragraphStyle: ps]
        if kern != 0 { attrs[.kern] = kern }
        return ceil((text as NSString).boundingRect(
            with: NSSize(width: width, height: .greatestFiniteMagnitude),
            options: [.usesLineFragmentOrigin, .usesFontLeading],
            attributes: attrs).height)
    }

    /// Shrinks the type until the string fits the space available, so long titles
    /// wrap and scale rather than being clipped.
    func fittedFont(_ text: String, width: CGFloat, maxHeight: CGFloat,
                    base: CGFloat, bold: Bool) -> NSFont {
        var size = base
        while size > base * 0.45 {
            let f = retroFont(size, bold: bold)
            if measure(text, font: f, width: width) <= maxHeight { return f }
            size *= 0.92
        }
        return retroFont(base * 0.45, bold: bold)
    }

    /// Draws wrapped text whose top edge is `top`, returning the height used.
    @discardableResult
    func drawText(_ text: String, x: CGFloat, top: CGFloat, width: CGFloat,
                  font: NSFont, color: NSColor, kern: CGFloat = 0,
                  shadow: NSShadow? = nil, centred: Bool = false) -> CGFloat {
        guard !text.isEmpty else { return 0 }
        let ps = NSMutableParagraphStyle()
        ps.lineBreakMode = .byWordWrapping
        ps.alignment = centred ? .center : .left
        var attrs: [NSAttributedString.Key: Any] = [
            .font: font, .foregroundColor: color, .paragraphStyle: ps]
        if kern != 0 { attrs[.kern] = kern }
        if let shadow { attrs[.shadow] = shadow }
        let h = measure(text, font: font, width: width, kern: kern)
        (text as NSString).draw(with: NSRect(x: x, y: top - h, width: width, height: h),
                                options: [.usesLineFragmentOrigin, .usesFontLeading],
                                attributes: attrs)
        return h
    }

    /// Cover art drawn aspect-fill into `rect`, with an optional soft shadow.
    func drawCover(_ index: Int, in rect: NSRect, alpha: CGFloat = 1,
                   radius: CGFloat = 0, shadowAlpha: CGFloat = 0) {
        guard alpha > 0.01 else { return }
        if shadowAlpha > 0 {
            NSGraphicsContext.saveGraphicsState()
            let sh = NSShadow()
            sh.shadowColor = NSColor(white: 0, alpha: shadowAlpha * alpha)
            sh.shadowBlurRadius = rect.width * 0.10
            sh.shadowOffset = NSSize(width: 0, height: -rect.width * 0.035)
            sh.set()
            NSColor(white: 0, alpha: alpha).setFill()
            NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius).fill()
            NSGraphicsContext.restoreGraphicsState()
        }
        guard let img = image(index) else { return }
        NSGraphicsContext.saveGraphicsState()
        NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius).addClip()
        let cover = max(rect.width, rect.height)
        img.draw(in: NSRect(x: rect.midX - cover / 2, y: rect.midY - cover / 2,
                            width: cover, height: cover),
                 from: .zero, operation: .sourceOver, fraction: alpha)
        NSGraphicsContext.restoreGraphicsState()
    }
}

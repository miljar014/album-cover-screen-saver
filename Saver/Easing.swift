import AppKit

/// Easing curves.
///
/// Linear interpolation is what makes an animation feel mechanical: it starts at
/// full speed, runs at one speed, and stops dead. Physical objects accelerate and
/// settle, and these curves are what put that back.
enum Ease {
    static func clamp(_ t: CGFloat) -> CGFloat { min(1, max(0, t)) }

    /// Slow at both ends. The default for anything that starts and stops.
    static func inOut(_ t: CGFloat) -> CGFloat {
        let x = clamp(t)
        return x < 0.5 ? 4 * x * x * x : 1 - pow(-2 * x + 2, 3) / 2
    }

    /// Quick off the mark, long gentle settle.
    static func out(_ t: CGFloat) -> CGFloat {
        let x = clamp(t)
        return 1 - pow(1 - x, 3)
    }

    /// Overshoots a little and settles back — for things that land on something.
    static func back(_ t: CGFloat) -> CGFloat {
        let x = clamp(t)
        let c1: CGFloat = 1.55, c3 = c1 + 1
        return 1 + c3 * pow(x - 1, 3) + c1 * pow(x - 1, 2)
    }
}

extension NSRect {
    /// Scaled about its own centre.
    func scaled(_ f: CGFloat) -> NSRect {
        NSRect(x: midX - width * f / 2, y: midY - height * f / 2,
               width: width * f, height: height * f)
    }
}

extension AlbumCoverSaverView {
    /// The featured cover, crossfading between records with a little scale so the
    /// change reads as one object replacing another rather than two images
    /// dissolving in place.
    func drawFeaturedCover(in rect: NSRect, radius: CGFloat = 0, shadowAlpha: CGFloat = 0) {
        let raw = featFadeRaw
        guard raw < 1 else {
            drawCover(featIndex, in: rect, alpha: 1, radius: radius, shadowAlpha: shadowAlpha)
            return
        }
        let e = Ease.inOut(raw)
        // Outgoing drifts back and away.
        drawCover(featPrevIndex, in: rect.scaled(1 + 0.07 * e), alpha: 1 - e,
                  radius: radius, shadowAlpha: shadowAlpha * (1 - e))
        // Incoming rises into place.
        drawCover(featIndex, in: rect.scaled(0.93 + 0.07 * e), alpha: e,
                  radius: radius, shadowAlpha: shadowAlpha * e)
    }
}

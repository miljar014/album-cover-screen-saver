import AppKit
import CoreImage

/// Heavily blurred album art, cached per album.
///
/// Blurring every frame would be far too slow, and blurring at full resolution
/// is wasted work — at this radius the result is indistinguishable from blurring
/// a thumbnail and scaling it up, which is what this does.
enum BlurStore {
    private static var cache: [String: NSImage] = [:]
    private static let ciContext = CIContext(options: [.useSoftwareRenderer: false])

    static func purge() { cache.removeAll() }

    static func blurred(for albumID: String, image: NSImage?) -> NSImage? {
        if let hit = cache[albumID] { return hit }
        guard let image,
              let tiff = image.tiffRepresentation,
              let ci = CIImage(data: tiff) else { return nil }

        let target: CGFloat = 140
        let scale = target / max(ci.extent.width, ci.extent.height)
        let small = ci.transformed(by: CGAffineTransform(scaleX: scale, y: scale))

        guard let filter = CIFilter(name: "CIGaussianBlur") else { return nil }
        // Clamping first stops the edges bleeding to transparent as the blur
        // samples outside the image.
        filter.setValue(small.clampedToExtent(), forKey: kCIInputImageKey)
        filter.setValue(18.0, forKey: kCIInputRadiusKey)

        guard let output = filter.outputImage?.cropped(to: small.extent),
              let cg = ciContext.createCGImage(output, from: output.extent) else { return nil }

        let result = NSImage(cgImage: cg, size: NSSize(width: output.extent.width,
                                                       height: output.extent.height))
        cache[albumID] = result
        return result
    }
}

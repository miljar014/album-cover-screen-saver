#!/usr/bin/env python3
"""
Builds the Windows icon: windows/src/AlbumCoverScreenSaver.Tray/app.ico

Ported from make-icon.py at the repo root, which draws the macOS icon. Section
11 of the brief settles that both platforms use the same mark: a vinyl record
whose label is four album-cover quadrants. It is drawing code rather than an
exported asset, so it ports as code.

Two deliberate differences from the Mac version:

* **No rounded-rectangle mask.** macOS puts every icon in a squircle. Windows
  does not, and a tray icon especially wants to be the shape of the thing
  itself on a transparent ground.

* **A simplified mark below 32 pixels.** Four distinct quadrants and a few
  dozen grooves do not survive at 16 pixels; they turn into grey mush. The
  small sizes draw the record and label silhouette with a single accent
  instead, which is what section 11 predicted would be needed.

Run from anywhere:  python3 windows/tools/make-icon.py
"""

from __future__ import annotations

import pathlib

from PIL import Image, ImageDraw, ImageFilter

ROOT = pathlib.Path(__file__).resolve().parent.parent
TARGET = ROOT / "src" / "AlbumCoverScreenSaver.Tray" / "app.ico"

SS = 4  # supersample

# Windows wants all of these in one .ico. 256 is used by the installer and by
# Explorer's large views; 16 and 20 are the tray and the title bar.
SIZES = [16, 20, 24, 32, 48, 64, 256]

# The four quadrants of the label, from the macOS icon.
QUADRANTS = [(29, 185, 84), (232, 116, 40), (120, 86, 214), (233, 209, 160)]


def full(px: int) -> Image.Image:
    """The full mark, for 32 pixels and above."""
    S = px * SS
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    cx, cy = S * 0.5, S * 0.5
    R = S * 0.48

    # The disc.
    d.ellipse([cx - R, cy - R, cx + R, cy + R], fill=(14, 14, 16, 255))

    # Grooves. Every third one brighter, so the surface reads as ridged rather
    # than as a gradient.
    r = R * 0.97
    i = 0
    while r > R * 0.42:
        v = 46 if i % 3 == 0 else 26
        w = max(1, int(S * 0.0022))
        d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=(v, v, v + 4, 255), width=w)
        r -= R * 0.038
        i += 1

    # The label: four album covers, which is the whole idea of the product in
    # one mark.
    lr = R * 0.46
    label = Image.new("RGBA", (int(lr * 2), int(lr * 2)))
    ld = ImageDraw.Draw(label)
    h = int(lr)
    ld.rectangle([0, 0, h, h], fill=QUADRANTS[0])
    ld.rectangle([h, 0, int(lr * 2), h], fill=QUADRANTS[1])
    ld.rectangle([0, h, h, int(lr * 2)], fill=QUADRANTS[2])
    ld.rectangle([h, h, int(lr * 2), int(lr * 2)], fill=QUADRANTS[3])

    mask = Image.new("L", label.size, 0)
    ImageDraw.Draw(mask).ellipse([0, 0, label.size[0] - 1, label.size[1] - 1], fill=255)
    img.paste(label, (int(cx - lr), int(cy - lr)), mask)

    d.ellipse([cx - lr, cy - lr, cx + lr, cy + lr],
              outline=(10, 10, 12, 255), width=max(1, int(S * 0.006)))

    sr = R * 0.055
    d.ellipse([cx - sr, cy - sr, cx + sr, cy + sr], fill=(18, 18, 20, 255))

    # Specular sweep, kept on the vinyl and off the label.
    sheen = Image.new("L", (S, S), 0)
    sd = ImageDraw.Draw(sheen)
    sd.pieslice([cx - R, cy - R, cx + R, cy + R], -60, -16, fill=54)
    sd.pieslice([cx - R, cy - R, cx + R, cy + R], 120, 164, fill=34)
    sheen = sheen.filter(ImageFilter.GaussianBlur(S * 0.030))

    clip = Image.new("L", (S, S), 0)
    ImageDraw.Draw(clip).ellipse([cx - R, cy - R, cx + R, cy + R], fill=255)
    ImageDraw.Draw(clip).ellipse([cx - lr, cy - lr, cx + lr, cy + lr], fill=0)
    sheen = Image.composite(sheen, Image.new("L", (S, S), 0), clip)
    img.paste(Image.new("RGB", (S, S), (255, 255, 255)), (0, 0), sheen)

    # Rim light along the top edge.
    d.arc([cx - R, cy - R, cx + R, cy + R], 190, 350,
          fill=(120, 120, 130, 255), width=max(1, int(S * 0.004)))

    return img.resize((px, px), Image.LANCZOS)


def small(px: int) -> Image.Image:
    """
    The simplified mark, for 24 pixels and below.

    A disc, a label in one accent colour, a spindle. No grooves, no quadrants,
    no sheen. At this size those are not detail, they are noise.
    """
    S = px * SS
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    cx, cy = S * 0.5, S * 0.5
    R = S * 0.48

    d.ellipse([cx - R, cy - R, cx + R, cy + R], fill=(22, 22, 26, 255))

    # One bright ring, so the silhouette is not a featureless dot against a
    # dark taskbar.
    d.ellipse([cx - R, cy - R, cx + R, cy + R],
              outline=(150, 150, 160, 255), width=max(1, int(S * 0.035)))

    lr = R * 0.46
    d.ellipse([cx - lr, cy - lr, cx + lr, cy + lr], fill=QUADRANTS[0])

    sr = R * 0.10
    d.ellipse([cx - sr, cy - sr, cx + sr, cy + sr], fill=(18, 18, 20, 255))

    return img.resize((px, px), Image.LANCZOS)


def main() -> None:
    TARGET.parent.mkdir(parents=True, exist_ok=True)

    images = [(small(px) if px < 32 else full(px)) for px in SIZES]

    # Pillow writes every supplied size into one .ico when the largest image is
    # saved with the rest passed as append_images.
    largest = images[-1]
    largest.save(
        TARGET,
        format="ICO",
        sizes=[(px, px) for px in SIZES],
        append_images=images[:-1],
    )

    print(f"wrote {TARGET} with sizes {SIZES}")

    # A contact sheet, so the small sizes can actually be looked at.
    sheet = Image.new("RGBA", (sum(SIZES) + 20 * len(SIZES), 300), (32, 32, 36, 255))
    x = 10
    for px, image in zip(SIZES, images):
        sheet.paste(image, (x, (300 - px) // 2), image)
        x += px + 20
    sheet.save(pathlib.Path(__file__).resolve().parent / "icon-preview.png")


if __name__ == "__main__":
    main()

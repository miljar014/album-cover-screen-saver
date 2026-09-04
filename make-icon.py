from PIL import Image, ImageDraw, ImageFilter
import math

SS = 4  # supersample

def rounded_mask(size, radius):
    m = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(m)
    d.rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=255)
    return m

def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))

def make(px):
    S = px * SS
    img = Image.new("RGB", (S, S), (0, 0, 0))
    d = ImageDraw.Draw(img)

    # Background: deep vertical gradient, warm at the bottom like console wood.
    top, bot = (28, 24, 46), (12, 10, 16)
    for y in range(S):
        d.line([(0, y), (S, y)], fill=lerp(top, bot, y / S))

    cx, cy = S * 0.5, S * 0.5
    R = S * 0.365

    # Vinyl disc.
    d.ellipse([cx - R, cy - R, cx + R, cy + R], fill=(14, 14, 16))
    # Grooves.
    r = R * 0.97
    i = 0
    while r > R * 0.42:
        v = 46 if i % 3 == 0 else 26
        w = max(1, int(S * 0.0022))
        d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=(v, v, v + 4), width=w)
        r -= R * 0.038
        i += 1

    # Label: four album-cover quadrants — the collage idea in one mark.
    lr = R * 0.46
    quad = [(29, 185, 84), (232, 116, 40), (120, 86, 214), (233, 209, 160)]
    lab = Image.new("RGB", (int(lr * 2), int(lr * 2)))
    ld = ImageDraw.Draw(lab)
    h = int(lr)
    ld.rectangle([0, 0, h, h], fill=quad[0])
    ld.rectangle([h, 0, int(lr * 2), h], fill=quad[1])
    ld.rectangle([0, h, h, int(lr * 2)], fill=quad[2])
    ld.rectangle([h, h, int(lr * 2), int(lr * 2)], fill=quad[3])
    lm = Image.new("L", lab.size, 0)
    ImageDraw.Draw(lm).ellipse([0, 0, lab.size[0] - 1, lab.size[1] - 1], fill=255)
    img.paste(lab, (int(cx - lr), int(cy - lr)), lm)

    # Label ring + spindle.
    d.ellipse([cx - lr, cy - lr, cx + lr, cy + lr],
              outline=(10, 10, 12), width=max(1, int(S * 0.006)))
    sr = R * 0.055
    d.ellipse([cx - sr, cy - sr, cx + sr, cy + sr], fill=(18, 18, 20))

    # Specular sweep across the disc, as on the record player style.
    sheen = Image.new("L", (S, S), 0)
    sd = ImageDraw.Draw(sheen)
    sd.pieslice([cx - R, cy - R, cx + R, cy + R], -60, -16, fill=54)
    sd.pieslice([cx - R, cy - R, cx + R, cy + R], 120, 164, fill=34)
    sheen = sheen.filter(ImageFilter.GaussianBlur(S * 0.030))
    # Keep the sheen on the vinyl only: off the disc and off the label.
    clip = Image.new("L", (S, S), 0)
    ImageDraw.Draw(clip).ellipse([cx - R, cy - R, cx + R, cy + R], fill=255)
    ImageDraw.Draw(clip).ellipse([cx - lr, cy - lr, cx + lr, cy + lr], fill=0)
    sheen = Image.composite(sheen, Image.new("L", (S, S), 0), clip)
    img.paste(Image.new("RGB", (S, S), (255, 255, 255)), (0, 0), sheen)

    # Rim light along the top edge.
    d.arc([cx - R, cy - R, cx + R, cy + R], 190, 350,
          fill=(120, 120, 130), width=max(1, int(S * 0.004)))

    out = img.resize((px, px), Image.LANCZOS)
    mask = rounded_mask(px * SS, int(px * SS * 0.2237)).resize((px, px), Image.LANCZOS)
    final = Image.new("RGBA", (px, px), (0, 0, 0, 0))
    final.paste(out, (0, 0), mask)
    return final

import os
os.makedirs("Icon.iconset", exist_ok=True)
specs = [(16, "16x16"), (32, "16x16@2x"), (32, "32x32"), (64, "32x32@2x"),
         (128, "128x128"), (256, "128x128@2x"), (256, "256x256"),
         (512, "256x256@2x"), (512, "512x512"), (1024, "512x512@2x")]
for px, name in specs:
    make(px).save(f"Icon.iconset/icon_{name}.png")
make(512).save("preview.png")
print("generated", len(specs), "sizes")

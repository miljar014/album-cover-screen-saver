#!/usr/bin/env python3
"""
Builds the fixture data in windows/fixtures.

The tray app does not exist until step 4, so the earlier steps need something to
draw. This writes an archive, a now-playing snapshot, a settings file and one
cover per album, all valid against the contract in doc 00 section 3.

The artists and albums are invented and the covers are abstract compositions
generated from the album id, so they are original artwork.

**No text is drawn on the covers.** Real album art is not captioned, and a
grid style is judged on how the covers read together. An earlier version wrote
the album and artist onto every cover, which made a wall of them look like a
wall of labels. The only caption the screen saver draws is its own, once, in
the corner.

Run from anywhere:  python3 windows/tools/make-fixtures.py
"""

from __future__ import annotations

import colorsys
import hashlib
import json
import math
import pathlib
import random

from PIL import Image, ImageDraw

ROOT = pathlib.Path(__file__).resolve().parent.parent
FIXTURES = ROOT / "fixtures"
ART = FIXTURES / "art"

SIZE = 600

# (artist, album, play count, played at, or None for a seeded entry)
#
# Enough of them that a full screen grid is not mostly repeats. A 4K screen at
# 200% holds about 60 tiles, so ten albums meant every cover appeared six times.
ALBUMS = [
    ("Harbour Lights", "Slow Tide", 12, "2026-09-09T03:41:18Z"),
    ("The Paper Kites Society", "Winter Radio", 9, "2026-09-09T02:58:44Z"),
    ("Cold Signal", "Static Bloom", 7, "2026-09-08T23:12:05Z"),
    ("Marguerite Vale", "Long Way Down", 5, "2026-09-08T21:03:31Z"),
    ("Otis & The Overpass", "Nightshift Gospel", 4, "2026-09-08T19:47:52Z"),
    ("Field Recordings", "Sixteen Rooms", 3, "2026-09-07T22:15:09Z"),
    ("Anna Bell Crow", "Terracotta", 6, "2026-09-07T20:41:00Z"),
    ("The Quiet Machines", "Analogue Heart", 3, "2026-09-07T18:22:47Z"),
    ("Sunday Driver", "Blue Hour", 2, "2026-09-06T23:55:12Z"),
    ("Ravensmoor", "The Long Field", 2, "2026-09-06T21:30:38Z"),
    ("Lantern Club", "Paper Moon", 8, "2026-09-06T17:04:21Z"),
    ("Delta Fern", "Riverbend", 5, "2026-09-05T22:48:03Z"),
    ("Nine Mile Radio", "Dust and Gold", 4, "2026-09-05T20:11:59Z"),
    ("Hollow Coast", "Tidewater", 3, "2026-09-05T16:37:44Z"),
    ("The Ivory Hours", "Small Hours", 7, "2026-09-04T23:29:16Z"),
    ("Junco Pass", "Winterlight", 2, "2026-09-04T19:52:30Z"),
    ("Saltmarsh", "Low Country", 4, None),
    ("Bright Antenna", "Signal Fire", 3, None),
    ("The Cartographers", "Northing", 2, None),
    ("Wren & Sparrow", "Common Ground", 5, None),
    ("Glasshouse Choir", "Evensong", 2, None),
    ("Motel Cassette", "Vacancy", 3, None),
    ("The Orchard Line", "Windfall", 1, None),
    ("Copperfield Green", "Thistledown", 1, None),
]

# Seeded albums sit one second after the epoch, exactly as the macOS build does,
# so they fall behind every real play and recency bias still favours listening.
SEEDED = "1970-01-01T00:00:01Z"
FIRST_SEEN = "2026-09-01T12:00:00Z"


def make_id(artist: str, album: str) -> str:
    """The rule from doc 00 section 3. These names are ASCII, so a plain
    character walk agrees with the grapheme walk the C# and Swift use."""
    raw = f"{artist}|{album}".lower()
    safe = "".join(c if c.isalpha() or c.isnumeric() else "-" for c in raw)
    return "lfm-" + safe[:80]


def palette(rng: random.Random, count: int = 6) -> list[tuple[int, int, int]]:
    base = rng.random()
    spread = rng.uniform(0.05, 0.22)
    shades = []
    for step in range(count):
        hue = (base + (step * spread)) % 1.0
        saturation = rng.uniform(0.30, 0.88)
        value = rng.uniform(0.28, 0.96)
        r, g, b = colorsys.hsv_to_rgb(hue, saturation, value)
        shades.append((int(r * 255), int(g * 255), int(b * 255)))
    return shades


def cover(album_id: str) -> Image.Image:
    seed = int(hashlib.sha256(album_id.encode()).hexdigest()[:12], 16)
    rng = random.Random(seed)
    shades = palette(rng)

    image = Image.new("RGB", (SIZE, SIZE), shades[0])
    draw = ImageDraw.Draw(image, "RGBA")

    style = seed % 7

    if style == 0:  # stacked bands
        y = 0
        while y < SIZE:
            height = rng.randint(24, 120)
            draw.rectangle([0, y, SIZE, y + height], fill=rng.choice(shades[1:]))
            y += height

    elif style == 1:  # concentric rings, off centre
        cx = SIZE * rng.uniform(0.35, 0.65)
        cy = SIZE * rng.uniform(0.35, 0.65)
        rings = rng.randint(9, 16)
        for step in range(rings, 0, -1):
            radius = SIZE * step / rings * rng.uniform(0.62, 0.80)
            draw.ellipse(
                [cx - radius, cy - radius, cx + radius, cy + radius],
                fill=shades[step % len(shades)],
            )

    elif style == 2:  # diagonal shards
        for _ in range(rng.randint(6, 12)):
            x = rng.randint(-SIZE // 2, SIZE)
            width = rng.randint(30, 160)
            draw.polygon(
                [(x, 0), (x + width, 0), (x + width - SIZE // 2, SIZE), (x - SIZE // 2, SIZE)],
                fill=rng.choice(shades[1:]),
            )

    elif style == 3:  # grid of blocks
        cells = rng.choice([3, 4, 5, 6])
        cell = SIZE / cells
        for row in range(cells):
            for column in range(cells):
                draw.rectangle(
                    [column * cell, row * cell, (column + 1) * cell, (row + 1) * cell],
                    fill=rng.choice(shades),
                )

    elif style == 4:  # split field with a disc
        draw.rectangle([0, 0, SIZE, SIZE * rng.uniform(0.35, 0.65)], fill=shades[1])
        radius = SIZE * rng.uniform(0.18, 0.30)
        cx = SIZE * rng.uniform(0.30, 0.70)
        cy = SIZE * rng.uniform(0.32, 0.58)
        draw.ellipse([cx - radius, cy - radius, cx + radius, cy + radius], fill=shades[3])
        draw.rectangle([0, SIZE * 0.78, SIZE, SIZE * 0.82], fill=shades[4])

    elif style == 5:  # scattered dots on a plain ground
        image.paste(shades[1], [0, 0, SIZE, SIZE])
        for _ in range(rng.randint(18, 60)):
            radius = rng.uniform(SIZE * 0.02, SIZE * 0.12)
            cx = rng.uniform(0, SIZE)
            cy = rng.uniform(0, SIZE)
            draw.ellipse(
                [cx - radius, cy - radius, cx + radius, cy + radius],
                fill=rng.choice(shades[2:]),
            )

    else:  # nested rectangles
        inset = 0
        index = 1
        while inset < SIZE / 2:
            draw.rectangle(
                [inset, inset, SIZE - inset, SIZE - inset],
                fill=shades[index % len(shades)],
            )
            inset += rng.randint(18, 60)
            index += 1

    # A quiet vignette, so a wall of covers has some depth rather than reading
    # as flat colour swatches.
    for step in range(28):
        alpha = int(3 + (step * 1.4))
        draw.rectangle([step, step, SIZE - step, SIZE - step], outline=(0, 0, 0, alpha))

    return image


def main() -> None:
    ART.mkdir(parents=True, exist_ok=True)

    albums: dict[str, dict] = {}
    newest_id = ""
    newest_played = ""

    for artist, album, plays, played in ALBUMS:
        album_id = make_id(artist, album)
        last_played = played or SEEDED

        cover(album_id).save(ART / f"{album_id}.jpg", "JPEG", quality=88, optimize=True)

        albums[album_id] = {
            "id": album_id,
            "name": album,
            "artist": artist,
            "imageURL": f"https://example.invalid/art/{album_id}.jpg",
            "firstSeen": FIRST_SEEN,
            "lastPlayed": last_played,
            "playCount": plays,
        }

        if last_played > newest_played:
            newest_played, newest_id = last_played, album_id

    archive = {
        "albums": albums,
        "lastCursorMs": 1788561066092,
        "updated": "2026-09-09T03:41:18Z",
    }

    now_playing = {
        "albumID": newest_id,
        "albumName": albums[newest_id]["name"],
        "artist": albums[newest_id]["artist"],
        "track": "Slow Tide",
        "imageURL": albums[newest_id]["imageURL"],
        "isPlaying": True,
        "updated": "2026-09-09T03:41:18Z",
        "progressMs": 84000,
        "durationMs": 213000,
    }

    settings = {
        "mode": "mosaic",
        "tileSize": 200,
        "tempo": 4.0,
        "recencyBias": 0.4,
        "showTrackLabel": True,
        "multiMonitor": "separate",
        "displays": {},
    }

    for name, payload in (
        ("archive.json", archive),
        ("nowplaying.json", now_playing),
        ("settings.json", settings),
    ):
        (FIXTURES / name).write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")

    print(f"wrote {len(albums)} albums to {FIXTURES}")
    print(f"now playing: {newest_id}")


if __name__ == "__main__":
    main()

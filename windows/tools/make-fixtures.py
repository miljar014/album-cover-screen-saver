#!/usr/bin/env python3
"""
Builds the fixture data in windows/fixtures.

The tray app does not exist until step 4, so steps 2 and 3 need something to
draw. This writes a small archive, a now-playing snapshot, a settings file and
one cover per album, all valid against the contract in doc 00 section 3.

The artists and albums are invented. The covers are abstract compositions
generated from the album id, so they are original artwork and every one of them
is clearly distinguishable from the others on screen, which is what makes a
grid of them useful for checking a renderer.

Run from anywhere:  python3 windows/tools/make-fixtures.py
"""

from __future__ import annotations

import colorsys
import hashlib
import json
import math
import pathlib
import random

from PIL import Image, ImageDraw, ImageFont

ROOT = pathlib.Path(__file__).resolve().parent.parent
FIXTURES = ROOT / "fixtures"
ART = FIXTURES / "art"

SIZE = 600

# (artist, album, play count, played at, or None for a seeded entry)
ALBUMS = [
    ("Harbour Lights", "Slow Tide", 12, "2026-09-09T03:41:18Z"),
    ("The Paper Kites Society", "Winter Radio", 9, "2026-09-09T02:58:44Z"),
    ("Cold Signal", "Static Bloom", 7, "2026-09-08T23:12:05Z"),
    ("Marguerite Vale", "Long Way Down", 5, "2026-09-08T21:03:31Z"),
    ("Otis & The Overpass", "Nightshift Gospel", 4, "2026-09-08T19:47:52Z"),
    ("Field Recordings", "Sixteen Rooms", 3, "2026-09-07T22:15:09Z"),
    ("Anna Bell Crow", "Terracotta", 2, None),
    ("The Quiet Machines", "Analogue Heart", 2, None),
    ("Sunday Driver", "Blue Hour", 1, None),
    ("Ravensmoor", "The Long Field", 1, None),
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


def palette(seed: int) -> list[tuple[int, int, int]]:
    rng = random.Random(seed)
    base = rng.random()
    shades = []
    for step in range(5):
        hue = (base + step * rng.uniform(0.06, 0.16)) % 1.0
        saturation = rng.uniform(0.35, 0.85)
        value = rng.uniform(0.30, 0.95)
        r, g, b = colorsys.hsv_to_rgb(hue, saturation, value)
        shades.append((int(r * 255), int(g * 255), int(b * 255)))
    return shades


def font(size: int):
    for candidate in (
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
    ):
        if pathlib.Path(candidate).exists():
            return ImageFont.truetype(candidate, size)
    return ImageFont.load_default()


def cover(artist: str, album: str, album_id: str) -> Image.Image:
    seed = int(hashlib.sha256(album_id.encode()).hexdigest()[:12], 16)
    rng = random.Random(seed)
    shades = palette(seed)

    image = Image.new("RGB", (SIZE, SIZE), shades[0])
    draw = ImageDraw.Draw(image, "RGBA")

    style = seed % 4

    if style == 0:  # stacked bands
        y = 0
        while y < SIZE:
            height = rng.randint(30, 110)
            draw.rectangle([0, y, SIZE, y + height], fill=rng.choice(shades[1:]))
            y += height
    elif style == 1:  # concentric arcs
        for step in range(14, 0, -1):
            radius = SIZE * step / 14 * 0.72
            box = [SIZE / 2 - radius, SIZE * 0.42 - radius, SIZE / 2 + radius, SIZE * 0.42 + radius]
            draw.ellipse(box, fill=shades[step % len(shades)])
    elif style == 2:  # diagonal shards
        for _ in range(9):
            x = rng.randint(-SIZE // 2, SIZE)
            width = rng.randint(40, 150)
            draw.polygon(
                [(x, 0), (x + width, 0), (x + width - SIZE // 2, SIZE), (x - SIZE // 2, SIZE)],
                fill=rng.choice(shades[1:]),
            )
    else:  # grid of blocks
        cells = rng.choice([3, 4, 6])
        cell = SIZE / cells
        for row in range(cells):
            for column in range(cells):
                draw.rectangle(
                    [column * cell, row * cell, (column + 1) * cell, (row + 1) * cell],
                    fill=rng.choice(shades),
                )

    # A dark band along the bottom so the text is legible on any palette.
    draw.rectangle([0, SIZE - 165, SIZE, SIZE], fill=(0, 0, 0, 190))

    album_font = font(46)
    artist_font = font(30)

    def fit(text: str, start: int, minimum: int, limit: int):
        size = start
        while size > minimum:
            candidate = font(size)
            if draw.textlength(text, font=candidate) <= limit:
                return candidate
            size -= 2
        return font(minimum)

    album_font = fit(album, 46, 22, SIZE - 80)
    artist_font = fit(artist.upper(), 30, 16, SIZE - 80)

    draw.text((40, SIZE - 132), album, font=album_font, fill=(255, 255, 255))
    draw.text((40, SIZE - 70), artist.upper(), font=artist_font, fill=(235, 235, 235))

    # A thin accent rule, so a cover reduced to a thumbnail still has an edge.
    draw.rectangle([0, SIZE - 168, SIZE, SIZE - 165], fill=shades[2])

    return image


def main() -> None:
    ART.mkdir(parents=True, exist_ok=True)

    albums: dict[str, dict] = {}
    newest_id = ""
    newest_played = ""

    for artist, album, plays, played in ALBUMS:
        album_id = make_id(artist, album)
        last_played = played or SEEDED

        cover(artist, album, album_id).save(
            ART / f"{album_id}.jpg", "JPEG", quality=88, optimize=True
        )

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

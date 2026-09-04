# AlbumCoverScreenSaver

A macOS screen saver that renders a living collage of the album artwork from
your Spotify listening history, plus a small menu bar app that keeps that
history archived and growing.

---

## Why it's two pieces

macOS runs `.saver` bundles inside a sandboxed `legacyScreenSaver` process.
That process gets network access, but Keychain storage and OAuth browser
redirects are unreliable inside it — and a screen saver isn't running most of
the time anyway, so it could never build up history on its own.

So the work is split:

| | **AlbumCoverScreenSaver.app** (menu bar) | **AlbumCoverScreenSaver.saver** |
|---|---|---|
| Spotify sign-in | yes (PKCE, tokens in Keychain) | never sees a credential |
| Polls your history | every 30 min | no |
| Downloads album art | yes | no |
| Draws the collage | no | yes |
| Needs network | yes | **no** — works offline |

They meet at one folder inside the screen saver's own sandbox container, which
the app writes to and the saver reads from.

## Why the archive has to grow

Spotify's `recently-played` endpoint only ever returns your **last 50 tracks**.
There's no deep history available through the API. The menu bar app polls that
endpoint every 30 minutes and merges anything new into a permanent local
archive, so your collage gets richer the longer it runs.

On first launch it also seeds from your **top tracks** across all three time
ranges (short / medium / long term, 50 each), so the collage starts out full
instead of showing four covers.

---

## Setup

### 1. Register your own Spotify app (~3 minutes, free)

Every person who runs AlbumCoverScreenSaver registers their **own** Spotify app. This
isn't busywork — see *Why you need your own Client ID* below.

Launch the app and it opens **Spotify Setup** automatically (also available any
time from the menu bar). It walks you through it and has a Copy button for the
redirect URI, but for reference:

1. <https://developer.spotify.com/dashboard> → **Create app**
2. Name and description can be anything — just don't start the name with "Spot"
3. **Redirect URI**, exactly, then click **Add**:

   ```
   http://127.0.0.1:8888/callback
   ```

   It must be `127.0.0.1`, not `localhost` — Spotify rejects `localhost`, and the
   loopback IP is the only address it allows over plain `http`.
4. Tick **Web API**, save
5. Copy the **Client ID** from the app's page and paste it into Spotify Setup

There's no client secret — this uses Authorization Code with PKCE, so nothing
sensitive is stored in the app or its source.

### 2. Why you need your own Client ID

Spotify allows a Development Mode app **5 authorised users**, and since May 2025
Extended Quota Mode has been closed to individuals: it now requires a registered
business entity with an active service and 250,000+ monthly active users. A
hobby project cannot qualify, and no backend or hosting changes that — the limit
is an allowlist on the Client ID, enforced by Spotify before your code ever runs.

Routing everyone through one person's credentials would also breach the Developer
Terms, which require a separate Client ID per app and forbid sharing them.

So each person brings their own registration and their own allowance. Two
consequences worth knowing:

- As of February 2026, **the app owner needs Spotify Premium** for a Development
  Mode app to function.
- You can add up to 4 other people to *your* app under **User Management** on its
  dashboard page, if you'd rather not have them register their own.

### 3. Build

**In Xcode** — open `AlbumCoverScreenSaver.xcodeproj`. There are two schemes in the
menu next to the run button:

- **AlbumCoverScreenSaver** — the menu bar app. Press <kbd>Cmd</kbd>+<kbd>R</kbd> to
  run it. It has no window and no Dock icon; look for the grid icon in your
  menu bar.
- **AlbumCoverSaverModule** — the screen saver. Press <kbd>Cmd</kbd>+<kbd>B</kbd>
  to build. There's nothing to "run" — a `.saver` is loaded by macOS, not
  launched. A build phase copies it straight into `~/Library/Screen Savers/`
  whenever you build in **Release**, or in Debug if you set `INSTALL_SAVER=1`.

  To switch a scheme to Release: **Product → Scheme → Edit Scheme → Run →
  Build Configuration → Release.**

**Or from the terminal**, no Xcode window needed:

```bash
cd ~/Developer/AlbumCoverScreenSaver
./build.sh install
```

That compiles both bundles, copies the saver to `~/Library/Screen Savers/` and
the app to `/Applications/`, and writes everything to `build.log`.

### 4. Turn it on

1. Launch **AlbumCoverScreenSaver** from `/Applications`. It appears as a grid icon in
   the menu bar — no Dock icon, no window.
2. Click it → **Sign In to Spotify**. Your browser opens, you approve, done.
3. It immediately seeds the archive and downloads covers. The menu shows
   progress and the album count.
4. Turn on **Open at Login** from the same menu so it keeps archiving.
5. **System Settings → Screen Saver → AlbumCoverScreenSaver.**

---

## The nine styles

Open **AlbumCoverScreenSaver** in the menu bar → **Settings…** (or <kbd>Cmd</kbd>+<kbd>,</kbd>).

> Settings are **not** in System Settings. macOS's redesigned Screen Saver pane
> removed the **Options** button for third-party screen savers, so there is no
> sheet to open there. The app's own window replaces it.

- **Mosaic Grid** — dense full-screen grid; tiles flip to new albums on a timer.
  *Options:* random cover size, how many tiles flip at once, flip duration.
  **Random cover size** promotes roughly one cover in seven to a double-size
  block. Blocks are placed first and the gaps filled with single cells, so the
  screen still tiles exactly.
- **Drifting Float** — covers drift across a dark field at varied sizes and
  speeds with soft shadows, fading in and out at the edges.
  *Options:* number of covers on screen, drift speed, cover size.
- **Slow-Building Wall** — starts empty, covers land one at a time until full,
  holds, dissolves, rebuilds with a new selection.
  *Options:* hold duration when full, build speed.
- **Hero + Grid** — one large featured album crossfading over a dimmed grid of
  the rest. **While Spotify is playing, the featured cover follows the current
  track**, with a NOW PLAYING badge and the track name; when playback stops it
  resumes rotating through your history after 90 seconds.
  *Options:* feature what's playing, featured cover size, background dimming,
  seconds between features.

- **Record Player** — a mid-century turntable: walnut console with visible grain
  and brass trim, chrome platter, and the album art turning as the record label
  while a tonearm tracks slowly inward. A cream sleeve card names the record.
  **While Spotify is playing, the record follows the current track**, with the
  arm lifting and a new disc dropping when it changes.
  *Options:* play what's playing, seconds per record, turntable speed
  (snaps to 33⅓ / 45 / 78 RPM).

- **Ambient Field** — the cover suspended in a slowly drifting field of its own
  colours, with a soft reflection beneath and centred type.
  *Options:* seconds per album, colour drift speed.
- **Cassette Deck** — an '80s tape deck. The reels wind for real: the supply reel
  empties as the take-up fills, each turning at the speed its radius implies, and
  the wind tracks actual song progress. VU needles, brushed-metal face.
  *Options:* seconds per album.
- **Crate Digging** — a fanned stack of sleeves sliding past in a record crate,
  front one pulled forward. Jumps straight to whatever starts playing.
  *Options:* sleeves in the crate, seconds per pass.
- **Gallery Wall** — framed covers with mats and placards, under a spotlight that
  drifts across the wall. The playing record hangs in the middle.
  *Options:* frames across, seconds per album.

All four follow Spotify automatically when it's playing and rotate on a timer
otherwise.

Shared across all four: **tile size** (the window shows the resulting grid for
your display, e.g. "≈ 10 × 6 = 60 covers"), **seconds between changes**, how
strongly selection **favours recent listening** over the whole archive, and
whether the album/artist name is shown.

Changes save immediately. The running saver re-reads them within a few seconds —
no reinstall, no restart.

**Drifting Float's "Random size (depth effect)"** varies cover size and ties
motion to it: small covers sit further back, drift more slowly and fade into the
dark, while large ones sweep past in front. Because parallax — not size alone —
is what reads as depth, the two are deliberately locked together, so the fixed
**Cover size** slider greys out while it's on.

### After rebuilding the saver

Settings changes apply on their own — the running saver re-reads them every few
seconds. But macOS keeps the previously loaded `.saver` **binary** alive in its
`legacyScreenSaver` / `WallpaperAgent` helpers, so a new build won't take effect
until those restart.

`make install` now ends both helpers for you, which is enough in almost every
case. macOS relaunches them on demand, and the only visible effect is a brief
wallpaper flicker.

If a code change still doesn't show up, fall back to switching the screen saver
to something else and back, which forces a full reload.

## Where your data lives

```
~/Library/Application Support/AlbumCoverScreenSaver/archive.json
    Master archive. Survives everything.

~/Library/Containers/com.apple.ScreenSaver.Engine.legacyScreenSaver/
    Data/Library/Application Support/AlbumCoverScreenSaver/
    archive.json   mirror the saver reads
    settings.json  your Options choices
    art/           one ~300px JPEG per album
```

**Reveal Art Cache in Finder** in the menu opens the second one. Roughly 25 KB
per album, so a thousand albums is about 25 MB.

Tokens are in your login Keychain under `com.jaredmiller.AlbumCoverScreenSaver`.
**Sign Out** removes them. Nothing is sent anywhere except Spotify.

---

## Notes and caveats

- **Your Spotify app stays in development mode**, which is fine for personal
  use — it allows up to 25 users you add by hand. Going public requires a quota
  extension review you don't need here.
- **Code signing and the Keychain prompt.** macOS ties Keychain access rules to
  an app's exact code signature. Ad-hoc signing (`codesign -s -`) produces a new
  signature on every build, so every rebuild makes macOS re-prompt for your
  login password — and "Always Allow" only whitelists that one binary.

  The fix is a stable self-signed certificate, created once:

  1. Open **Keychain Access**
  2. Menu: **Keychain Access → Certificate Assistant → Create a Certificate…**
  3. **Name:** `AlbumCoverScreenSaver Dev`
     **Identity Type:** Self Signed Root
     **Certificate Type:** Code Signing
  4. **Create**, then **Done**

  The Makefile picks it up automatically and prints which identity it used.
  Without it, it falls back to ad-hoc and says so.

  Sharing the saver with anyone else is different again — that needs a real
  Developer ID certificate and notarization.
- **`exit(0)` on stop.** macOS 14+ leaks `ScreenSaverView` instances inside
  `legacyScreenSaver`, so repeat activations pile up CPU. Ending the host
  process on stop is the standard workaround — see the comment in
  `AlbumCoverSaverView.swift` if you'd rather remove it.
- **Podcasts and local files** don't carry album art the same way and are
  skipped.
- **First run needs a moment.** Seeding downloads up to ~150 covers.

## Shipping it to other people

One command cuts a release. Everything below that command is automated: build,
notarize, sign the update, regenerate the feed, publish.

```bash
make release                 # 2.1 -> 2.1.1
make release PART=minor      # 2.1 -> 2.2
make release PART=major      # 2.2 -> 3.0
```

### One-time setup

```bash
tools/release-setup.sh
```

It vendors Sparkle, generates the update-signing key pair, finds your Developer
ID, checks your notarytool credentials, creates the GitHub repo, and writes
`Release.config`. Re-running it is safe — every step checks before it acts.

**Back up the private update key.** It lives in your login Keychain and it is
the one irreplaceable thing in this project: without it you cannot sign an
update that your installed users will accept, and the only remedy is asking
every one of them to download the app again by hand.

```bash
Vendor/Sparkle/bin/generate_keys -x sparkle-private-key.txt
```

### What a release does

1. Bumps `VERSION` and stops if `releases/notes/<version>.md` is empty — the
   notes are what users read inside the updater, so they are not optional.
2. Full clean rebuild, signed with your Developer ID under the hardened runtime.
3. Zips the app, sends it to Apple, waits, staples the ticket, **re-zips**. The
   ticket has to be inside the archive Sparkle hands out or every update trips
   Gatekeeper.
4. Builds the disk image from the stapled app, notarizes and staples that too.
5. Signs the archive with the Sparkle key and rewrites `releases/appcast.xml`
   from `releases/releases.json`.
6. Publishes the tag to GitHub with three assets: the DMG (for new users), the
   zip (for the updater), and `appcast.xml`.

### Where the files live

Releases go to GitHub Releases, which is free, keeps every version at a
permanent URL, and serves unlimited public downloads.

| What | Where |
|---|---|
| Download page | `https://github.com/OWNER/REPO/releases/latest` |
| Update feed | `https://github.com/OWNER/REPO/releases/latest/download/appcast.xml` |

The feed URL is the trick that makes this maintenance-free: `latest/download/`
always resolves to the newest release's asset, so the address baked into every
shipped copy never has to change.

The repo has to be **public** — release assets of a private repo are not
publicly downloadable, which would make the feed unreachable for everyone. Your
`ClientID` and `LastFMKey` files are gitignored; they are baked into the binary
at build time either way.

### How users get updates

The app checks once a day and offers the update; **Check for Updates…** in the
menu bar forces it. Sparkle downloads the zip, verifies the EdDSA signature
against the public key in the app's `Info.plist`, swaps the app and relaunches.

The screen saver updates with it. The `.saver` ships **inside** the app bundle,
and the app copies it into `~/Library/Screen Savers` on launch whenever the
bundled build is newer — because Sparkle can only replace the `.app`, and a
screen saver that could only be updated by hand would drift a version behind
forever. It is also why installation is now a single drag with no installer
script.

### Cutting the same version again

If a release fails part-way through, fix it and run `make ship` — that skips the
bump and re-cuts the current version, replacing the GitHub release and its tag.
Use `make release` only when you want the version number to move.

## Rebuilding after edits

```bash
./build.sh install     # then re-select the saver in System Settings
```

macOS caches loaded savers aggressively. If a change doesn't show up, quit
System Settings entirely and reopen it.

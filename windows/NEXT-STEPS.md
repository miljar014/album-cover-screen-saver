# Windows port: everything still to do

Written 12 September 2026, after step 7a. Four of nineteen styles draw.

Read this with `claude/windows-port/progress.md`, which says what is already
built and why.

---

## 0. Before anything else

### 0a. Push

**Five commits exist only on the VM.** GitHub is still on `b5dc3bf`, which is
step 1. Everything since (steps 2, 3, 4, 5a, 5b and 7a) is on one machine.

Run `windows\BUILD.bat` and answer **y** to the GitHub question at the end.

This is the single biggest risk on the project right now. The VM is a virtual
machine; if it is deleted, rolled back, or simply stops working, six build steps
go with it. The cloud session cannot push, because its git proxy refuses this
repository, so it has to be you.

### 0b. Confirm step 7a builds and looks right

About 900 lines of new drawing code went to the VM without ever being compiled,
because the cloud container cannot build anything that targets Windows. A build
error on the first run would not be a surprise.

What to check, style by style, from the tray Settings window:

| Style | What it should look like |
|---|---|
| Mosaic Grid | Unchanged from before. If it changed, something in the shared grid code broke. |
| Hero + Grid | One big cover centred on a dim grid. Play something: it should switch to that album and the label should show the track with a green NOW PLAYING badge. |
| Slow-Building Wall | Starts empty. Fills one cover at a time over about 12 seconds, holds 6, dissolves in the same order, repeats. |
| Drifting Float | Covers sliding across black. Watch for at least a minute: the direction should change and the covers should turn one at a time rather than all together. Play something and a blurred version of that cover should fade in behind them. |

---

## 1. Step 7b: the remaining fifteen styles

This is the long tail, and the bulk of the work left. You can stop partway and
still have a finished product.

### 1a. The shared machinery first (one session)

Four of the five shared helpers in doc 01 section 2 do not exist yet, and nearly
every remaining style needs at least one of them. Building them as a batch, with
tests, is cheaper than discovering each one halfway through a style.

| Helper | What it does | Who needs it |
|---|---|---|
| `ArtPalette` | Pulls a colour scheme out of a cover: an accent, a near-black "room", a metal, readable text colours, and up to six swatches. Includes the WCAG contrast loop that nudges a colour until it is legible. | Every device style, and most scene styles |
| `TextLayout` | Wrapped text measurement, and a font that shrinks until a long album title fits instead of being cut off | Every style with typography |
| `ScatterLayout` | Persistent boards: where to put a new record on a table without covering one already there, and the rules that keep it there across track changes | Record Player, Polaroid Corkboard |
| `CorkTexture` | A procedurally drawn cork board, about 5,000 granules, generated once and cached | Polaroid Corkboard |
| `HalftoneStore` | Luminance grids, for rendering a cover as characters or dots | CRT Terminal, Newsstand |

All five are pure arithmetic or pure bitmap work, so all five can be built and
tested in the cloud before a line of them runs on Windows. That is the habit that
has caught something on every single step so far.

**Note on fonts.** The Mac uses Futura, which Windows does not have. Every use of
it goes through the shrink-to-fit function, so the fallback degrades to a
slightly different size rather than to clipped text. Century Gothic is the
closest common Windows face if we want the period feel.

### 1b. The ten scene styles (doc 03)

Cheaper than the device styles: they are compositions of covers rather than
drawings of objects.

Polaroid Corkboard, Crate Digging, Gallery Wall, Cover Flow, Starfield Orbit,
Newsstand, Vaporwave Grid, Zoetrope, Subway Platform, Ambient Field.

Roughly two or three per session, so **four or five sessions**. Ambient Field and
Gallery Wall are the simplest; Polaroid Corkboard needs the cork texture and the
persistent board, so it should come after 1a rather than first.

### 1c. The five device styles (doc 02)

Record Player, Cassette Deck, CD Player, Neon Jukebox, CRT Terminal.

These are drawings of physical objects with dozens of measured parts each: a
turntable with a tonearm that tracks the record, a tape deck whose reels wind at
the right rate, a CRT that renders the cover as text. Doc 02 specifies every
rectangle and every gradient angle.

**Record Player and Cassette Deck are the most intricate and the most loved.**
The plan deliberately saves them for last, when the engine is solid.

Roughly one per session, so **five sessions**. Record Player also brings its own
rule into the engine: when nothing is playing it hands over to another style
after a twenty second grace period, and it opens in that fallback style from
cold. That code has a place reserved for it in `Scene.cs` already.

---

## 2. Step 5b and the trip to the real PC

Everything in this section needs the PC with two monitors. **The VM has one
display, and doc 04 section 6 says the whole Screens section is hidden when there
is only one.** Building several hundred lines of Windows interop that cannot be
seen or exercised on the machine doing the building is how unverified code gets
believed.

### 2a. What gets built there

From doc 04 sections 2 and 4 to 6:

- **Display identity that survives a reboot.** `QueryDisplayConfig` gives each
  monitor a stable device path, so "the left one is set to Cassette Deck" still
  means the left one tomorrow.
- **The arrangement map.** A small picture of how the monitors are laid out, so
  you can tell which is which.
- **A style dropdown per display**, plus Off.
- **Linked mode.** All screens featuring the same album at the same time, driven
  by one director, instead of each going its own way.

### 2b. What gets checked there, which step 2 still owes

Doc 04 section 8 lists these, and none of them can be done on a VM:

- Two displays at **different scaling**. This is the case a single spanning
  window fails, and it is the one that proves the whole window-per-display
  design.
- Two displays at different sizes: both fill their own screen completely.
- Duplicate mode: one logical screen, with the explanatory line showing.
- One display set to Off is solid black, and the lock screen still appears
  normally when you come back.
- Unplugging a monitor while it is running: no crash.
- Unplugging and replugging between runs: that display's setting comes back.
- **Preview mode in the Windows Screen Saver dialog.**
- The saver actually starting on idle, and the lock screen on resume.

Call it **one or two sessions at the PC.** Worth doing before step 6, because a
release that fails on two monitors is a release you have to redo.

---

## 3. Step 6: signing and the first release

**This is the step where you spend money and where I need decisions from you.**

### 3a. The x64 build, which has never been made

Everything built so far is ARM64, because the VM is ARM. Most Windows PCs are
x64. Nothing in the code should care, but "should" is doing work in that
sentence: SkiaSharp ships separate native binaries per architecture, and that is
exactly the kind of thing that is fine until it is not.

Prism runs x64 programs on the ARM VM with nothing to install, so we can prove an
x64 build starts. Running it properly still wants a real x64 machine, which the
PC is.

### 3b. Code signing

Without a signature, Windows SmartScreen shows a blue "Windows protected your PC"
box and a **More info** link that people have to click to run the installer. Most
people do not click it.

Two kinds of certificate, and the difference matters:

- **OV (organisation validation).** Cheaper. Still triggers SmartScreen until the
  installer builds up reputation, which takes downloads over time.
- **EV (extended validation).** More expensive, and the private key has to live
  on a hardware token or in a cloud HSM, so it cannot simply sit on your disk.
  SmartScreen trusts it immediately.

Both require verifying that you, or Miljar Productions, are a real registered
entity. That paperwork takes days to weeks and is the long pole, not the money.

**I will look up current prices and requirements when we actually get here
rather than quoting numbers from memory, because both change.**

There is a real question of whether this should be under Miljar Productions,
given that is the name already being set up for the Brickworks games. Worth
deciding before buying anything, because the certificate is issued to a specific
legal entity and cannot be transferred.

### 3c. Velopack and the update feed

Velopack builds the `Setup.exe` and handles automatic updates, the way Sparkle
does on the Mac.

**The one thing doc 00 section 11 insists on getting right:** both platforms
publish to the same GitHub Releases page, so the Mac's Sparkle feed and the
Windows Velopack feed must not be able to see each other's files. Tag per
platform (`mac-2.4`, `win-1.0`) and prefix every asset name. Then verify it
explicitly: publish a Windows release, confirm the Mac updater still offers only
Mac builds, and the reverse. A cross-platform update offer is silent, confusing,
and very hard to notice.

**One or two sessions, plus whatever the certificate paperwork takes.**

---

## 4. Step 8: Last.fm

The last piece, and the one that makes the product feel finished on first run.

Right now a new install starts with an empty archive and fills up slowly as you
listen. With Last.fm connected it starts full: your listening history seeds the
collage, so the very first time the screen saver runs it is a wall of your music
rather than a wall of nothing.

Windows ships without a Spotify source by decision (doc 00 section 4c), so
Last.fm is the one that matters most here, and when it is done the Windows build
is feature-complete against the Mac.

**Needs from you:** a Last.fm API key, which is free and takes a few minutes.

**One session.**

---

## 5. Loose ends, none urgent

1. **`VERSION` says 2.1, the brief says the Mac ships 2.3.** One of the two is
   stale and it would be good to know which before cutting a Windows release.
2. **`driftBackdropBlur`** is in the brief but in neither the Mac's `Store.swift`
   nor its settings window. It is implemented on Windows per the brief, with a
   slider of its own. Possibly the Mac should get it too.
3. **`01-engine-and-base-styles.md` ends with leftover machine text** after
   checklist item 14. Junk, worth deleting from the project doc.
4. **A stray empty folder on the VM**, `Developer\Album Cover Screen Saver` with
   spaces in the name. Harmless, tidy it up sometime.
5. **The tray may be reading the wrong player.** A screenshot during step 4
   showed it reporting one track while a browser player looked like it was on
   another, both from the same album. Windows decides which media session is
   "current" and with several players open that may not be the one you are
   looking at. If it turns out to be real, the fix is to list all the sessions
   and choose deliberately. Not yet confirmed as a bug.

---

## The shape of what is left

| Step | Where | Sessions | Needs you to |
|---|---|---|---|
| 0. Push, verify 7a | VM | part of one | Run BUILD.bat, say y |
| 7b-1. Shared machinery | VM or cloud | 1 | Nothing |
| 7b-2. Ten scene styles | VM | 4 to 5 | Nothing |
| 7b-3. Five device styles | VM | 5 | Nothing |
| 5b. Screens section | **PC** | 1 to 2 | Be at the PC, two monitors |
| 6. Signing and release | PC | 1 to 2 | **Buy a certificate, decide the legal entity** |
| 8. Last.fm | Either | 1 | Get a free API key |

Call it **thirteen to sixteen sessions**, of which two need you at the PC and one
needs you to spend money.

**The order is not fixed.** The plan puts step 6 before step 7, but step 6 is
where money goes and there is more sense in having more of the product first. The
PC work in 5b is the other thing that could reasonably move earlier, because it
closes out the one part of step 2 that was never verified.

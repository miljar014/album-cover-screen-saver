# Album Cover Screen Saver, Windows

The Windows port. The macOS app lives in the folders above this one; the two
share a repo, a Releases page and, most importantly, one data contract.

Versioning is independent: Windows starts at 1.0 whatever the Mac is on.

## Where things are

```
windows/
  src/AlbumCoverScreenSaver.Shared/   the data contract, shared by both halves
  tests/AlbumCoverScreenSaver.Shared.Tests/   the test suite
  fixtures/                           hand-built data to draw from before the tray app exists
  tools/make-fixtures.py              regenerates those fixtures
```

## Running the tests

You need the .NET 8 SDK and nothing else. There are no NuGet packages, so this
works on a machine that has never restored one.

```
cd windows
dotnet run --project tests/AlbumCoverScreenSaver.Shared.Tests
```

It prints one line per test and ends with either `Step 1 is good.` or a list of
what failed. It exits non-zero on failure, so it can go straight into CI later.

## What is built so far

**Step 1 of the build order in `claude/windows-port/00-overview-and-plan.md`.**
The shared library only. There is nothing to look at on screen yet, and that is
the expected result for this step.

- `AlbumEntry`, `Archive`, `NowPlaying`, `Settings`, `DisplaySetting`
- `AlbumEntry.MakeId`, the derived album id
- `NowPlaying.ProgressFraction` and `IsLive`
- `SharedStore`, over one folder at `%LOCALAPPDATA%\AlbumCoverScreenSaver`
- ISO-8601 dates in exactly the form Foundation reads, so an archive can move
  between the two platforms unchanged

## Three things worth knowing before touching this code

**One folder, not two.** The macOS build writes every file to two locations and
reads whichever is newer. That is a sandbox workaround and Windows does not have
the problem. If you find yourself writing "whichever root is newest", stop.

**Settings decode leniently, per field.** A missing key, a wrong type, an
unknown style id: each costs one value and never the file. Strict decoding on
macOS meant an old settings file silently reset every preference the user had.
`SettingsConverter` is where that rule lives, and the round-trip test uses
reflection so a field added to `Settings` but forgotten in the converter fails
the build rather than going unnoticed.

**The album id must match Swift exactly.** Swift walks grapheme clusters, so an
emoji or a decomposed accent is one character there and must be one here.
A mismatch does not raise an error, it just files the same album twice.

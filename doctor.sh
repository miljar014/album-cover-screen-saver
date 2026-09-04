#!/bin/bash
# Dumps everything needed to diagnose signing + settings problems into doctor.log
cd "$(dirname "$0")"
C="$HOME/Library/Containers/com.apple.ScreenSaver.Engine.legacyScreenSaver/Data/Library/Application Support/AlbumCoverScreenSaver"
{
echo "=== date ==="; date
echo; echo "=== code signing identities available to codesign ==="
security find-identity -v -p codesigning 2>&1
echo; echo "=== certificates with 'Collage' in the name ==="
security find-certificate -a -c "Collage" 2>&1 | grep -E '"labl"|"alis"' || echo "(none)"
echo; echo "=== how the INSTALLED app is signed ==="
codesign -dv --verbose=2 "/Applications/AlbumCoverScreenSaver.app" 2>&1 \
  | grep -E "Identifier|Authority|Signature|CDHash" || echo "(not installed)"
echo; echo "=== how the INSTALLED saver is signed ==="
codesign -dv --verbose=2 "$HOME/Library/Screen Savers/AlbumCoverScreenSaver.saver" 2>&1 \
  | grep -E "Identifier|Authority|Signature|CDHash" || echo "(not installed)"
echo; echo "=== settings.json, every candidate location ==="
for p in "$C/settings.json" "$HOME/Library/Application Support/AlbumCoverScreenSaver/settings.json"; do
  echo "--- $p"
  if [ -f "$p" ]; then
    stat -f '    modified: %Sm' "$p"
    echo "    $(cat "$p")"
  else
    echo "    MISSING"
  fi
done
echo; echo "=== CAN the app write into the saver container? ==="
if [ -d "$C" ]; then
  if touch "$C/.writetest" 2>/tmp/wt.err; then
    echo "WRITABLE"; rm -f "$C/.writetest"
  else
    echo "NOT WRITABLE:"; cat /tmp/wt.err
  fi
else
  echo "container dir does not exist: $C"
fi
echo; echo "=== container directory listing ==="
ls -la "$C" 2>&1 | head -20
echo; echo "=== archive + art cache ==="
echo "archive.json: $(stat -f '%z bytes, modified %Sm' "$C/archive.json" 2>/dev/null || echo MISSING)"
echo "art files:    $(ls "$C/art" 2>/dev/null | wc -l | tr -d ' ')"
echo; echo "=== write errors reported by the app ==="
cat "$HOME/Library/Application Support/AlbumCoverScreenSaver/write-errors.log" 2>/dev/null || echo "(none)"
echo; echo "=== app-support copy ==="
A="$HOME/Library/Application Support/AlbumCoverScreenSaver"
echo "archive.json: $(stat -f '%z bytes, modified %Sm' "$A/archive.json" 2>/dev/null || echo MISSING)"
echo "art files:    $(ls "$A/art" 2>/dev/null | wc -l | tr -d ' ')"
echo; echo "=== keychain items for this app ==="
security find-generic-password -s "com.jaredmiller.AlbumCoverScreenSaver" 2>&1 \
  | grep -E '"acct"|"svce"' || echo "(none)"
} > doctor.log 2>&1
echo "Wrote doctor.log"

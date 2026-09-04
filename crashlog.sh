#!/bin/bash
# Copies the newest crash report for this app into the project so it can be read.
cd "$(dirname "$0")"
APP="AlbumCoverScreenSaver"
DIR="$HOME/Library/Logs/DiagnosticReports"

NEWEST=$(ls -t "$DIR"/${APP}*.ips 2>/dev/null | head -1)
if [ -z "$NEWEST" ]; then
  echo "No crash report found for $APP." > crash.log
  echo "Looking for anything recent instead:" >> crash.log
  ls -t "$DIR" 2>/dev/null | head -10 >> crash.log
else
  echo "=== $NEWEST ===" > crash.log
  cat "$NEWEST" >> crash.log
fi

echo "" >> crash.log
echo "=== running it from the terminal to catch stderr ===" >> crash.log
"/Applications/Album Cover Screen Saver.app/Contents/MacOS/AlbumCoverScreenSaver" \
  > run.log 2>&1 &
PID=$!
sleep 4
if kill -0 $PID 2>/dev/null; then
  echo "still running after 4s (did not crash when launched this way)" >> crash.log
  kill $PID 2>/dev/null
else
  echo "exited within 4s — output follows" >> crash.log
fi
cat run.log >> crash.log 2>/dev/null
echo "Wrote crash.log"

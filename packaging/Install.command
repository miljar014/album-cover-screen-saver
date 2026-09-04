#!/bin/bash
# Installs AlbumCoverScreenSaver. Double-click this file.
set -e
DIR="$(cd "$(dirname "$0")" && pwd)"

echo "Installing Album Cover Screen Saver…"
rm -rf "/Applications/Album Cover Screen Saver.app"
cp -R "$DIR/Album Cover Screen Saver.app" /Applications/

mkdir -p "$HOME/Library/Screen Savers"
rm -rf "$HOME/Library/Screen Savers/Album Cover Screen Saver.saver"
cp -R "$DIR/Album Cover Screen Saver.saver" "$HOME/Library/Screen Savers/"

# This build is not notarized by Apple, so macOS quarantines it and a screen
# saver has no "right-click > Open" escape hatch the way an app does. Clearing
# the quarantine flag is what lets it load. Only ever do this for software you
# trust the source of — it is a real security control, not a formality.
echo "Clearing the download quarantine flag (see note in READ ME FIRST)…"
xattr -dr com.apple.quarantine "/Applications/Album Cover Screen Saver.app" 2>/dev/null || true
xattr -dr com.apple.quarantine "$HOME/Library/Screen Savers/Album Cover Screen Saver.saver" 2>/dev/null || true

pkill -f legacyScreenSaver 2>/dev/null || true
pkill -x WallpaperAgent 2>/dev/null || true

open "/Applications/Album Cover Screen Saver.app"
echo ""
echo "Installed."
echo "  1. Album Cover Screen Saver is now in your menu bar — follow its Spotify Setup window."
echo "  2. Then System Settings > Screen Saver > Album Cover Screen Saver."

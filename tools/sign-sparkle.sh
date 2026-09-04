#!/bin/bash
# Re-sign an embedded Sparkle.framework with our own identity, inside out.
#
# Two reasons this is not optional:
#  * Under the hardened runtime a process may only load libraries signed by its
#    own team, unless it disables library validation. Re-signing with our
#    Developer ID is the clean way to satisfy that; disabling validation is the
#    dirty one.
#  * Sparkle ships helper executables (an installer XPC, a downloader XPC, the
#    Autoupdate tool and Updater.app) that codesign will not reach on its own.
#    Signing the framework without them leaves a bundle Apple rejects.
#
# --preserve-metadata=entitlements matters: the downloader XPC is sandboxed and
# carries entitlements we must not drop on the floor.
set -euo pipefail

FRAMEWORK="${1:?usage: sign-sparkle.sh <path to Sparkle.framework> <identity> [extra codesign flags...]}"
IDENTITY="${2:?missing signing identity}"
shift 2
FLAGS=("$@")

V="$FRAMEWORK/Versions/B"
[ -d "$V" ] || V="$FRAMEWORK/Versions/A"

sign() {
  [ -e "$1" ] || return 0
  codesign --force --sign "$IDENTITY" "${FLAGS[@]}" \
           --preserve-metadata=entitlements "$1"
}

# Deepest first: nested code must be sealed before whatever contains it.
for xpc in "$V"/XPCServices/*.xpc; do sign "$xpc"; done
sign "$V/Updater.app/Contents/MacOS/Updater"
sign "$V/Updater.app"
sign "$V/Autoupdate"
sign "$FRAMEWORK"

echo "    signed Sparkle.framework with '$IDENTITY'"

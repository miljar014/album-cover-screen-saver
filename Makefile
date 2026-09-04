# AlbumCoverScreenSaver — menu bar companion app + macOS screen saver
#
#   make            build both
#   make install    build, then install the saver and the app
#   make clean

# Marketing version lives in the VERSION file; `make bump` moves it.
VERSION ?= $(shell cat VERSION 2>/dev/null || echo 1.0)
# Build number is a timestamp: always increases, unique per build, and needs no
# counter file that could double-increment or drift between machines.
BUILD_NUMBER ?= $(shell date +%Y%m%d%H%M%S)

ARCH    := $(shell uname -m)
TARGET  := $(ARCH)-apple-macos12.0
SWIFTC  := xcrun swiftc
CLANG   := xcrun clang
SDK     := $(shell xcrun --show-sdk-path)
# Swift's compatibility shim archives (libswiftCompatibility56.a and friends)
# live in the toolchain, not the SDK. swiftc emits autolink references to them,
# so a raw clang link needs to be told where to look.
SWIFTLIB := $(shell dirname $(shell xcrun --find swiftc))/../lib/swift/macosx

# Code signing identity.
# Ad-hoc signing ("-") produces a NEW signature on every build, and macOS ties
# Keychain access rules to the signature — so each rebuild makes the OS re-prompt
# for the password. Signing with a stable self-signed certificate keeps the
# identity constant across rebuilds and the prompt appears only once.
# Falls back to ad-hoc automatically if the certificate isn't installed.
# Any identity from `security find-identity -v -p codesigning`.
SIGN_CERT ?= Apple Development
# Must look for an IDENTITY (certificate + private key), not just a certificate —
# codesign can't sign with a cert whose key it can't reach.
SIGN_ID := $(shell security find-identity -v -p codesigning 2>/dev/null \
             | grep -q "$(SIGN_CERT)" && echo "$(SIGN_CERT)" || echo "-")

# Notarization requires the hardened runtime and a secure timestamp. Everyday
# builds skip both: the timestamp server adds a network round trip to every
# build, and the hardened runtime only matters once the thing leaves this Mac.
ifdef RELEASE
CS_FLAGS := --options runtime --timestamp
else
CS_FLAGS := --timestamp=none
endif

# ---------------------------------------------------------------- release config
# Written by tools/release-setup.sh; holds the GitHub repo the releases go to.
-include Release.config
GH_OWNER ?=
GH_REPO  ?=
# A release asset named appcast.xml at this URL always resolves to the newest
# release, so the feed address baked into shipped copies never has to change.
FEED_URL ?= https://github.com/$(GH_OWNER)/$(GH_REPO)/releases/latest/download/appcast.xml
TAG      := v$(VERSION)

SPARKLE_DIR := Vendor/Sparkle
SPARKLE_FW  := $(SPARKLE_DIR)/Sparkle.framework
HAS_SPARKLE := $(shell test -d $(SPARKLE_FW) && echo 1)

BUILD   := build
APP     := $(BUILD)/AlbumCoverScreenSaver.app
SAVER   := $(BUILD)/AlbumCoverScreenSaver.saver
APPBIN  := $(APP)/Contents/MacOS/AlbumCoverScreenSaver
SAVBIN  := $(SAVER)/Contents/MacOS/AlbumCoverScreenSaver

SHARED     := $(wildcard Shared/*.swift)
COMPANION  := $(wildcard Companion/*.swift)
SAVERSRC   := $(wildcard Saver/*.swift)

SWIFTFLAGS := -swift-version 5 -target $(TARGET) -sdk $(SDK) -O

# The app links Sparkle when it has been vendored; without it the same sources
# still build, just with the updater compiled out (see Companion/Updater.swift).
ifeq ($(HAS_SPARKLE),1)
APPFLAGS := -D SPARKLE -F $(SPARKLE_DIR) -framework Sparkle \
            -Xlinker -rpath -Xlinker @executable_path/../Frameworks
else
APPFLAGS :=
endif

.PHONY: all app saver install clean dmg zip verify signinfo bump release ship

all: signinfo app saver

.PHONY: signinfo
signinfo:
	@echo "==> version: $(VERSION) (build $(BUILD_NUMBER))"
	@if [ "$(SIGN_ID)" = "-" ]; then \
		echo "==> signing: ad-hoc (certificate '$(SIGN_CERT)' not found)"; \
		echo "    Keychain will re-prompt after every rebuild — see README."; \
	else \
		echo "==> signing: '$(SIGN_ID)' (stable across rebuilds)"; \
	fi

# ---------------------------------------------------------------- companion app

app: $(APPBIN)

# The app depends on the saver because it *contains* it: see the copy below.
$(APPBIN): $(SHARED) $(COMPANION) Companion/Info.plist Makefile $(SAVBIN)
	@echo "==> building AlbumCoverScreenSaver.app ($(ARCH))"
	@rm -rf $(APP)
	@mkdir -p $(APP)/Contents/MacOS $(APP)/Contents/Resources
	@cp Companion/Info.plist $(APP)/Contents/Info.plist
	@/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $(VERSION)" \
		$(APP)/Contents/Info.plist >/dev/null
	@/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $(BUILD_NUMBER)" \
		$(APP)/Contents/Info.plist >/dev/null
	@# A Client ID in the ClientID file is baked into the build, so the people you
	@# add to your Spotify app just sign in. Absent, the app asks each user to
	@# register their own. Client IDs are public identifiers under PKCE — there is
	@# no secret here to leak.
	@if [ -s ClientID ]; then \
		/usr/libexec/PlistBuddy -c "Add :SpotifyClientID string $$(tr -d '[:space:]' < ClientID)" \
			$(APP)/Contents/Info.plist >/dev/null 2>&1 || true; \
		echo "    bundled Client ID: $$(tr -d '[:space:]' < ClientID | cut -c1-8)…"; \
	fi
	@if [ -s LastFMKey ] && [ -n "$$(tr -d '[:space:]' < LastFMKey)" ]; then \
		/usr/libexec/PlistBuddy -c "Add :LastFMAPIKey string $$(tr -d '[:space:]' < LastFMKey)" \
			$(APP)/Contents/Info.plist >/dev/null 2>&1 || true; \
		echo "    bundled Last.fm key: yes"; \
	fi
	@# iconutil is macOS-only and ships with the developer tools.
	@iconutil -c icns Icon.iconset -o $(APP)/Contents/Resources/Icon.icns 2>/dev/null \
		|| echo "    (icon skipped: iconutil unavailable)"
	@# Sparkle: framework in Frameworks/, feed and public key in the plist. The
	@# public key is not a secret — the matching private key stays in the login
	@# Keychain and only ever meets sign_update.
ifeq ($(HAS_SPARKLE),1)
	@mkdir -p $(APP)/Contents/Frameworks
	@cp -R $(SPARKLE_FW) $(APP)/Contents/Frameworks/
	@/usr/libexec/PlistBuddy -c "Add :SUFeedURL string $(FEED_URL)" \
		$(APP)/Contents/Info.plist >/dev/null 2>&1 || true
	@/usr/libexec/PlistBuddy -c "Add :SUEnableAutomaticChecks bool true" \
		$(APP)/Contents/Info.plist >/dev/null 2>&1 || true
	@# Daily. A screen saver is not software anyone wants nagging them hourly.
	@/usr/libexec/PlistBuddy -c "Add :SUScheduledCheckInterval integer 86400" \
		$(APP)/Contents/Info.plist >/dev/null 2>&1 || true
	@if [ -s SparklePublicKey ]; then \
		/usr/libexec/PlistBuddy -c "Add :SUPublicEDKey string $$(tr -d '[:space:]' < SparklePublicKey)" \
			$(APP)/Contents/Info.plist >/dev/null 2>&1 || true; \
		echo "    update feed: $(FEED_URL)"; \
	else \
		echo "    !! no SparklePublicKey — updates will be refused as unsigned."; \
	fi
endif
	$(SWIFTC) $(SWIFTFLAGS) $(APPFLAGS) \
		-o $(APP)/Contents/MacOS/AlbumCoverScreenSaver \
		$(SHARED) $(COMPANION)
	@# The saver rides inside the app. Sparkle can only replace the .app, so this
	@# is what lets an update reach the screen saver too — the app copies it into
	@# ~/Library/Screen Savers on the next launch. It also makes the DMG a single
	@# drag instead of an app plus a saver plus an installer script.
	@mkdir -p $(APP)/Contents/Resources
	@cp -R $(SAVER) "$(APP)/Contents/Resources/$(PRETTY).saver"
ifeq ($(HAS_SPARKLE),1)
	@tools/sign-sparkle.sh "$(APP)/Contents/Frameworks/Sparkle.framework" \
		"$(SIGN_ID)" $(CS_FLAGS)
endif
	@# No --deep: it would re-sign the nested framework and saver with the wrong
	@# flags. They are already signed; this seals them.
	@codesign --force --sign "$(SIGN_ID)" $(CS_FLAGS) $(APP)
	@echo "    -> $(APP)"

# ---------------------------------------------------------------- screen saver
# A .saver is a loadable bundle (MH_BUNDLE), which swiftc won't emit directly:
# compile to a single object with whole-module optimisation, then let clang do
# the bundle link.

saver: $(SAVBIN)

$(SAVBIN): $(SHARED) $(SAVERSRC) Saver/Info.plist Saver/Saver.entitlements Makefile
	@echo "==> building AlbumCoverScreenSaver.saver ($(ARCH))"
	@rm -rf $(SAVER)
	@mkdir -p $(BUILD) $(SAVER)/Contents/MacOS $(SAVER)/Contents/Resources
	@cp Saver/Info.plist $(SAVER)/Contents/Info.plist
	@/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $(VERSION)" \
		$(SAVER)/Contents/Info.plist >/dev/null
	@/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $(BUILD_NUMBER)" \
		$(SAVER)/Contents/Info.plist >/dev/null
	$(SWIFTC) $(SWIFTFLAGS) -whole-module-optimization -parse-as-library \
		-module-name AlbumCoverSaverModule -D SAVER_TARGET \
		-emit-object -o $(BUILD)/saver.o \
		$(SHARED) $(SAVERSRC)
	@test -d "$(SWIFTLIB)" || { \
		echo ""; \
		echo "!! Swift toolchain libraries not found at:"; \
		echo "   $(SWIFTLIB)"; \
		echo "   Run 'xcrun --find swiftc' and check the path, or build in Xcode instead."; \
		echo ""; exit 1; }
	$(CLANG) -bundle -isysroot $(SDK) -target $(TARGET) \
		-o $(SAVER)/Contents/MacOS/AlbumCoverScreenSaver \
		$(BUILD)/saver.o \
		-framework ScreenSaver -framework AppKit -framework Foundation \
		-L$(SWIFTLIB) -L$(SDK)/usr/lib/swift -L/usr/lib/swift \
		-Xlinker -rpath -Xlinker /usr/lib/swift
	@codesign --force --sign "$(SIGN_ID)" $(CS_FLAGS) \
		--entitlements Saver/Saver.entitlements $(SAVER)
	@echo "    -> $(SAVER)"

# ---------------------------------------------------------------- install

PRETTY := Album Cover Screen Saver

install: all
	@mkdir -p "$(HOME)/Library/Screen Savers"
	@# Clear both the old product name and any earlier copy of this one.
	@rm -rf "$(HOME)/Library/Screen Savers/SpotifyCollage.saver" \
		"$(HOME)/Library/Screen Savers/AlbumCoverScreenSaver.saver" \
		"$(HOME)/Library/Screen Savers/$(PRETTY).saver"
	@cp -R $(SAVER) "$(HOME)/Library/Screen Savers/$(PRETTY).saver"
	@rm -rf "/Applications/SpotifyCollage.app" \
		"/Applications/AlbumCoverScreenSaver.app" \
		"/Applications/$(PRETTY).app"
	@cp -R $(APP) "/Applications/$(PRETTY).app"
	@# macOS keeps the previously loaded .saver binary alive in these helpers, so a
	@# fresh install keeps running the OLD code until they restart. Ending them
	@# forces the new bundle to load; macOS relaunches both on demand.
	@pkill -f legacyScreenSaver 2>/dev/null || true
	@pkill -x WallpaperAgent 2>/dev/null || true
	@echo ""
	@echo "Installed version $(VERSION) (build $(BUILD_NUMBER))."
	@echo "  1. Open '/Applications/Album Cover Screen Saver.app'."
	@echo "  2. System Settings > Screen Saver > Album Cover Screen Saver."

# ---------------------------------------------------------------- distribution

DMG := $(BUILD)/AlbumCoverScreenSaver-$(VERSION).dmg
# Sparkle updates from a zipped .app: no disk image to mount, no drag, and the
# archive is the exact bundle that gets swapped in.
ZIP := $(BUILD)/AlbumCoverScreenSaver-$(VERSION).zip

dmg: all
	@echo "==> building $(DMG)"
	@rm -rf $(BUILD)/dmg "$(DMG)"
	@mkdir -p $(BUILD)/dmg
	@# One item to drag. The screen saver lives inside the app and installs itself
	@# on first launch, so there is nothing else in here to get wrong.
	@cp -R $(APP) "$(BUILD)/dmg/$(PRETTY).app"
	@cp "packaging/READ ME FIRST.txt" $(BUILD)/dmg/
	@ln -s /Applications $(BUILD)/dmg/Applications
	@hdiutil create -volname "Album Cover Screen Saver" -srcfolder $(BUILD)/dmg \
		-ov -format UDZO "$(DMG)" >/dev/null
	@rm -rf $(BUILD)/dmg
	@echo "    -> $(DMG)"
ifndef RELEASE
	@echo ""
	@echo "    This DMG is signed locally, not notarized: other Macs will warn."
	@echo "    See 'make release' and the README for the proper route."
endif

zip: all
	@rm -f "$(ZIP)"
	@# ditto, not zip(1): it is the only archiver that preserves the bundle's
	@# symlinks, extended attributes and — the part that matters — its signature.
	@ditto -c -k --keepParent $(APP) "$(ZIP)"
	@echo "    -> $(ZIP)"

# ---------------------------------------------------------------- shipping
#
#   make release                 patch bump, notarize, publish
#   make release PART=minor      2.1 -> 2.2
#   make ship                    same, without bumping (re-cut the current version)
#
# Credentials come from Release.config: DEVID, PROFILE, GH_OWNER, GH_REPO.

.PHONY: ship release-check notes

release-check:
	@test -n "$(DEVID)"  || { echo "!! DEVID not set. Run tools/release-setup.sh."; exit 1; }
	@test -n "$(PROFILE)"|| { echo "!! PROFILE not set. Run tools/release-setup.sh."; exit 1; }
	@test -n "$(GH_OWNER)$(GH_REPO)" || { echo "!! GH_OWNER/GH_REPO not set. Run tools/release-setup.sh."; exit 1; }
	@command -v gh >/dev/null || { echo "!! GitHub CLI not installed: brew install gh"; exit 1; }
	@test -d $(SPARKLE_FW) || { echo "!! Sparkle not vendored. Run tools/release-setup.sh."; exit 1; }
	@test -s SparklePublicKey || { echo "!! No SparklePublicKey. Run tools/release-setup.sh."; exit 1; }
	@security find-identity -v -p codesigning | grep -q "$(DEVID)" \
		|| { echo "!! No such signing identity: $(DEVID)"; \
		     security find-identity -v -p codesigning; exit 1; }
	@gh auth status >/dev/null 2>&1 || { echo "!! Not signed in to GitHub: gh auth login"; exit 1; }
	@# Check the notarytool credentials up front. Finding out they are missing
	@# happens otherwise only after a full clean rebuild, several minutes in.
	@xcrun notarytool history --keychain-profile "$(PROFILE)" >/dev/null 2>&1 || { \
		echo "!! notarytool profile '$(PROFILE)' does not work."; \
		echo "   Get an app-specific password at appleid.apple.com (Sign-In and"; \
		echo "   Security > App-Specific Passwords), then run:"; \
		echo ""; \
		echo "     xcrun notarytool store-credentials \"$(PROFILE)\" \\"; \
		echo "       --apple-id YOUR@EMAIL --team-id JVM7ZTCC5P --password xxxx-xxxx-xxxx-xxxx"; \
		echo ""; exit 1; }

# Release notes for this version, shown inside the updater and on the GitHub page.
NOTES := releases/notes/$(VERSION).md

notes:
	@mkdir -p releases/notes
	@test -s $(NOTES) || { \
		printf '## Version %s\n\n- \n' "$(VERSION)" > $(NOTES); \
		echo "!! Wrote a stub at $(NOTES) — fill it in, then run make ship."; \
		exit 1; }

ship: release-check notes
	@echo "==> shipping $(PRETTY) $(VERSION)"
	@# A full rebuild: changing the signing identity on the command line does not
	@# make existing products out of date, and they would carry the wrong signature.
	$(MAKE) clean
	$(MAKE) all RELEASE=1 SIGN_CERT="$(DEVID)" BUILD_NUMBER=$(BUILD_NUMBER)
	$(MAKE) zip BUILD_NUMBER=$(BUILD_NUMBER)
	@echo "==> notarizing the app (a few minutes)"
	xcrun notarytool submit "$(ZIP)" --keychain-profile "$(PROFILE)" --wait
	@# Staple the app, then re-zip: the ticket has to be inside the archive Sparkle
	@# hands to users, or every update is a fresh Gatekeeper prompt.
	xcrun stapler staple $(APP)
	$(MAKE) zip BUILD_NUMBER=$(BUILD_NUMBER)
	$(MAKE) dmg RELEASE=1 SIGN_CERT="$(DEVID)" BUILD_NUMBER=$(BUILD_NUMBER)
	@echo "==> notarizing the disk image"
	xcrun notarytool submit "$(DMG)" --keychain-profile "$(PROFILE)" --wait
	xcrun stapler staple "$(DMG)"
	@echo "==> appcast"
	@python3 tools/appcast.py add \
		--version "$(VERSION)" \
		--build "$$(/usr/libexec/PlistBuddy -c 'Print :CFBundleVersion' $(APP)/Contents/Info.plist)" \
		--archive "$(ZIP)" \
		--url "https://github.com/$(GH_OWNER)/$(GH_REPO)/releases/download/$(TAG)/$(notdir $(ZIP))" \
		--feed "$(FEED_URL)" \
		--notes "$(NOTES)" \
		--min-system 12.0
	@echo "==> publishing $(TAG)"
	@gh release view $(TAG) --repo $(GH_OWNER)/$(GH_REPO) >/dev/null 2>&1 \
		&& gh release delete $(TAG) --repo $(GH_OWNER)/$(GH_REPO) --yes --cleanup-tag || true
	gh release create $(TAG) --repo $(GH_OWNER)/$(GH_REPO) \
		--title "$(PRETTY) $(VERSION)" --notes-file $(NOTES) \
		"$(DMG)#Download for Mac (disk image)" \
		"$(ZIP)#Update archive" \
		"releases/appcast.xml#appcast.xml"
	@echo ""
	@echo "Published $(PRETTY) $(VERSION)."
	@echo "  download: https://github.com/$(GH_OWNER)/$(GH_REPO)/releases/latest"
	@echo "  feed:     $(FEED_URL)"
	@echo "  Existing users get it within a day, or immediately from Check for Updates."
	@$(MAKE) verify

# Confirms the products would actually pass Gatekeeper on someone else's Mac.
verify:
	@echo "==> signature"
	@codesign --verify --strict --verbose=2 $(APP) 2>&1 | tail -3
	@codesign --verify --strict --verbose=2 $(SAVER) 2>&1 | tail -3
	@echo "==> gatekeeper"
	@spctl --assess --type execute --verbose=2 $(APP) 2>&1 || true
	@echo "==> stapled ticket"
	@xcrun stapler validate "$(DMG)" 2>&1 | tail -2 || echo "(dmg not built or not stapled)"

# make bump              1.1 -> 1.1.1   (or 1.1.1 -> 1.1.2)
# make bump PART=minor   1.1.1 -> 1.2
# make bump PART=major   1.2 -> 2.0
bump:
	@python3 -c "\
import sys; \
v=open('VERSION').read().strip().split('.'); \
v=[int(x) for x in v]+[0]*(3-len(v)); \
part='$(PART)' or 'patch'; \
v=[v[0]+1,0,0] if part=='major' else ([v[0],v[1]+1,0] if part=='minor' else [v[0],v[1],v[2]+1]); \
s='.'.join(str(x) for x in (v[:2] if v[2]==0 else v)); \
open('VERSION','w').write(s+chr(10)); \
print('version is now '+s)"

# Bump, then cut and publish. The sub-make re-reads VERSION after the bump.
release: release-check bump
	@$(MAKE) ship

clean:
	@rm -rf $(BUILD)

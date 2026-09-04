#!/bin/bash
# One-time setup for shipping updates. Safe to re-run: every step checks first,
# and anything already done is skipped.
#
# Nothing here pauses waiting for you to run a command in another window — that
# only invites typing into the wrong prompt. Steps that need something from you
# are reported and collected, and the script tells you what to do at the end.
set -uo pipefail
cd "$(dirname "$0")/.."

bold() { printf '\n\033[1m%s\033[0m\n' "$1"; }
ok()   { printf '  \033[32m/\033[0m %s\n' "$1"; }
warn() { printf '  \033[33m!\033[0m %s\n' "$1"; }
die()  { printf '\n\033[31mx %s\033[0m\n\n' "$1"; exit 1; }

TODO=()
todo() { TODO+=("$1"); }

command -v xcrun >/dev/null || die "Xcode command line tools missing: xcode-select --install"

# ------------------------------------------------------------------ 1. Sparkle
bold "1. Sparkle"
if [ -d Vendor/Sparkle/Sparkle.framework ]; then
  ok "already vendored ($(cat Vendor/Sparkle/VERSION 2>/dev/null || echo 'version unknown'))"
else
  mkdir -p Vendor
  # Resolve whatever the current release is rather than pinning a number that
  # will be stale in a year. The tag lands in Vendor/Sparkle/VERSION so the
  # build stays reproducible after the fact.
  TAG=$(curl -fsSL https://api.github.com/repos/sparkle-project/Sparkle/releases/latest \
        | sed -n 's/.*"tag_name": *"\([^"]*\)".*/\1/p' | head -1)
  [ -n "${TAG}" ] || die "Could not reach GitHub to find the current Sparkle release."
  echo "  downloading Sparkle ${TAG}..."
  curl -fsSL "https://github.com/sparkle-project/Sparkle/releases/download/${TAG}/Sparkle-${TAG}.tar.xz" \
    -o /tmp/sparkle.tar.xz || die "Sparkle download failed."
  rm -rf Vendor/Sparkle && mkdir -p Vendor/Sparkle
  tar -xJf /tmp/sparkle.tar.xz -C Vendor/Sparkle || die "Could not unpack Sparkle."
  rm -f /tmp/sparkle.tar.xz
  echo "${TAG}" > Vendor/Sparkle/VERSION
  [ -d Vendor/Sparkle/Sparkle.framework ] || die "Sparkle.framework not where expected."
  ok "Sparkle ${TAG} in Vendor/Sparkle"
fi

# -------------------------------------------------------------- 2. update keys
bold "2. Update signing key"
GK=Vendor/Sparkle/bin/generate_keys
if [ -s SparklePublicKey ]; then
  ok "public key on file: $(cut -c1-16 < SparklePublicKey)..."
else
  # The private key goes into the login Keychain, never into the repo.
  PUB=$("$GK" -p 2>/dev/null | tr -d '[:space:]')
  if [ -z "${PUB}" ]; then
    echo "  generating a new key pair..."
    "$GK" >/tmp/sparklekeys.txt 2>&1
    PUB=$(grep -Eo '[A-Za-z0-9+/]{40,}=*' /tmp/sparklekeys.txt | head -1)
    rm -f /tmp/sparklekeys.txt
  fi
  [ -n "${PUB}" ] || die "generate_keys produced no public key."
  printf '%s\n' "${PUB}" > SparklePublicKey
  ok "key pair created; public half in ./SparklePublicKey"
  todo "Back up the private update key, then move the file off this Mac:
       ${GK} -x sparkle-private-key.txt
     Lose it and no future update will be accepted by anyone who already
     installed the app. It is the only unrecoverable thing in this project."
fi

# ------------------------------------------------------------- 3. Apple identity
bold "3. Apple Developer ID"
DEVID=$(security find-identity -v -p codesigning 2>/dev/null \
        | sed -n 's/.*"\(Developer ID Application: [^"]*\)".*/\1/p' | head -1)
if [ -n "${DEVID}" ]; then
  ok "${DEVID}"
else
  warn "no Developer ID Application certificate in your keychain"
  todo "Create a Developer ID Application certificate:
     Xcode > Settings > Accounts > Manage Certificates > + > Developer ID Application"
fi

TEAM=$(printf '%s' "${DEVID}" | sed -n 's/.*(\([A-Z0-9]*\))$/\1/p')
PROFILE=$(sed -n 's/^PROFILE *= *//p' Release.config 2>/dev/null | head -1)
PROFILE=${PROFILE:-albumcover}
if xcrun notarytool history --keychain-profile "${PROFILE}" >/dev/null 2>&1; then
  ok "notarytool profile '${PROFILE}' works"
else
  warn "no working notarytool profile named '${PROFILE}'"
  todo "Store your notarization credentials, once. Two ways -- the API key is
     the better one: it does not expire, it is scoped to notarization alone,
     and it does not care about your Apple ID password policy.

     A. App Store Connect API key (recommended)
        appstoreconnect.apple.com > Users and Access > Integrations > Keys
        Generate a Team Key with the 'Developer' role, download the .p8 (you
        get exactly one chance), and note the Key ID and the Issuer ID shown
        at the top of that page. Then:
          xcrun notarytool store-credentials \"${PROFILE}\" \\
            --key ~/Downloads/AuthKey_XXXXXXXXXX.p8 \\
            --key-id XXXXXXXXXX \\
            --issuer xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
        Afterwards, move the .p8 somewhere safe outside this project.

     B. App-specific password
        appleid.apple.com > Sign-In and Security > App-Specific Passwords.
          xcrun notarytool store-credentials \"${PROFILE}\" \\
            --apple-id YOUR@EMAIL --team-id ${TEAM:-TEAMID}

     Either way the profile is called '${PROFILE}' and nothing else changes."
fi

# ------------------------------------------------------------------- 4. GitHub
bold "4. GitHub"
OWNER=""
REPO=$(sed -n 's/^GH_REPO *= *//p' Release.config 2>/dev/null | head -1)
REPO=${REPO:-album-cover-screen-saver}

if ! command -v gh >/dev/null; then
  if command -v brew >/dev/null; then
    echo "  installing the GitHub CLI..."
    brew install gh >/dev/null || die "brew install gh failed."
    ok "gh installed"
  else
    todo "Install Homebrew (brew.sh), then: brew install gh"
  fi
fi

if command -v gh >/dev/null; then
  if gh auth status >/dev/null 2>&1; then
    OWNER=$(gh api user -q .login 2>/dev/null)
    ok "signed in as ${OWNER}"
  else
    # Deliberately not run from inside this script: gh auth login is a full
    # interactive menu, and nesting it under a script is how you end up typing
    # a notarytool command into an arrow-key prompt.
    warn "not signed in to GitHub"
    todo "Sign in to GitHub, on its own:
       gh auth login
     Answer: GitHub.com > HTTPS > yes to git credentials > login with a web browser."
  fi
fi

if [ -n "${OWNER}" ]; then
  printf '  repository name for releases [%s]: ' "${REPO}"
  read -r ANSWER
  REPO=${ANSWER:-$REPO}

  if gh repo view "${OWNER}/${REPO}" >/dev/null 2>&1; then
    ok "${OWNER}/${REPO} exists"
  else
    # Public, because release assets of a private repo are not publicly
    # downloadable -- which would make the update feed unreachable for everyone.
    echo "  creating public repo ${OWNER}/${REPO}..."
    gh repo create "${OWNER}/${REPO}" --public \
       --description "A macOS screen saver that collages your album art." \
       >/dev/null && ok "created" || warn "could not create it; make it yourself on github.com"
  fi

  if [ ! -d .git ]; then
    cat > .gitignore <<'IGNORE'
build/
Vendor/
releases/*.zip
.DS_Store
*.log
Release.config
# Baked into the binary at build time; no reason to publish them as text.
ClientID
LastFMKey
# The public half of the update key is safe to publish. The private half must
# never be committed -- back it up somewhere off this machine instead.
sparkle-private-key.txt
IGNORE
    git init -q
    git add -A
    git commit -qm "Album Cover Screen Saver" >/dev/null 2>&1
    git branch -M main
    git remote add origin "https://github.com/${OWNER}/${REPO}.git" 2>/dev/null
    if git push -qu origin main 2>/dev/null; then
      ok "source pushed to ${OWNER}/${REPO}"
    else
      warn "push failed -- releases still work; you can push later with: git push -u origin main"
    fi
  fi
fi

# ------------------------------------------------------------------ 5. config
bold "5. Release.config"
{
  echo "# Written by tools/release-setup.sh. Read by the Makefile; not committed."
  echo "DEVID    = ${DEVID}"
  echo "PROFILE  = ${PROFILE}"
  echo "GH_OWNER = ${OWNER}"
  echo "GH_REPO  = ${REPO}"
} > Release.config
ok "written"

# --------------------------------------------------------------------- wrap up
if [ ${#TODO[@]} -eq 0 ]; then
  bold "Ready."
  cat <<DONE
  Cut a release with:

      make release                 patch bump  (2.1 -> 2.1.1)
      make release PART=minor      2.1 -> 2.2

  Download page:  https://github.com/${OWNER}/${REPO}/releases/latest
  Update feed:    https://github.com/${OWNER}/${REPO}/releases/latest/download/appcast.xml
DONE
else
  bold "Still to do (${#TODO[@]}):"
  n=1
  for t in "${TODO[@]}"; do
    printf '\n  %d. %s\n' "$n" "$t"
    n=$((n + 1))
  done
  printf '\nThen run this script again -- it picks up where it left off.\n\n'
  exit 1
fi

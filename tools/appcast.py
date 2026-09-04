#!/usr/bin/env python3
"""Record a release and regenerate the Sparkle appcast.

releases/releases.json is the source of truth; appcast.xml is rebuilt from it
every time. Editing generated XML in place is how appcasts rot — one bad merge
and every user's updater sees a malformed feed — so nothing here parses the XML
it produced last time.

Usage:
  tools/appcast.py add --version 2.2 --build 20260904210530 \
      --archive build/AlbumCoverScreenSaver-2.2.zip \
      --url https://github.com/owner/repo/releases/download/v2.2/... \
      [--notes releases/notes/2.2.md] [--min-system 12.0]
"""
import argparse, json, os, subprocess, sys, datetime, html, re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RELDIR = os.path.join(ROOT, "releases")
DB = os.path.join(RELDIR, "releases.json")
OUT = os.path.join(RELDIR, "appcast.xml")
SIGN = os.path.join(ROOT, "Vendor", "Sparkle", "bin", "sign_update")

TITLE = "Album Cover Screen Saver"


def sign(archive):
    """EdDSA signature + byte length, from Sparkle's own signing tool.

    The private key lives in the login Keychain, never in the repo — sign_update
    is the only thing that touches it.
    """
    if not os.path.exists(SIGN):
        sys.exit("!! Sparkle's sign_update not found. Run tools/release-setup.sh first.")
    out = subprocess.run([SIGN, archive], capture_output=True, text=True)
    if out.returncode != 0:
        sys.exit("!! sign_update failed:\n" + (out.stderr or out.stdout))
    sig = re.search(r'sparkle:edSignature="([^"]+)"', out.stdout)
    length = re.search(r'length="(\d+)"', out.stdout)
    if not sig:
        sys.exit("!! could not read a signature out of sign_update:\n" + out.stdout)
    return sig.group(1), int(length.group(1)) if length else os.path.getsize(archive)


def notes_html(path):
    """Markdown-ish release notes to the small HTML subset Sparkle renders.

    Deliberately minimal: headings, bullets, paragraphs. Anything fancier is a
    reason to use a release-notes URL instead.
    """
    if not path or not os.path.exists(path):
        return ""
    lines, out, in_list = open(path).read().splitlines(), [], False
    for raw in lines:
        line = raw.rstrip()
        if not line:
            if in_list:
                out.append("</ul>")
                in_list = False
            continue
        if line.startswith("#"):
            if in_list:
                out.append("</ul>")
                in_list = False
            level = min(4, len(line) - len(line.lstrip("#")))
            out.append(f"<h{level+2}>{html.escape(line.lstrip('# ').strip())}</h{level+2}>")
        elif line.lstrip().startswith(("- ", "* ")):
            if not in_list:
                out.append("<ul>")
                in_list = True
            out.append(f"<li>{html.escape(line.lstrip()[2:].strip())}</li>")
        else:
            if in_list:
                out.append("</ul>")
                in_list = False
            out.append(f"<p>{html.escape(line)}</p>")
    if in_list:
        out.append("</ul>")
    return "\n".join(out)


def load():
    if os.path.exists(DB):
        return json.load(open(DB))
    return {"items": []}


def write_appcast(db, feed_url):
    # Newest first is not required by Sparkle, but it makes the file readable.
    items = sorted(db["items"], key=lambda i: float(i["build"]), reverse=True)
    parts = ['<?xml version="1.0" encoding="utf-8"?>',
             '<rss version="2.0" xmlns:sparkle="http://www.andymatuschak.org/xml-namespaces/sparkle">',
             '  <channel>',
             f'    <title>{html.escape(TITLE)}</title>',
             f'    <link>{html.escape(feed_url)}</link>',
             f'    <description>Updates for {html.escape(TITLE)}.</description>',
             '    <language>en</language>']
    for i in items:
        parts.append('    <item>')
        parts.append(f'      <title>Version {html.escape(i["version"])}</title>')
        parts.append(f'      <pubDate>{i["date"]}</pubDate>')
        parts.append(f'      <sparkle:version>{i["build"]}</sparkle:version>')
        parts.append(f'      <sparkle:shortVersionString>{html.escape(i["version"])}</sparkle:shortVersionString>')
        parts.append(f'      <sparkle:minimumSystemVersion>{i["minSystem"]}</sparkle:minimumSystemVersion>')
        if i.get("notes"):
            parts.append('      <description><![CDATA[')
            parts.append(i["notes"])
            parts.append('      ]]></description>')
        parts.append(f'      <enclosure url="{html.escape(i["url"])}" '
                     f'length="{i["length"]}" type="application/octet-stream" '
                     f'sparkle:edSignature="{i["signature"]}" />')
        parts.append('    </item>')
    parts += ['  </channel>', '</rss>', '']
    open(OUT, "w").write("\n".join(parts))
    return len(items)


def main():
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    a = sub.add_parser("add")
    a.add_argument("--version", required=True)
    a.add_argument("--build", required=True)
    a.add_argument("--archive", required=True)
    a.add_argument("--url", required=True)
    a.add_argument("--feed", default="")
    a.add_argument("--notes", default="")
    a.add_argument("--min-system", default="12.0")
    args = ap.parse_args()

    os.makedirs(RELDIR, exist_ok=True)
    db = load()
    signature, length = sign(args.archive)

    entry = {
        "version": args.version,
        "build": args.build,
        "date": datetime.datetime.now(datetime.timezone.utc)
                    .strftime("%a, %d %b %Y %H:%M:%S +0000"),
        "url": args.url,
        "length": length,
        "signature": signature,
        "minSystem": args.min_system,
        "notes": notes_html(args.notes),
    }
    # Re-releasing the same marketing version replaces its entry rather than
    # stacking a second one users would see as a separate update.
    db["items"] = [i for i in db["items"] if i["version"] != args.version]
    db["items"].append(entry)
    json.dump(db, open(DB, "w"), indent=2)
    n = write_appcast(db, args.feed)
    print(f"    appcast: {n} version(s), newest {args.version} -> {OUT}")


if __name__ == "__main__":
    main()

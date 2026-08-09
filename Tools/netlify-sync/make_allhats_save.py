#!/usr/bin/env python3
"""Seed a cloud save with every hat unlocked, via the deployed site's own
/.netlify/functions/sync endpoint - no build or deploy needed.

The hat list is parsed from HatManager.cs (catalog order), so the script
stays correct as hats are appended.

Merge onto your real save (recommended - keeps high score, notes, selection,
and your device's sync code identity):

    python3 Tools/netlify-sync/make_allhats_save.py \
        --site https://YOUR-SITE.netlify.app --code WOOF-4821

Or mint a standalone scratch save (fresh everything, just hats):

    python3 Tools/netlify-sync/make_allhats_save.py \
        --site https://YOUR-SITE.netlify.app --new-code ALLHATS-0000

Then on the phone: tap the title art five times -> sync panel -> enter the
code -> RESTORE (tap twice; the second tap confirms). The page reloads with
all 46 hats.
"""

import argparse
import json
import re
import sys
import urllib.request
from pathlib import Path

CODE_RE = re.compile(r"^[A-Z]{2,10}-\d{4}$")
CATALOG = Path(__file__).resolve().parents[2] / "Assets/Scripts/Core/HatManager.cs"


def catalog_ids():
    ids = re.findall(r'new HatDef\("(\w+)"', CATALOG.read_text())
    if len(ids) < 46:
        sys.exit(f"Parsed only {len(ids)} hat ids from {CATALOG} - catalog moved?")
    return ids


def fetch_save(site, code):
    with urllib.request.urlopen(f"{site}/.netlify/functions/sync?code={code}") as r:
        return json.load(r)["data"]


def post_save(site, code, data):
    body = json.dumps({"code": code, "data": data}).encode()
    req = urllib.request.Request(
        f"{site}/.netlify/functions/sync", data=body,
        headers={"Content-Type": "application/json"}, method="POST")
    with urllib.request.urlopen(req) as r:
        return json.load(r)


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--site", required=True, help="deployed origin, e.g. https://xxx.netlify.app")
    ap.add_argument("--code", help="existing sync code to merge onto (keeps score/notes/selection)")
    ap.add_argument("--new-code", help="code to write to (defaults to --code, else ALLHATS-0000)")
    ap.add_argument("--dry-run", action="store_true", help="print the save JSON, don't upload")
    args = ap.parse_args()

    site = args.site.rstrip("/")
    src = args.code.strip().upper() if args.code else None
    dst = (args.new_code or src or "ALLHATS-0000").strip().upper()
    for c in filter(None, (src, dst)):
        if not CODE_RE.match(c):
            sys.exit(f"Code '{c}' must match LETTERS-1234 (2-10 letters, 4 digits).")

    ids = catalog_ids()
    base = fetch_save(site, src) if src else {
        "v": 1, "highScore": 0, "hatsSelected": "", "notesUnlocked": "", "notesSeen": 0}

    base["hatsUnlocked"] = ",".join(ids)
    base["hatsSeen"] = len(ids)  # arrive pre-seen: no NEW-badge flood, no auto-wear

    print(f"{len(ids)} hats -> {dst}"
          f" (highScore {base.get('highScore', 0)},"
          f" notes {len([n for n in base.get('notesUnlocked', '').split(',') if n])})")
    if args.dry_run:
        print(json.dumps(base, indent=2))
        return

    post_save(site, dst, base)
    print(f"Uploaded. On the phone: tap the title 5x -> enter {dst} -> RESTORE (tap twice).")


if __name__ == "__main__":
    main()

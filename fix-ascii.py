#!/usr/bin/env python3
"""
Replace common non-ASCII typographic characters in .cs source files with
ASCII equivalents, clearing ASCII001 errors from Directory.Build.targets.

Walks all .cs files under the repo root, skipping:
  - obj/, bin/, .vs/, .git/, packages/, node_modules/
  - Files containing "// allow-non-ascii" within the first 4 lines.

For any non-ASCII character NOT in the replacement map, the file is left
alone for that character and the codepoint is reported at the end so you
can decide what to do (add to the map, edit by hand, or opt out the file).

Idempotent: running again after a successful pass is a no-op.

Usage:
    python fix-ascii.py                # rewrite files in place
    python fix-ascii.py --dry-run      # report only, no writes
    python fix-ascii.py --root PATH    # run against a different repo root
"""

import argparse
import os
import sys
from collections import Counter
from pathlib import Path

# Replacements keyed by Unicode codepoint (so the map is readable even when the
# source character is invisible, like zero-width spaces or directional marks).
REPLACEMENTS_BY_CP = {
    # ---------- Dashes ----------
    0x2010: "-",  # hyphen
    0x2011: "-",  # non-breaking hyphen
    0x2012: "-",  # figure dash
    0x2013: "-",  # en-dash
    0x2014: "-",  # em-dash - make singular
    0x2015: "-",  # horizontal bar - make singular
    # ---------- Quotes ----------
    0x2018: "'",  # left single quote
    0x2019: "'",  # right single quote
    0x201A: "'",  # single low-9
    0x201B: "'",  # single high-reversed
    0x201C: '"',  # left double quote
    0x201D: '"',  # right double quote
    0x201E: '"',  # double low-9
    0x201F: '"',  # double high-reversed
    0x2032: "'",  # prime
    0x2033: '"',  # double prime
    0x00AB: "<",  # left guillemet
    0x00BB: ">",  # right guillemet
    0x2039: "<",  # single left guillemet
    0x203A: ">",  # single right guillemet
    # ---------- Punctuation ----------
    0x2026: "...",  # horizontal ellipsis
    0x2022: "*",  # bullet
    0x00B7: "*",  # middle dot
    0x2027: "*",  # hyphenation point
    # ---------- Arrows ----------
    0x2190: "<-",
    0x2191: "^",
    0x2192: "->",
    0x2193: "v",
    0x2194: "<->",
    0x21D0: "<=",
    0x21D2: "=>",
    0x21D4: "<=>",
    # ---------- Math / symbols ----------
    0x00D7: "x",  # multiplication
    0x00F7: "/",  # division
    0x00B1: "+/-",  # plus-minus
    0x2212: "-",  # minus sign
    0x2260: "!=",  # not equal
    0x2264: "<=",
    0x2265: ">=",
    0x00B0: " deg",  # degree
    0x221E: "inf",  # infinity
    # ---------- Trade / copy ----------
    0x00A9: "(c)",
    0x00AE: "(R)",
    0x2122: "(TM)",
    0x2120: "(SM)",
    # ---------- Spaces (all -> regular space) ----------
    0x00A0: " ",  # non-breaking space
    0x2000: " ",
    0x2001: " ",
    0x2002: " ",
    0x2003: " ",
    0x2004: " ",
    0x2005: " ",
    0x2006: " ",
    0x2007: " ",
    0x2008: " ",
    0x2009: " ",
    0x200A: " ",
    0x202F: " ",  # narrow no-break space
    0x205F: " ",  # medium math space
    0x3000: " ",  # ideographic space
    # ---------- Invisible / control ----------
    0x200B: "",  # zero-width space
    0x200C: "",  # ZWNJ
    0x200D: "",  # ZWJ
    0x200E: "",  # LRM
    0x200F: "",  # RLM
    0x2028: "\n",  # line separator
    0x2029: "\n",  # paragraph separator
    0x202A: "",
    0x202B: "",
    0x202C: "",
    0x202D: "",
    0x202E: "",
    0xFEFF: "",  # BOM in mid-file (preserved at start; see has_bom)
    # ---------- Box-drawing ----------
    0x2500: "-",
    0x2501: "-",
    0x2502: "|",
    0x2503: "|",
    0x250C: "+",
    0x250D: "+",
    0x250E: "+",
    0x250F: "+",
    0x2510: "+",
    0x2511: "+",
    0x2512: "+",
    0x2513: "+",
    0x2514: "+",
    0x2515: "+",
    0x2516: "+",
    0x2517: "+",
    0x2518: "+",
    0x2519: "+",
    0x251A: "+",
    0x251B: "+",
    0x251C: "+",
    0x2524: "+",
    0x252C: "+",
    0x2534: "+",
    0x253C: "+",
    0x2550: "=",
    0x2551: "|",
    0x2552: "+",
    0x2553: "+",
    0x2554: "+",
    0x2555: "+",
    0x2556: "+",
    0x2557: "+",
    0x2558: "+",
    0x2559: "+",
    0x255A: "+",
    0x255B: "+",
    0x255C: "+",
    0x255D: "+",
    0x255E: "+",
    0x255F: "+",
    0x2560: "+",
    0x2561: "+",
    0x2562: "+",
    0x2563: "+",
    0x2564: "+",
    0x2565: "+",
    0x2566: "+",
    0x2567: "+",
    0x2568: "+",
    0x2569: "+",
    0x256A: "+",
    0x256B: "+",
    0x256C: "+",
}

REPLACEMENTS = {chr(cp): v for cp, v in REPLACEMENTS_BY_CP.items()}

SKIP_DIRS = {"obj", "bin", ".vs", ".git", "packages", "node_modules"}


def find_cs_files(root):
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for name in filenames:
            if name.endswith(".cs"):
                yield Path(dirpath) / name


def has_optout(text):
    head = "\n".join(text.splitlines()[:4])
    return "// allow-non-ascii" in head


def is_unwanted(ch):
    o = ord(ch)
    if ch in ("\r", "\n", "\t"):
        return False
    return o < 0x20 or o > 0x7E


def process_file(path, dry_run):
    raw = path.read_bytes()
    has_bom = raw.startswith(b"\xef\xbb\xbf")
    try:
        text = raw.decode("utf-8-sig" if has_bom else "utf-8")
    except UnicodeDecodeError as e:
        return {"path": path, "error": "not valid UTF-8: " + str(e)}

    if has_optout(text):
        return None

    new_text = text
    replaced = Counter()
    for k, v in REPLACEMENTS.items():
        if k in new_text:
            replaced[k] += new_text.count(k)
            new_text = new_text.replace(k, v)

    leftover = Counter(ch for ch in new_text if is_unwanted(ch))

    if not replaced and not leftover:
        return None

    if replaced and not dry_run:
        encoding = "utf-8-sig" if has_bom else "utf-8"
        path.write_bytes(new_text.encode(encoding))

    return {"path": path, "replaced": replaced, "leftover": leftover}


def fmt_counts(counter):
    return ", ".join("U+{:04X}x{}".format(ord(k), v) for k, v in counter.most_common())


def main():
    parser = argparse.ArgumentParser(
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument(
        "--dry-run", action="store_true", help="report only, don't modify files"
    )
    parser.add_argument("--root", default=".", help="repo root (default: cwd)")
    args = parser.parse_args()

    root = Path(args.root).resolve()
    if not root.is_dir():
        print("!! root is not a directory: " + str(root), file=sys.stderr)
        return 1

    files_changed = 0
    total_replaced = Counter()
    files_with_leftover = []
    errors = []

    for cs in find_cs_files(root):
        result = process_file(cs, args.dry_run)
        if result is None:
            continue
        if result.get("error"):
            errors.append((cs, result["error"]))
            continue

        rel = result["path"].relative_to(root)
        if result["replaced"]:
            files_changed += 1
            verb = "would fix" if args.dry_run else "fixed"
            print("{} {}: {}".format(verb, rel, fmt_counts(result["replaced"])))
            total_replaced.update(result["replaced"])
        if result["leftover"]:
            files_with_leftover.append((rel, result["leftover"]))

    prefix = "DRY-RUN " if args.dry_run else ""
    print()
    print("{}Files changed: {}".format(prefix, files_changed))
    print("{}Total replacements: {}".format(prefix, sum(total_replaced.values())))

    if total_replaced:
        print("By codepoint:")
        for k, v in total_replaced.most_common():
            print("  U+{:04X}  -> {!r:>8}  x{}".format(ord(k), REPLACEMENTS[k], v))

    if files_with_leftover:
        print()
        print("Non-ASCII characters NOT covered by the map (left untouched):")
        for rel, lc in files_with_leftover:
            print("  {}: {}".format(rel, fmt_counts(lc)))
        print()
        print("Add an entry to REPLACEMENTS_BY_CP, edit by hand, or opt-out the file")
        print("with '// allow-non-ascii' in the first 4 lines.")

    if errors:
        print()
        print("Errors:")
        for path, msg in errors:
            print("  {}: {}".format(path, msg))
        return 1

    return 2 if files_with_leftover else 0


if __name__ == "__main__":
    sys.exit(main())

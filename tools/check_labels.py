"""Validate filename labels against cards.json and report per-directory stats.

Contract mirrored from Ygo.CardLib (LabelCodec / CardDatabase.Normalize):
  {stem}__{name1}+{name2}+{name3}+{name4}+{name5}.ext ; SKIP_ prefix = skipped ;
  "?" / "？" = unknown slot ; illegal filename chars stored fullwidth (NFKC reverses).
Exit code 1 when any labeled name is not in the card database.
"""
import argparse
import json
import re
import sys
import unicodedata
from collections import Counter
from pathlib import Path

EXTS = {".png", ".jpg", ".jpeg", ".bmp"}
SEP = "__"
NAME_SEP = "+"
SKIP = "SKIP_"
SLOTS = 5


def normalize(s: str) -> str:
    t = unicodedata.normalize("NFKC", s)
    for a, b in (("—", "-"), ("–", "-"), ("−", "-"), ("‘", "'"), ("’", "'"), ("“", '"'), ("”", '"'), ("・", "·"), ("•", "·")):
        t = t.replace(a, b)
    return re.sub(r"\s+", "", t).lower()


def load_aliases(cards_path: Path) -> set[str]:
    cards = json.loads(cards_path.read_text(encoding="utf-8"))
    aliases = set()
    for c in cards.values():
        for key in ("md_name", "sc_name", "cn_name", "nwbbs_n", "cnocg_n"):
            v = c.get(key)
            if v:
                aliases.add(normalize(v))
    return aliases


def parse(stem: str):
    """Return (kind, names) where kind in {'labeled','skipped','unlabeled'}."""
    if stem.startswith(SKIP):
        return "skipped", None
    idx = stem.rfind(SEP)
    if idx <= 0:
        return "unlabeled", None
    parts = stem[idx + len(SEP):].split(NAME_SEP)
    if len(parts) != SLOTS:
        return "unlabeled", None
    names = [None if p in ("?", "？") else p for p in parts]
    return "labeled", names


def check_dir(d: Path, aliases: set[str]) -> tuple[dict, list[str]]:
    stats = Counter()
    invalid = []
    hands = Counter()
    files = sorted(p for p in d.iterdir() if p.suffix.lower() in EXTS)
    for p in files:
        kind, names = parse(p.stem)
        stats[kind] += 1
        if kind != "labeled":
            continue
        hands[tuple(names)] += 1
        for slot, raw in enumerate(names, 1):
            if raw is None:
                stats["unknown_slots"] += 1
            elif normalize(raw) not in aliases:
                invalid.append(f"{p.name} slot{slot}: {raw}")
    stats["total"] = len(files)
    dup = {h: n for h, n in hands.items() if n > 1}
    if dup:
        stats["duplicate_hands"] = len(dup)
    index = d / "labels.json"
    if index.exists():
        entries = json.loads(index.read_text(encoding="utf-8"))
        stale = [k for k in entries if not (d / k).exists()]
        if stale:
            stats["stale_index_keys"] = len(stale)
    return dict(stats), invalid


def main() -> int:
    repo = Path(__file__).resolve().parent.parent
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("dirs", nargs="*", type=Path,
                    default=[repo / "samples" / "own", repo / "samples" / "web"])
    ap.add_argument("--cards", type=Path, default=repo / "data" / "cards.json")
    args = ap.parse_args()
    aliases = load_aliases(args.cards)
    bad = 0
    for d in args.dirs:
        if not d.is_dir():
            print(f"[skip] not a directory: {d}")
            continue
        stats, invalid = check_dir(d, aliases)
        print(f"== {d}")
        print("   " + "  ".join(f"{k}={v}" for k, v in sorted(stats.items())))
        for line in invalid[:50]:
            print("   INVALID " + line)
        if len(invalid) > 50:
            print(f"   ... and {len(invalid) - 50} more")
        bad += len(invalid)
    print(f"invalid names total: {bad}")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
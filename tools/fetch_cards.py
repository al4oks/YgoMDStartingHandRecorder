"""Download the full YGO card database from ygocdb (cards.zip) with MD5 incremental check."""
import argparse
import hashlib
import json
import shutil
import sys
import tempfile
import urllib.request
import zipfile
from pathlib import Path

BASE = "https://ygocdb.com/api/v0/"
UA = {"User-Agent": "YgoHand-CardFetch/0.1 (personal local tool)"}


def http_get(url: str, timeout: int = 120) -> bytes:
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return resp.read()


def md5_of(path: Path) -> str:
    h = hashlib.md5()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--data", type=Path, default=Path(__file__).resolve().parent.parent / "data")
    ap.add_argument("--force", action="store_true", help="re-download even if MD5 unchanged")
    args = ap.parse_args()
    data: Path = args.data
    data.mkdir(parents=True, exist_ok=True)
    json_path = data / "cards.json"
    md5_path = data / "cards.zip.md5"

    published = http_get(BASE + "cards.zip.md5").decode("utf-8").strip().strip('"').lower()
    local = md5_of(json_path) if json_path.exists() else None
    if local == published and not args.force:
        print(f"up-to-date: cards.json md5={published}, no download")
        return 0

    print(f"downloading cards.zip (local={local}, published={published}) ...")
    zip_tmp = data / "cards.zip.tmp"
    zip_tmp.write_bytes(http_get(BASE + "cards.zip", timeout=300))
    with tempfile.TemporaryDirectory() as td:
        with zipfile.ZipFile(zip_tmp) as zf:
            zf.extract("cards.json", td)
        extracted = Path(td) / "cards.json"
        actual = md5_of(extracted)
        if actual != published:
            zip_tmp.unlink(missing_ok=True)
            print(f"MD5 MISMATCH: extracted={actual} published={published}", file=sys.stderr)
            return 1
        shutil.move(str(extracted), json_path)
    shutil.move(str(zip_tmp), data / "cards.zip")
    md5_path.write_text(published + "\n", encoding="utf-8")

    cards = json.loads(json_path.read_text(encoding="utf-8"))
    md = sum(1 for c in cards.values() if c.get("md_name"))
    print(f"ok: {len(cards)} cards, md_name coverage {md}, md5={published}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
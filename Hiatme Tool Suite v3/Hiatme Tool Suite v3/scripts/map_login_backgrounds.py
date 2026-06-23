#!/usr/bin/env python3
"""Map generated pop-culture theme PNGs onto all 244 login background filenames."""

from __future__ import annotations

import json
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "Resources" / "login_backgrounds"
CATALOG = Path(__file__).resolve().parent / "login_background_catalog.json"
THEMES_PER_LEVEL = 8
MAX_LEVEL = 30


def main() -> None:
    data = json.loads(CATALOG.read_text(encoding="utf-8"))
    slot_order: list[str] = data["slot_order"]
    if len(slot_order) < 68:
        raise SystemExit("slot_order needs at least 68 theme ids")

    OUT.mkdir(parents=True, exist_ok=True)

    # Build assignment for every theme slot -> pop culture id
    assignments: list[tuple[str, str]] = []

    classics = [
        ("classic-black-lime", "classic-black-lime"),
        ("classic-midnight", "classic-midnight"),
        ("classic-graphite", "classic-graphite"),
        ("classic-slate", "classic-slate"),
    ]
    assignments.extend(classics)

    idx = 4
    for level in range(1, MAX_LEVEL + 1):
        for index in range(THEMES_PER_LEVEL):
            theme_id = slot_order[idx % len(slot_order)]
            file_stem = f"L{level:02d}-{index:02d}"
            assignments.append((file_stem, theme_id))
            idx += 1

    missing = []
    copied = 0
    for file_stem, theme_id in assignments:
        src = OUT / f"{theme_id}.png"
        dst = OUT / f"{file_stem}.png"
        if not src.exists():
            missing.append(theme_id)
            continue
        shutil.copy2(src, dst)
        copied += 1

    print(f"Mapped {copied} files into {OUT}")
    if missing:
        unique_missing = sorted(set(missing))
        print(f"Missing {len(unique_missing)} source images — generate these first:")
        for m in unique_missing:
            print(f"  - {m}.png")


if __name__ == "__main__":
    main()

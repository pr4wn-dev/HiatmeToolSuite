#!/usr/bin/env python3
"""Download pop-culture login wallpapers from Wallhaven and map to all theme slots."""

from __future__ import annotations

import argparse
import json
import time
from io import BytesIO
from pathlib import Path

import requests
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "Resources" / "login_backgrounds"
CATALOG = Path(__file__).resolve().parent / "login_background_catalog.json"

THEMES_PER_LEVEL = 8
MAX_LEVEL = 30
OUTPUT_W, OUTPUT_H = 1600, 900

HEADERS = {"User-Agent": "HiatmeToolSuite/1.0 (login backgrounds)"}

SEARCH: dict[str, list[str]] = {
    "classic-black-lime": ["superman city", "superman"],
    "classic-midnight": ["batman gotham", "batman dark"],
    "classic-graphite": ["blade runner city", "cyberpunk neon city"],
    "classic-slate": ["avatar pandora", "bioluminescent forest"],
    "pop-superman": ["superman wallpaper", "superman"],
    "pop-batman": ["batman wallpaper", "batman"],
    "pop-spiderman": ["spider-man", "spiderman city"],
    "pop-wonder-woman": ["wonder woman", "wonder woman wallpaper"],
    "pop-iron-man": ["iron man", "iron man wallpaper"],
    "pop-captain-america": ["captain america", "captain america shield"],
    "pop-elm-street": ["freddy krueger", "nightmare elm street"],
    "pop-friday13": ["jason voorhees", "friday the 13th"],
    "pop-halloween": ["michael myers", "halloween horror"],
    "pop-grinch": ["the grinch", "grinch christmas"],
    "pop-scream": ["ghostface scream", "scream movie"],
    "pop-it-clown": ["pennywise", "it clown balloon"],
    "pop-shining": ["the shining", "shining hotel"],
    "pop-beetlejuice": ["beetlejuice", "beetlejuice movie"],
    "pop-alien": ["xenomorph alien", "alien movie"],
    "pop-exorcist": ["the exorcist", "exorcist horror"],
    "pop-star-wars": ["star wars tatooine", "star wars desert"],
    "pop-harry-potter": ["hogwarts", "harry potter castle"],
    "pop-lotr": ["hobbit shire", "lord of the rings"],
    "pop-matrix": ["matrix code", "matrix movie"],
    "pop-jurassic": ["jurassic park", "jurassic park t rex"],
    "pop-got": ["game of thrones", "winter is coming wall"],
    "pop-simpsons": ["simpsons", "springfield simpsons"],
    "pop-shrek": ["shrek", "shrek swamp"],
    "pop-toy-story": ["toy story", "buzz lightyear"],
    "pop-frozen": ["frozen elsa", "frozen disney ice"],
    "pop-lion-king": ["lion king", "pride rock"],
    "pop-top-gun": ["top gun", "fighter jet sunset"],
    "pop-rocky": ["rocky balboa steps", "rocky philadelphia"],
    "pop-godfather": ["the godfather", "godfather movie"],
    "pop-jaws": ["jaws shark", "jaws movie"],
    "pop-ghostbusters": ["ghostbusters", "ghostbusters slime"],
    "pop-back-to-future": ["delorean", "back to the future"],
    "pop-terminator": ["terminator", "terminator skull"],
    "pop-minecraft": ["minecraft", "minecraft landscape"],
    "pop-mario": ["mario bros", "super mario"],
    "pop-zelda": ["zelda hyrule", "legend of zelda"],
    "pop-pokemon": ["pokemon", "pokemon landscape"],
    "pop-gremlins": ["gremlins", "gremlins movie"],
    "pop-pulp-fiction": ["pulp fiction", "pulp fiction diner"],
    "pop-indiana-jones": ["indiana jones", "indiana jones temple"],
    "pop-robocop": ["robocop", "robocop movie"],
    "pop-e-t": ["e.t. bicycle moon", "et movie moon"],
    "pop-grease": ["grease movie", "drive in diner"],
    "pop-mean-girls": ["mean girls pink", "mean girls"],
    "pop-barbie": ["barbie pink", "barbie movie"],
    "pop-twilight": ["twilight movie", "twilight forest"],
    "pop-hunger-games": ["hunger games", "hunger games fire"],
    "pop-stranger-things": ["stranger things", "upsidedown"],
    "pop-breaking-bad": ["breaking bad", "breaking bad rv"],
    "pop-office": ["the office", "dunder mifflin"],
    "pop-friends": ["friends tv central perk", "central perk"],
    "pop-seinfeld": ["seinfeld", "seinfeld apartment"],
    "pop-spongebob": ["spongebob", "bikini bottom"],
    "pop-rick-morty": ["rick and morty", "rick morty portal"],
    "pop-deadpool": ["deadpool", "deadpool marvel"],
    "pop-wicked": ["wicked musical", "emerald city"],
    "pop-moana": ["moana disney", "moana ocean"],
    "pop-coco": ["coco pixar", "day of the dead"],
    "pop-inside-out": ["inside out pixar", "inside out emotions"],
    "pop-finding-nemo": ["finding nemo", "nemo reef"],
    "pop-cars": ["cars pixar", "radiator springs"],
    "pop-monsters-inc": ["monsters inc", "monsters inc doors"],
}


def wallhaven_search(query: str, retries: int = 5) -> str | None:
    """Search Wallhaven with progressively looser size filters — any resolution is OK."""
    size_filters = [None, "640x480", "800x600", "1280x720", "1920x1080"]

    for size in size_filters:
        for attempt in range(retries):
            try:
                params: dict[str, str] = {
                    "q": query,
                    "categories": "111",
                    "purity": "100",
                    "sorting": "relevance",
                    "order": "desc",
                }
                if size:
                    params["atleast"] = size

                r = requests.get(
                    "https://wallhaven.cc/api/v1/search",
                    params=params,
                    headers=HEADERS,
                    timeout=45,
                )
                if r.status_code == 429:
                    wait = 10 + attempt * 6
                    print(f"    rate limited ({size or 'any'}), wait {wait}s")
                    time.sleep(wait)
                    continue
                r.raise_for_status()
                data = r.json().get("data") or []
                if data:
                    return data[0]["path"]
                break  # empty results — try looser size filter
            except Exception as exc:
                print(f"    search error ({size or 'any'}): {exc}")
                time.sleep(3 + attempt * 2)
    return None


def download_image(url: str) -> Image.Image | None:
    try:
        r = requests.get(url, headers=HEADERS, timeout=180)
        r.raise_for_status()
        img = Image.open(BytesIO(r.content))
        img.load()
        return img.convert("RGB")
    except Exception as exc:
        print(f"    download error: {exc}")
        return None


def fit_cover(img: Image.Image, width: int, height: int) -> Image.Image:
    """Scale/crop any input dimensions to exact output size (no minimum source size)."""
    src_w, src_h = img.size
    if src_w < 1 or src_h < 1:
        raise ValueError("invalid image dimensions")

    scale = max(width / src_w, height / src_h)
    new_w = max(1, int(src_w * scale))
    new_h = max(1, int(src_h * scale))
    resized = img.resize((new_w, new_h), Image.Resampling.LANCZOS)

    left = max(0, (new_w - width) // 2)
    top = max(0, (new_h - height) // 2)
    return resized.crop((left, top, left + width, top + height))


def save_wallpaper(theme_id: str, img: Image.Image) -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    target = OUT / f"{theme_id}.png"
    fitted = fit_cover(img, OUTPUT_W, OUTPUT_H)
    fitted.save(target, "PNG", optimize=True)


def is_valid_wallpaper(path: Path) -> bool:
    if not path.exists() or path.stat().st_size < 1024:
        return False
    try:
        with Image.open(path) as img:
            w, h = img.size
            return w >= 32 and h >= 32
    except Exception:
        return False


def fetch_theme(theme_id: str, queries: list[str], force: bool) -> bool:
    target = OUT / f"{theme_id}.png"
    if not force and is_valid_wallpaper(target):
        print(f"  keep {theme_id}")
        return True

    for query in queries:
        print(f"  fetch {theme_id}: {query}")
        url = wallhaven_search(query)
        if not url:
            time.sleep(2.0)
            continue
        img = download_image(url)
        if img is None:
            time.sleep(2.0)
            continue
        try:
            save_wallpaper(theme_id, img)
        except Exception as exc:
            print(f"    save error: {exc}")
            time.sleep(2.0)
            continue
        print(f"    ok {img.size[0]}x{img.size[1]} <- {url}")
        time.sleep(2.0)
        return True

    print(f"    FAILED {theme_id}")
    return False


def safe_copy(src: Path, dst: Path) -> bool:
    if src.resolve() == dst.resolve():
        return True
    try:
        dst.write_bytes(src.read_bytes())
        return True
    except OSError as exc:
        print(f"    copy failed {dst.name}: {exc}")
        return False


def map_all_slots() -> tuple[int, list[str]]:
    data = json.loads(CATALOG.read_text(encoding="utf-8"))
    slot_order: list[str] = data["slot_order"]
    assignments: list[tuple[str, str]] = [
        ("classic-black-lime", "classic-black-lime"),
        ("classic-midnight", "classic-midnight"),
        ("classic-graphite", "classic-graphite"),
        ("classic-slate", "classic-slate"),
    ]
    idx = 4
    for level in range(1, MAX_LEVEL + 1):
        for index in range(THEMES_PER_LEVEL):
            theme_id = slot_order[idx % len(slot_order)]
            assignments.append((f"L{level:02d}-{index:02d}", theme_id))
            idx += 1

    missing = []
    copied = 0
    for file_stem, theme_id in assignments:
        src = OUT / f"{theme_id}.png"
        dst = OUT / f"{file_stem}.png"
        if not is_valid_wallpaper(src):
            missing.append(theme_id)
            continue
        if safe_copy(src, dst):
            copied += 1
    return copied, sorted(set(missing))


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--force", action="store_true", help="Re-download even if file exists")
    parser.add_argument("--map-only", action="store_true", help="Only copy sources to Lxx-xx slots")
    args = parser.parse_args()

    OUT.mkdir(parents=True, exist_ok=True)

    if not args.map_only:
        ok = fail = 0
        failed_ids: list[str] = []
        for theme_id, queries in SEARCH.items():
            if fetch_theme(theme_id, queries, force=args.force):
                ok += 1
            else:
                fail += 1
                failed_ids.append(theme_id)

        print(f"\nSources ok: {ok}, failed: {fail}")
        if failed_ids:
            print("Failed:", ", ".join(failed_ids))

    copied, missing = map_all_slots()
    print(f"Mapped {copied} slot files")
    if missing:
        print("Unmapped (missing source):", ", ".join(missing))


if __name__ == "__main__":
    main()

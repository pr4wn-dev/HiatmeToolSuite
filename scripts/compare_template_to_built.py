"""Compare Friday template CSVs to a built schedule xlsx (or May source xlsx).

Usage:
  python scripts/compare_template_to_built.py ^
    --templates "Hiatme Tool Suite v3/Hiatme Tool Suite v3/bin/Debug/Friday" ^
    --built "C:/Users/megap/Desktop/Schedule for June 5 2026.xlsx"

If --built is omitted, uses the May 15 2026 xlsx the templates were exported from
(sanity check only — not your June Supey build).
"""
from __future__ import annotations

import argparse
import csv
import re
from dataclasses import dataclass
from datetime import datetime, time
from pathlib import Path

import pandas as pd

SKIP_SHEETS = {"reserves", "schedule", "lgtc"}
ROSTER_MAP = {
    "aaron c": "Aaron N Cadwell",
    "dean d": "DEAN   DAVIS",
    "jamie b": "JAMIE   BROWN",
    "jeffrey b": "Jeffrey J Brown",
    "richard b": "RICHARD   BROWN",
    "remie d": "Remie R Deschaine",
    "shiloh m": "SHILOH  MCCAFFREY",
    "cherie g": "Cherie  Givens",
    "bobby y": "BOBBY  YONTZ",
}


def norm(s: str) -> str:
    return re.sub(r"\s+", " ", (s or "").strip()).lower()


def norm_time(raw: str) -> str:
    s = (raw or "").strip()
    if not s:
        return ""
    s = s.lower().replace(" ", "")
    if s in ("00:00", "00:00:00", "12:00am"):
        return "00:00"
    return s.lstrip("0")


def parse_time_to_minutes(raw: str) -> int | None:
    s = (raw or "").strip()
    if not s:
        return None
    for fmt in ("%I:%M %p", "%I:%M%p", "%H:%M", "%H:%M:%S"):
        try:
            t = datetime.strptime(s.upper(), fmt).time()
            return t.hour * 60 + t.minute
        except ValueError:
            continue
    m = re.match(r"^(\d{1,2}):(\d{2})", s)
    if m:
        return int(m.group(1)) * 60 + int(m.group(2))
    return None


def trip_key(row: dict) -> tuple:
    return (
        norm(row.get("client", "")),
        norm(row.get("pu_street", "")),
        norm(row.get("pu_city", "")),
        norm_time(row.get("pu_time", "")),
        norm(row.get("do_street", "")),
        norm(row.get("do_city", "")),
        norm_time(row.get("do_time", "")),
    )


def trips_match(a: dict, b: dict) -> bool:
    if a["key"] == b["key"]:
        return True
    # Parsed time equality fallback
    ta, tb = a.get("pu_min"), b.get("pu_min")
    da, db = a.get("do_min"), b.get("do_min")
    if ta is not None and tb is not None and ta == tb:
        if da is not None and db is not None and da == db:
            return (
                norm(a["client"]) == norm(b["client"])
                and norm(a["pu_street"]) == norm(b["pu_street"])
                and norm(a["pu_city"]) == norm(b["pu_city"])
                and norm(a["do_street"]) == norm(b["do_street"])
                and norm(a["do_city"]) == norm(b["do_city"])
            )
    return False


def is_gap(cells: list[str]) -> bool:
    if len(cells) < 14:
        cells = cells + [""] * (14 - len(cells))
    if not cells[0] and not cells[2] and not cells[3] and not cells[6]:
        return True
    return all(not (c or "").strip() for c in cells)


def row_from_cells(cells: list[str]) -> dict:
    if len(cells) < 14:
        cells = cells + [""] * (14 - len(cells))
    return {
        "trip": (cells[0] or "").strip(),
        "client": cells[2],
        "pu_street": cells[3],
        "pu_city": cells[4],
        "pu_time": cells[6],
        "do_street": cells[7],
        "do_city": cells[8],
        "do_time": cells[10],
    }


def enrich(t: dict) -> dict:
    k = trip_key(t)
    t = dict(t)
    t["key"] = k
    t["pu_min"] = parse_time_to_minutes(t["pu_time"])
    t["do_min"] = parse_time_to_minutes(t["do_time"])
    return t


@dataclass
class Slot:
    driver: str
    kind: str  # gap | trip
    trip: dict | None


def load_template_csv_dir(path: Path) -> dict[str, list[Slot]]:
    by_driver: dict[str, list[Slot]] = {}
    for csv_path in sorted(path.glob("*.csv")):
        tab = csv_path.stem.strip()
        if tab.startswith("_"):
            continue
        driver = ROSTER_MAP.get(tab.lower(), tab)
        slots: list[Slot] = []
        for line in csv_path.read_text(encoding="utf-8", errors="replace").splitlines():
            if not line.strip():
                slots.append(Slot(driver, "gap", None))
                continue
            cells = next(csv.reader([line]))
            if is_gap(cells):
                slots.append(Slot(driver, "gap", None))
                continue
            trip = row_from_cells(cells)
            if not trip["client"] and not trip["pu_street"]:
                continue
            slots.append(Slot(driver, "trip", enrich(trip)))
        by_driver[driver] = slots
    return by_driver


def load_xlsx_schedule(path: Path) -> dict[str, list[dict]]:
    by_driver: dict[str, list[dict]] = {}
    xl = pd.ExcelFile(path)
    for sheet in xl.sheet_names:
        if sheet.strip().lower() in SKIP_SHEETS:
            continue
        driver = ROSTER_MAP.get(sheet.strip().lower(), sheet.strip())
        df = pd.read_excel(path, sheet_name=sheet, header=None, usecols=range(14))
        trips = []
        for _, series in df.iterrows():
            cells = ["" if pd.isna(series.get(i)) else str(series.get(i)).strip() for i in range(14)]
            if is_gap(cells):
                continue
            trip = row_from_cells(cells)
            if not trip["client"] and not trip["pu_street"]:
                continue
            trips.append(enrich(trip))
        by_driver[driver] = trips
    return by_driver


def find_match(pool: list[dict], target: dict, used: set[int]) -> int | None:
    for i, p in enumerate(pool):
        if i in used:
            continue
        if trips_match(p, target):
            return i
    return None


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument(
        "--templates",
        default=r"Hiatme Tool Suite v3\Hiatme Tool Suite v3\bin\Debug\Friday",
    )
    ap.add_argument(
        "--built",
        help="Built schedule xlsx (e.g. after Supey SAVE). If omitted, uses May 15 2026 source.",
    )
    ap.add_argument(
        "--source-xlsx",
        default=r"C:\Users\megap\Desktop\Schedule for 2026\Schedule for May 15 2026.xlsx",
    )
    args = ap.parse_args()

    tpl_dir = Path(args.templates)
    built_path = Path(args.built) if args.built else Path(args.source_xlsx)
    if not tpl_dir.is_dir():
        raise SystemExit(f"Template folder not found: {tpl_dir}")
    if not built_path.is_file():
        raise SystemExit(f"Built/source xlsx not found: {built_path}")

    templates = load_template_csv_dir(tpl_dir)
    built = load_xlsx_schedule(built_path)

    print(f"Template folder: {tpl_dir}")
    print(f"Compared schedule: {built_path.name}")
    if not args.built:
        print("(WARNING: no --built file — comparing template to May SOURCE, not your June Supey build)")
    print()

    all_built: list[dict] = []
    for trips in built.values():
        all_built.extend(trips)
    used_global: set[int] = set()

    missing_on_built = []
    wrong_driver = []
    matched_ok = []

    for driver, slots in sorted(templates.items()):
        trip_slots = [s for s in slots if s.kind == "trip" and s.trip]
        built_on_driver = built.get(driver, [])
        used_local: set[int] = set()

        for slot in trip_slots:
            t = slot.trip
            idx = find_match(built_on_driver, t, used_local)
            if idx is not None:
                used_local.add(idx)
                matched_ok.append((driver, t["trip"], built_on_driver[idx]["trip"], "same driver"))
                continue
            gidx = find_match(all_built, t, used_global)
            if gidx is not None:
                used_global.add(gidx)
                other = None
                for d, blist in built.items():
                    for bt in blist:
                        if bt is all_built[gidx]:
                            other = d
                            break
                wrong_driver.append((driver, t, other, all_built[gidx]["trip"]))
            else:
                missing_on_built.append((driver, t))

    print("## Summary")
    tpl_trip_count = sum(
        1 for slots in templates.values() for s in slots if s.kind == "trip"
    )
    built_count = len(all_built)
    print(f"Template trip rows: {tpl_trip_count}")
    print(f"Trips on compared schedule: {built_count}")
    print(f"Matched on correct driver: {len(matched_ok)}")
    print(f"On WRONG driver (identity match elsewhere): {len(wrong_driver)}")
    print(f"NOT on any driver tab: {len(missing_on_built)}")
    print()

    roster_checked = set(ROSTER_MAP.values())
    for d in sorted(roster_checked):
        n_tpl = sum(1 for s in templates.get(d, []) if s.kind == "trip")
        n_built = len(built.get(d, []))
        flag = ""
        if n_tpl and n_built == 0:
            flag = " *** NO TRIPS ON BUILT SCHEDULE"
        elif n_tpl == 0 and n_built:
            flag = " (extra on built, not in template folder)"
        print(f"  {d}: template {n_tpl} rows, built sheet {n_built} trips{flag}")

    missing_files = [d for d in roster_checked if d not in templates and d not in ("Cherie  Givens", "BOBBY  YONTZ")]
    if "Cherie  Givens" not in templates:
        print("  Cherie  Givens: NO Friday CSV in template folder")
    if "BOBBY  YONTZ" not in templates:
        print("  BOBBY  YONTZ: NO Friday CSV in template folder")
    print()

    if wrong_driver:
        print("## On a different driver than template (should move?)")
        for exp_drv, t, act_drv, live_tn in wrong_driver[:40]:
            print(
                f"  Template {exp_drv}: {t['client'][:30]} PU {t['pu_time']} "
                f"-> built on {act_drv} as {live_tn}"
            )
        if len(wrong_driver) > 40:
            print(f"  ... +{len(wrong_driver) - 40} more")
        print()

    if missing_on_built:
        print("## Template rows NOT on built schedule (first 50)")
        for driver, t in missing_on_built[:50]:
            print(
                f"  {driver}: {t['trip']} | {t['client'][:35]} | "
                f"PU {t['pu_time']} {t['pu_street'][:25]} | DO {t['do_time']}"
            )
        if len(missing_on_built) > 50:
            print(f"  ... +{len(missing_on_built) - 50} more")
        print()

    extra_on_built = []
    matched_keys = {m[1] for m in matched_ok}
    for driver, blist in built.items():
        for bt in blist:
            found = any(
                trips_match(bt, {"key": trip_key(bt), **bt})
                for _, slots in templates.items()
                for s in slots
                if s.trip and s.driver == driver and trips_match(bt, s.trip)
            )
            if not found:
                extra_on_built.append((driver, bt))

    # Simpler extra detection
    used_tpl_keys = set()
    for slots in templates.values():
        for s in slots:
            if s.trip:
                used_tpl_keys.add(s.trip["key"])
    extra = []
    for driver, blist in built.items():
        for bt in blist:
            if bt["key"] not in used_tpl_keys:
                extra.append((driver, bt))
    if extra:
        print(f"## On built schedule but no template row matches ({len(extra)} trips)")
        for driver, bt in extra[:30]:
            print(f"  {driver}: {bt['trip']} | {bt['client'][:35]} | PU {bt['pu_time']}")
        if len(extra) > 30:
            print(f"  ... +{len(extra) - 30} more")


if __name__ == "__main__":
    main()

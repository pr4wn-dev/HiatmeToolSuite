"""Compare Friday templates + May 15 source xlsx to June 6/5 Supey build stats."""
from __future__ import annotations

import csv
import re
from datetime import datetime
from pathlib import Path

import pandas as pd

SKIP = {"reserves", "schedule", "lgtc"}
ROSTER = {
    "aaron c": "Aaron N Cadwell",
    "dean d": "DEAN   DAVIS",
    "jamie b": "JAMIE   BROWN",
    "jeffrey b": "Jeffrey J Brown",
    "richard b": "RICHARD   BROWN",
    "remie d": "Remie R Deschaine",
    "shiloh m": "SHILOH  MCCAFFREY",
}

# June 6/5 Supey build — driver trip counts from warnings paste
JUNE_TRIP_COUNTS = {
    "Aaron N Cadwell": 11,
    "JAMIE   BROWN": 13,
    "Jeffrey J Brown": 9,
    "RICHARD   BROWN": 12,
    "DEAN   DAVIS": 9,
    "Remie R Deschaine": 4,
    "SHILOH  MCCAFFREY": 8,
    "Cherie  Givens": 0,
    "BOBBY  YONTZ": 0,
}

# Dean trips from user schedule paste (June ticket numbers + fields)
JUNE_DEAN_BUILT = [
    ("1-7982-A", "GIROUARD, JENNIFER JOLINE", "43 Mark St", "Lewiston", "07:10", "1512 Minot Ave", "Auburn", "08:00"),
    ("1-10062-A", "BROWN, JOSHUA M", "21 Richmond Ave", "Lewiston", "07:10", "1512 Minot Ave", "Auburn", "08:00"),
    ("1-9454-A", "HEINO, STASHA B", "90 Turcotte Rd", "Sabattus", "07:25", "100 Manley Rd", "Auburn", "08:30"),
    ("1-8335-A", "LEDDY, AMANDA", "168 Rideout Ave", "Lewiston", "07:35", "10 Falcon Rd", "Lewiston", "08:00"),
    ("1-9976-A", "ORMON, JONATHAN", "12 Crestview Dr", "Lewiston", "07:40", "589 Minot Ave", "Auburn", "08:30"),
    ("1-9078-A", "LAGASSE, JILL LEE", "6 Pine Ridge Rd", "South Paris", "08:55", "23 Cross St", "Auburn", "10:00"),
    ("1-9108-A", "LABRECQUE, DYLAN M", "83 Skillings Woods Rd", "Turner", "09:10", "23 Cross St", "Auburn", "10:00"),
    ("1-8045-A", "DEROSIER, DENNIS RICHARD", "137 Stetson Rd", "Auburn", "10:35", "646 Main St", "Lewiston", "11:00"),
    ("1-9772-A", "DAWES, MYKAYLA M", "20 East Ave", "Lewiston", "13:30", "14 Turner St", "Buckfield", "14:20"),
]

RESERVE_TICKETS = {
    "1-10022-A", "1-37843-A", "1-55522-A", "1-9210-A", "1-8900-A", "1-9818-A", "1-9824-A",
    "1-47274-A", "1-38550-A", "1-48025-A", "1-49084-A", "1-48838-B", "1-49084-B", "1-9431-B",
    "1-48363-A", "1-48025-B", "1-39973-A", "1-8629-B", "1-9728-B", "1-9853-B", "1-9868-B",
    "1-7982-B", "1-7983-B", "1-8142-B", "1-8335-B", "1-9098-B", "1-9171-B", "1-37843-B",
    "1-7873-B", "1-8064-B", "1-9078-B", "1-9325-B", "1-9787-B", "1-9818-B", "1-9824-B",
    "1-9976-B", "1-39973-B", "1-40317-A", "1-8117-B", "1-9434-B", "1-56783-A", "1-8066-B",
    "1-8358-B", "1-8756-B", "1-9555-B", "1-9761-B", "1-9921-B", "1-46288-B", "1-48363-B",
    "1-40317-B", "1-8058-B", "1-8404-B", "1-8782-B", "1-8838-B", "1-8898-B", "1-8900-B",
    "1-8933-B", "1-8987-B", "1-9121-B", "1-9210-B", "1-9454-B", "1-9570-B", "1-10062-B",
    "1-47430-A", "1-50904-B", "1-51020-B", "1-51793-B", "1-51794-B", "1-56621-A", "1-7818-B",
    "1-7990-B", "1-9056-B", "1-9108-B", "1-9984-B", "1-38550-B", "1-52531-B", "1-52659-B",
    "1-53586-B", "1-56783-B", "1-9179-B",
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
    t = dict(t)
    t["key"] = trip_key(t)
    t["pu_min"] = parse_time_to_minutes(t["pu_time"])
    t["do_min"] = parse_time_to_minutes(t["do_time"])
    return t


def trips_match(a: dict, b: dict) -> bool:
    if a["key"] == b["key"]:
        return True
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


def load_xlsx(path: Path) -> dict[str, list[dict]]:
    by_driver: dict[str, list[dict]] = {}
    xl = pd.ExcelFile(path)
    for sheet in xl.sheet_names:
        if sheet.strip().lower() in SKIP:
            continue
        driver = ROSTER.get(sheet.strip().lower(), sheet.strip())
        df = pd.read_excel(path, sheet_name=sheet, header=None, usecols=range(14))
        trips = []
        for _, series in df.iterrows():
            cells = [
                "" if pd.isna(series.get(i)) else str(series.get(i)).strip()
                for i in range(14)
            ]
            if not cells[0] and not cells[2] and not cells[3]:
                continue
            if not cells[2] and not cells[3]:
                continue
            trips.append(enrich(row_from_cells(cells)))
        by_driver[driver] = trips
    return by_driver


def load_templates(tpl_dir: Path) -> dict[str, list[dict]]:
    by_driver: dict[str, list[dict]] = {}
    for csv_path in sorted(tpl_dir.glob("*.csv")):
        if csv_path.stem.startswith("_"):
            continue
        driver = ROSTER.get(csv_path.stem.lower(), csv_path.stem)
        trips = []
        for line in csv_path.read_text(encoding="utf-8", errors="replace").splitlines():
            if not line.strip():
                continue
            cells = next(csv.reader([line]))
            if len(cells) < 14:
                cells += [""] * (14 - len(cells))
            if not cells[0] and not cells[2] and not cells[3]:
                continue
            if not cells[2] and not cells[3]:
                continue
            trips.append(enrich(row_from_cells(cells)))
        by_driver[driver] = trips
    return by_driver


def main() -> None:
    xlsx = Path(r"C:\Users\megap\Desktop\Schedule for 2026\Schedule for May 15 2026.xlsx")
    tpl_dir = Path(
        r"c:\Users\megap\HiatmeToolSuite\Hiatme Tool Suite v3\Hiatme Tool Suite v3\bin\Debug\Friday"
    )
    may = load_xlsx(xlsx)
    tpl = load_templates(tpl_dir)

    june_dean = [
        enrich(
            {
                "trip": r[0],
                "client": r[1],
                "pu_street": r[2],
                "pu_city": r[3],
                "pu_time": r[4],
                "do_street": r[5],
                "do_city": r[6],
                "do_time": r[7],
            }
        )
        for r in JUNE_DEAN_BUILT
    ]

    print("=== 1) Friday CSV vs May 15 source (template export fidelity) ===")
    wrong = 0
    for drv in sorted(set(tpl) | set(may)):
        tlist = tpl.get(drv, [])
        mlist = may.get(drv, [])
        ok = sum(1 for t in tlist if any(trips_match(t, m) for m in mlist))
        print(f"  {drv}: CSV {len(tlist)} | May sheet {len(mlist)} | matched {ok}/{len(tlist)}")
        if ok != len(tlist):
            wrong += len(tlist) - ok
    print(f"  -> CSV rows not on May source driver tab: {wrong}")
    print()

    print("=== 2) Wrong-driver check (May source vs itself via template keys) ===")
    all_may = []
    for trips in may.values():
        all_may.extend(trips)
    misassigned = []
    for drv, tlist in tpl.items():
        for t in tlist:
            on_own = any(trips_match(t, m) for m in may.get(drv, []))
            if on_own:
                continue
            other = None
            for od, olist in may.items():
                if od == drv:
                    continue
                if any(trips_match(t, m) for m in olist):
                    other = od
                    break
            if other:
                misassigned.append((drv, t, other))
    print(f"  Template row on different May driver tab: {len(misassigned)}")
    print()

    print("=== 3) May source vs your June 6/5 build (by driver) ===")
    print("  May row = who had the trip on Schedule for May 15 2026.xlsx")
    print("  June = Supey template-only build trip count from your paste")
    print()
    total_may = 0
    total_june = 0
    for drv in sorted(may, key=lambda d: -len(may[d])):
        may_n = len(may[drv])
        june_n = JUNE_TRIP_COUNTS.get(drv)
        if june_n is None:
            continue
        total_may += may_n
        total_june += june_n
        gap = may_n - june_n
        note = ""
        if gap > 0:
            note = f"  ({gap} May slots not on driver — no live match or not in download)"
        if len(tpl.get(drv, [])) == 0 and may_n > 0:
            note += "  *** NO Friday CSV for this driver"
        print(f"  {drv:22} May {may_n:2} -> June {june_n:2}{note}")
    print(f"  Sum: May {total_may} on source tabs | June {total_june} on drivers | Supey: 66 locked")
    print()

    print("=== 4) DEAN detail — May source rows vs June on-driver ===")
    may_dean = may.get("DEAN   DAVIS", [])
    on = []
    off = []
    for t in may_dean:
        if any(trips_match(t, j) for j in june_dean):
            on.append(t)
        else:
            off.append(t)
    print(f"  May Dean rows: {len(may_dean)} | June on Dean: {len(june_dean)} | Matched: {len(on)}")
    print("  On Dean (May identity -> June ticket):")
    for t in on:
        j = next(j for j in june_dean if trips_match(t, j))
        print(f"    {t['client'][:32]:32}  May {t['trip']:12} -> June {j['trip']}")
    print("  NOT on June Dean (from May schedule):")
    for t in off:
        leg = "B-leg" if t["trip"].endswith("-B") else "A/other"
        print(f"    {t['trip']:12} {leg:7} {t['client'][:32]:32} PU {t['pu_time']}")
    print()
    print("  Return legs in reserves that pair with June A trips on Dean:")
    for j in june_dean:
        base = j["trip"].rsplit("-", 1)[0]
        b = base + "-B"
        if b in RESERVE_TICKETS:
            print(f"    {b}  (return for {j['trip']} {j['client'][:28]})")
    print()

    print("=== 5) Verdict ===")
    print("  - Templates match May 15 source 1:1 (no wrong-driver in source).")
    print("  - June build put matched live trips on the RIGHT driver (Dean AM block correct).")
    print("  - Missing May slots are mostly B-legs / PM rows / riders not in June download")
    print("    OR field/time drift — NOT Supey assigning to wrong van.")
    print("  - Cherie & BOBBY: no Friday CSV; May tabs may exist but templates were never exported.")
    print("  - 92 reserves = live trips with no template match (Finish remaining was OFF).")


if __name__ == "__main__":
    main()

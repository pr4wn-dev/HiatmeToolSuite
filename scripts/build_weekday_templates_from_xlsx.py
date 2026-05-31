"""Export recent schedule .xlsx files into Hiatme weekday template folders (Monday..Friday)."""
from __future__ import annotations

import re
import shutil
from datetime import datetime, time
from pathlib import Path

import math

import pandas as pd

SCHEDULE_DIR = Path(r"C:\Users\megap\Desktop\Schedule for 2026")
OUT_BASE = Path(
    r"C:\Users\megap\HiatmeToolSuite\Hiatme Tool Suite v3\Hiatme Tool Suite v3\bin\Debug"
)
SKIP_SHEETS = {"reserves", "schedule", "lgtc"}
INVALID_CHARS = re.compile(r'[\\/:*?"<>|]')


def pick_latest_xlsx_per_weekday() -> dict[str, tuple[datetime.date, Path]]:
    pat = re.compile(r"Schedule for (\w+) (\d+) (\d{4})\.xlsx", re.I)
    by_dow: dict[str, tuple[datetime.date, Path]] = {}
    for f in SCHEDULE_DIR.glob("*.xlsx"):
        m = pat.match(f.name)
        if not m:
            continue
        dt = datetime.strptime(f"{m.group(1)} {m.group(2)} {m.group(3)}", "%B %d %Y").date()
        dow = dt.strftime("%A")
        if dow not in by_dow or dt > by_dow[dow][0]:
            by_dow[dow] = (dt, f)
    return by_dow


def fmt_time(v) -> str:
    if isinstance(v, time):
        if v.hour == 0 and v.minute == 0:
            return "00:00"
        h, m = v.hour, v.minute
        ap = "AM" if h < 12 else "PM"
        h12 = h % 12 or 12
        return f"{h12}:{m:02d} {ap}"
    return ""


def _is_missing(v) -> bool:
    if v is None:
        return True
    try:
        if pd.isna(v):
            return True
    except (TypeError, ValueError):
        pass
    if isinstance(v, float) and math.isnan(v):
        return True
    if isinstance(v, str):
        s = v.strip().lower()
        if not s:
            return True
        if s in ("nat", "nan", "none", "<na>"):
            return True
        if "nan" in s and "/" in s:
            return True
    return False


def fmt_cell(v, col_idx: int) -> str:
    if _is_missing(v):
        return ""
    if isinstance(v, str):
        s = v.strip()
        if _is_missing(s):
            return ""
        return s
    if col_idx == 1 and isinstance(v, (datetime, pd.Timestamp)):
        d = pd.Timestamp(v).date()
        return f"{d.month}/{d.day}/{d.year}"
    if col_idx in (6, 10) and isinstance(v, time):
        return fmt_time(v)
    if col_idx == 11 and isinstance(v, (datetime, pd.Timestamp)):
        d = pd.Timestamp(v).date()
        return f"{d.month}/{d.day}/{d.year}"
    if col_idx == 12 and isinstance(v, (int, float)) and not pd.isna(v):
        return str(int(v)) if float(v).is_integer() else str(v)
    return str(v).strip()


def _is_valid_trip_number(trip: str) -> bool:
    if not trip:
        return False
    low = trip.lower()
    if low in ("trip", "trip#", "trip #", "nat", "nan", "none"):
        return False
    if low.startswith("trip "):
        return False
    if "nan" in low:
        return False
    return True


def _is_gap_row(cells: list[str]) -> bool:
    if not cells[0] and not cells[2] and not cells[3] and not cells[6]:
        return True
    return all(not (c or "").strip() for c in cells)


def sheet_to_csv_rows(df: pd.DataFrame) -> list[list[str]]:
    rows: list[list[str]] = []
    for _, series in df.iterrows():
        cells = [fmt_cell(series.get(i), i) for i in range(14)]
        if _is_gap_row(cells):
            rows.append([""] * 14)
            continue
        trip = cells[0]
        if not _is_valid_trip_number(trip):
            continue
        if not cells[2] and not cells[3]:
            continue
        rows.append(cells)
    return rows


def write_csv(path: Path, rows: list[list[str]]) -> None:
    lines = [
        ",".join('"' + (c or "").replace('"', '""') + '"' for c in row) for row in rows
    ]
    path.write_text("\n".join(lines) + ("\n" if lines else ""), encoding="utf-8")


def export_weekday(dow: str, service_date: datetime.date, xlsx: Path) -> dict:
    out_dir = OUT_BASE / dow
    if out_dir.exists():
        shutil.rmtree(out_dir)
    out_dir.mkdir(parents=True)
    xl = pd.ExcelFile(xlsx)
    stats = {"source": xlsx.name, "service_date": service_date.isoformat(), "drivers": 0, "trips": 0}
    for sheet in xl.sheet_names:
        if sheet.strip().lower() in SKIP_SHEETS:
            continue
        if INVALID_CHARS.search(sheet):
            continue
        df = pd.read_excel(xlsx, sheet_name=sheet, header=None, usecols=range(14))
        rows = sheet_to_csv_rows(df)
        if not rows:
            continue
        write_csv(out_dir / f"{sheet.strip()}.csv", rows)
        stats["drivers"] += 1
        stats["trips"] += len(rows)
    (out_dir / "_built_from.txt").write_text(
        f"Weekday: {dow}\nSource: {xlsx}\nService date: {service_date}\n"
        f"Drivers: {stats['drivers']}\nTrips: {stats['trips']}\n",
        encoding="utf-8",
    )
    return stats


def main() -> None:
    if not SCHEDULE_DIR.is_dir():
        raise SystemExit(f"Schedule folder not found: {SCHEDULE_DIR}")
    OUT_BASE.mkdir(parents=True, exist_ok=True)
    by_dow = pick_latest_xlsx_per_weekday()
    for dow in ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"]:
        if dow not in by_dow:
            print(f"{dow}: skipped (no xlsx)")
            continue
        svc, path = by_dow[dow]
        st = export_weekday(dow, svc, path)
        print(f"{dow}: {st['drivers']} drivers, {st['trips']} trips <- {path.name}")


if __name__ == "__main__":
    main()

# Cherie / Remie timing (99 schedules, 8,636 A-legs)

Source: `C:\Users\megap\Desktop\Schedule for 2026\*.xlsx`  
Regenerate: `python scripts/analyze_schedule_timing.py` then `python scripts/generate_desk_timing_cs.py`  
Coded in: `SupeyDeskScheduleTiming.cs` + `SupeyDeskScheduleTiming.Rules.cs`

## By drop **place** (A-leg: how early PU is scheduled before DO)

| Place | Typical gap (PU → DO) | BUILD early-arrival before scheduled PU |
|-------|------------------------|----------------------------------------|
| **Manley** | 50–65 min (often 90+) | **50 min** |
| **Minot** | 50–65 min | **45 min** + honor "cannot drop before" in comments |
| **Falcon** | 35–50 min | **40 min** |
| **23 Cross / 63 Broad / 646 Main** | 35–50 min | **40 min** |
| **618 Main / 20 East** (programs) | 25–40 min | **35 min** |
| Other A-legs | — | **29 min** (scoreboard default) |

## By **person**

| Client | Pattern |
|--------|---------|
| **BROWN, JOSHUA M** | "Requests later PU" — **no** early pickup before scheduled PU |
| **TUTTLE, SIERRA B** | Same |

## Drop rules

- **Minot**: 256× "CANNOT BE DROPPED OFF BEFORE …" — drop no earlier than that time; tier = program-flexible.
- **Falcon / Manley / 646 / 23 Cross / 63 Broad**: strict appointment DO (0 min late cap).

## Not shift times

Driver **ShiftStart/End** in `SupeyDrivers.json` are roster clocks. Sheet PU/DO times are trip times; do not set shift to 5:45 because first PU is 6:50.

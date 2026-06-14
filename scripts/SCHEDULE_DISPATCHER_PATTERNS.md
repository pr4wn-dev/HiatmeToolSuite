# Dispatcher patterns from real schedules

Source: `C:\Users\megap\Desktop\Schedule for 2026` — **117** workbook days, **18,585** trips, **6,980** route groups.

## How groups are drawn on the sheet

- **Blank row = new group** in **88%** of groups (explicit route break).
- Groups with **90+ min pickup gaps** inside one block (would split if inferred from time only): **84** cases — dispatchers often **keep one group** anyway.
- Typical group size: 1 trips×2429, 2 trips×1623, 3 trips×1059, 4 trips×705, 5 trips×518, 6 trips×398.

## Geography — dispatchers routinely batch distant towns

- **36.2%** of groups pick up in **2+ cities**.
- **10.4%** pick up in **3+ cities** (Poland + Mechanic Falls + Lewiston in one morning batch is normal).
- PU span inside a group: **p75 30 min**, max **575 min**.
- Desk order ≠ PU-time order in **1016** groups vs chronological **3044** — **route order on sheet is intentional**, not sort-by-PU.

### Example multi-city groups

- **Remie D** (Schedule for April 1 2026.xlsx): PU cities ['AUBURN', 'MECHANIC FALLS', 'NEW GLOUCESTER', 'POLAND'], span 30 min
- **Bobby Y** (Schedule for April 1 2026.xlsx): PU cities ['HARTFORD', 'OXFORD', 'SOUTH PARIS'], span 15 min
- **Cherie G** (Schedule for April 1 2026.xlsx): PU cities ['GREENE', 'SABATTUS', 'WALES'], span 20 min
- **Jeffrey B** (Schedule for April 1 2026.xlsx): PU cities ['AUBURN', 'LEWISTON', 'LISBON FALLS', 'SABATTUS'], span 60 min
- **Aaron C** (Schedule for April 1 2026.xlsx): PU cities ['GREENE', 'LEEDS', 'LEWISTON'], span 25 min

## Timing tricks

- **Shared drop time batches**: {2: 611, 3: 481, 4: 354, 5: 162, 6: 21} — many A-legs share **8:00 AM DO** in one group.
- **8:00 AM DO waves** (A-leg only): {3: 171, 4: 144, 2: 120, 5: 74, 6: 12}.
- First trip on sheet vs earliest PU in group: **10.0%** of groups start desk list **before** the earliest scheduled PU (early pickup / different first stop).
- Inter-group: prior DO → next PU p75 **50 min**; **1907** transitions under 30 min; **737** where next PU is *before* prior DO (overlap batches / separate groups on same driver).

## Notes in trip comments (col 13+)

- **door to door**: 3899× — e.g. `1974-04-15 00:00:00 1 SO/ATS/DOOR TO DOOR`
- **cannot leave unattended**: 2411× — e.g. `1997-09-30 00:00:00 15 SO/UTS/CANNOT BE LEFT UNATTENDED`
- **one way**: 463× — e.g. `1986-02-19 00:00:00 21 SO/ONE WAY/ALONE/UTS/DOOR TO DOOR/NON VERBAL/CANT LEAVE UNATTENDED/…`
- **non verbal**: 428× — e.g. `1986-02-19 00:00:00 21 SO/ONE WAY/ALONE/UTS/DOOR TO DOOR/NON VERBAL/CANT LEAVE UNATTENDED/…`
- **cannot drop before**: 279× — e.g. `1991-10-22 00:00:00 15 SO/ ATS/ DOOR-TO-DOOR/ CANNOT BE DROPPED OFF BEFORE 1030`
- **needs assistance**: 225× — e.g. `1988-06-21 00:00:00 10 SO/ ATS/ NEEDS ASSISTANCE OPENING VEHICLE DOOR`
- **requests later pu**: 178× — e.g. `1989-04-28 00:00:00 6 SO/ATS/DOOR TO DOOR/REQUESTS LATER PU`
- **pick up at door**: 111× — e.g. `1978-03-12 00:00:00 7 SO/ATS/PICK UP AT DOOR NOT GATE`
- **call first**: 70× — e.g. `1965-06-10 00:00:00 3 CELL Y TEXT Y/CALL BEFORE/UPON ARRIVAL/`
- **ready for pu at**: 43× — e.g. `1966-10-23 00:00:00 1 SO/ONE WAY/READY FOR PU AT 2PM/ALONE/ATS`
- **wheelchair**: 2× — e.g. `1965-04-30 00:00:00 10 BARIATRIC MWC/265LBS/5'1/TF/`

## A + B same driver same day

Clients with both A-leg (morning) and B-leg (return) on one driver sheet:

- THEBERGE, STACY L: 111 schedule-days
- QUADROS, LAURA L: 100 schedule-days
- JOLICOEUR, JEFFREY: 99 schedule-days
- HOLT, CARMAN G: 68 schedule-days
- PHILLIPS, MICAH J: 67 schedule-days
- CARLMARK, ZOEY LAUREN: 60 schedule-days
- BURKHARDT, JOEL DAVID: 37 schedule-days
- TANCREL, LINDSEY M: 37 schedule-days
- LOZIER, SUSAN A.: 34 schedule-days
- SWAN, SHERI S: 33 schedule-days

## Implications for suggest-driver

1. **Do not require PU-time sort** — honor desk/route order; BUILD uses OSRM tour order.
2. **Multi-city morning batches are valid** — reject merge only when OSRM tour fails, not when cities look far apart on a map.
3. **Blank row groups** — prefer `NewGroupAfterGroup` with explicit gaps; allow overlap transitions when inter-group timing shows next PU before prior DO (separate batches).
4. **Comment-driven rules** — `REQUESTS LATER PU` = no early PU; `CANNOT BE DROPPED OFF BEFORE` = program DO floor; `READY FOR PU AT` = hard PU floor.
5. **8:00 DO clusters** — scoring should prefer merging into groups that share DO anchor times.

Regenerate: `python scripts/analyze_schedule_patterns.py`

# Cherie / Remie schedule patterns (for tuning Supey — not auto-coded)

Read from `C:\Users\megap\Desktop\Schedule for 2026\` sample workbooks. Use this to adjust **roster shifts**, **early PU/DO rules**, and **BUILD** — not regex in C# until you agree.

## Day span vs Supey roster (06:00–18:00)

Real sheets often run **~6:40 first PU → ~4:30–5:30 PM last DO** (sometimes later). Examples:

| Sheet (sample) | First PU | Last DO |
|----------------|----------|---------|
| Remie D | 6:55 | 3:30 PM |
| Aaron C | 6:45 | 4:30 PM |
| Jeffrey B | 6:40 | 2:00 PM |
| Dean D | 7:05 | 2:25 PM |

**Tune:** Per-driver shift start/end on roster (or template-only BUILD + manual review), not a global 06:00–18:00 if that rejects good mornings.

## Instruction rows (no trip #)

Dispatchers put **timing / grouping hints between trips**, e.g.:

- `6:50 PICK UP`, `7:00 PICK UP`, `10:30 PICK UP`
- `PICK UP TOGETHER`, `pick up togther in order all going to Auburn`
- `PICK UP WITH PEPIN`, `8:30 PICK UP FOR BOTH`
- `645 am start`, `COME TO OFFICE`, `Round Trip`, `GET BURKHARDT`

**Tune:** These are **human instructions** — show in preview (done: kept as gap text), do **not** auto-parse into BUILD until rules are defined.

## Trip comments (column N) — recurring phrases

| Pattern | Meaning for BUILD |
|---------|-------------------|
| `CANNOT BE DROPPED OFF BEFORE ####` | Earliest drop time (not “late deadline”) |
| `CANNOT ARRIVE BEFORE …` | Earliest PU / arrive time |
| `REQUESTS LATER PU` | Do not treat early Modivcare PU as real |
| `CANNOT BE LEFT UNATTENDED` / `ENSURE STAFF` | Program-style drop; staff must be there |
| `DOOR TO DOOR` / `PICK UP AT DOOR NOT GATE` | Operational, not timing |
| Clinic / hub names in comments | Often pairs with strict DO |

**Tune:** Decide per phrase in `SupeyTripTimingPolicy` / roster — **after** review, not guessed (e.g. hardcoded 7:45 for “later PU” was wrong).

## Early PU on A-legs

Sheets often show PU **35–60+ minutes before** DO on A-legs (intentional “early pickup”).

**Tune:** Existing **29 min early PU/DO** compression in BUILD may be too small for some riders; may need tier by facility or comment, not one global number.

## What Supey should do next (your call)

1. Set roster shifts from sheet min PU / max DO per driver (manual in Supey driver editor).
2. Agree which comment phrases change PU floor, DO floor, or tier.
3. Only then add code — small, explicit rules in `SupeyTripTimingPolicy` / `McTripTimingRules`, not a “note parser” class.

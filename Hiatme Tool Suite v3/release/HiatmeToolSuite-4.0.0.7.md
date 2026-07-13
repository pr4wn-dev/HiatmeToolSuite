What's new in 4.0.0.7:

Schedule Builder — Modivcare trip fields
- Fix header fuzzy-match that put Age/State/Gender into Date/PU Time/DO City (showing "A", "ME", "F"/"M")
- Modivcare new-trip sync repairs already-saved corrupt trips by matching trip # to a fresh download

Schedule Builder — trip alerts (match Analyzer)
- MWC, Child, Time, Address, and other analyzer alerts now run on Reserves trips too
- Alert pass downloads live Modivcare data (not just saved file comments)
- LBS alert wired up; will-call-on-driver-tab uses 0:00 / 00:00 detection
- Alert failures show in the status bar instead of failing silently

Schedule Builder — notes
- Add/edit note dialog: "Center text in row" checkbox (preview + saved workbook)
- Edit group note works on blank group-color bars (creates a real group header row)
- Group note row color can override group color; center alignment persists on export

Includes all 4.0.0.5 features (Modivcare M/d/yyyy dates, H:mm times, Reserves section counts, multi-select cancels/reroutes).

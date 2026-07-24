What's new in 4.0.0.14:

Driver Habits
- Faster day load / Refresh: reuses the printed schedule workbook instead of re-parsing Excel every time
- Parses the workbook off the UI thread when a reload is needed
- Driver strip sorts by late/early event counts first (minutes are a tiebreaker), so multi-hit days rank worse than one long late
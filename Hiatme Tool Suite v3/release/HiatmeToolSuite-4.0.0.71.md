# Hiatme Tool Suite 4.0.0.71

**Van capacity and shift hours are shared now.** Saving a roster used to write only
this PC's `SupeyDrivers.json`, so a van set to five seats on one desk stayed four
everywhere else. Opening Schedule Builder or Supey now pulls the shared roster from
the panel, and saving pushes it so the next desk sees the same numbers. The local file
is still written and is what the app uses if the panel is down.

Capacity is not enforced until someone actually types a number. The boards show most
vans have carried six or seven at once, so do not leave a van at the old default of
four unless that is really how many seats it has.

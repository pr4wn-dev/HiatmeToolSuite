What's new in 4.0.0.0:

Schedule Builder — Cancels & highlighting
- New Reserves → Cancels section (violet) with right-click "Add to Cancels section" — only the selected trip moves; partner legs stay put
- WellRyde cancelled/suspended trips highlighted on load and BUILD (fails soft when WellRyde is unavailable)
- Modivcare reroute (red) and WellRyde cancel highlights stay stable across edits
- Leg-aware (A/B/C) trip matching — partner legs are never moved or mismatched
- Cancels section ordered above Reroutes
- Verified Modivcare reroutes cached to the reroute registry — future loads skip re-checking known reroutes

Schedule Builder — Modivcare reroute
- Reroute confirmation loads reason options live from Modivcare; pick from a themed dropdown before submit
- Success and error dialogs use themed Supey popups with dark scrollbars (no system MessageBox)

Schedule Builder — cut / paste
- Persistent cut-trip banner under driver tabs shows trip #, leg, client, PU/DO times, and addresses on every tab until paste or clear

Also in recent 3.0.1.x builds bundled into this release
- Modivcare reroute verify on schedule load
- Shared ListView column widths saved in workbooks
- Office Gmail defaults and schedule email recipient picker

What's new in 4.0.0.3:

Updater
- Release notes carousel (Prev / Next) while the download runs
- Download first, then RESTART TO INSTALL when you are ready
- Update waits for the app to fully exit and unlock files before installing
- Launch button after install (avoids file-in-use on restart)
- Updater dependencies ship correctly so Update.exe starts reliably

Schedule Builder â€” map and mileage
- Batch geocode and OSRM preload for all driver tabs with per-tab map cache
- Loading spinner on the map only (not the whole app)
- Mileage and efficiency more reliable when pins exist on the map
- Tab switch selects the first trip (not a note or gap row)
- Group legend shows all groups when viewing all driver trips
- Auto-sort group uses map geocodes (no longer fails when routes already draw)

Schedule Builder â€” placement
- Suggest driver for trip on right-click (ranked placements, apply to the live list)
- Auto-sort group for best route efficiency

Schedule Builder â€” Modivcare new trips
- Pull new Modivcare trips into Reserves on schedule load
- Manual toolbar button to check for new trips
- Smarter matching so on-schedule and rerouted trips are not duplicated
- Result dialog and bar to browse newly added trips

Schedule Builder â€” list and UI
- Trip Alerts column (cancel, reroute, address, time, WC, and related icons)
- Add row above / below submenu
- Trailing blank rows at end of each driver tab
- Clearer load progress; WellRyde failures fail soft
- Theme-proof dark scrollbars and themed dialogs

Schedule Builder â€” email
- Themed send progress dialog
- Send log

Trip Scout
- Card toolbar layout, live bell and scan controls, HUD and date picker polish

Login and connectivity
- Login defaults to WellRyde (not Gmail when office Gmail is configured)
- Panel URL prefers public/home address; office LAN and localhost as fallbacks

Analyzer
- Correctly counts Schedule Builder note rows

Includes all 4.0.0.1 features (office Gmail defaults, Cancels, reroute tools, cut-trip banner, and prior Schedule Builder work).

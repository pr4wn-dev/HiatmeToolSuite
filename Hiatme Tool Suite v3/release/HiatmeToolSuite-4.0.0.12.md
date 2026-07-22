What's new in 4.0.0.12:

Updater â€” real fix for broken RESTART TO INSTALL
- In-app update no longer runs the old Update.exe from the install folder (that binary was dying silently)
- Fresh Update.exe is extracted from the downloaded zip into TEMP and run as the worker
- App auto-relaunches after install, with a harder fallback if the first start fails

Stuck on an older build? You do NOT need a working in-app update first:
1. Download HiatmeApplyUpdate.exe from https://hiatme.com/downloads/hiatme-tool-suite/
2. Run it â†’ DOWNLOAD & INSTALL LATEST
3. It finds your install, closes the app, installs, and reopens

Also upload HiatmeApplyUpdate.exe next to the zip on the downloads page.

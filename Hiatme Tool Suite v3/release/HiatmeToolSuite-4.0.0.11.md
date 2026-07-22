What's new in 4.0.0.11:

Updater fix (important)
- RESTART TO INSTALL was failing silently on many desks: Update.exe depended on MaterialSkin.dll that was not next to it, so the updater died after the app closed and never applied the zip
- Update.exe is now plain WinForms (no MaterialSkin)
- The app always refreshes Update.exe from the downloaded zip before handoff
- App auto-relaunches after a successful install (Launch button still available as fallback)

Driver Discipline (from 4.0.0.10)
- Shared write-up library, live history filter while typing driver name, themed menus/dialogs

If a desk is still stuck on an older build and Restart does nothing:
1. Close Hiatme Tool Suite
2. Delete Update.exe from the install folder
3. Open the app Ã¢â€ â€™ Check for updates Ã¢â€ â€™ download 4.0.0.11 Ã¢â€ â€™ RESTART TO INSTALL
   (Deleting Update.exe forces a fresh updater to be pulled out of the zip.)

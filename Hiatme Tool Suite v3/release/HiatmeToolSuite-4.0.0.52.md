What's new in 4.0.0.52:

Schedule share
- AutoSave and SAVE publish the workbook to the server before they finish
- LOAD pulls when the published file is newer, or when the local xlsx does not match the published hash
- Saving replaces the xlsx in place so OneDrive / Office does not keep the old cached copy
- A stale Desktop copy cannot overwrite a newer published schedule

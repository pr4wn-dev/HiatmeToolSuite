# WellRyde — capture “Save driver profile” for Supey

Supey can **pull** drivers from WellRyde today. To **push** address edits back (so every desk sees the same home when they **Pull from WellRyde**), we need one real save request from your browser.

## What to do (5 minutes)

1. Open **Chrome** → [WellRyde portal](https://portal.app.wellryde.com) → sign in.
2. **F12** → **Network** tab → check **Preserve log**.
3. Filter: **Fetch/XHR** (or type `users` in the filter box).
4. Go to **Administer Users** → open a **driver** you can edit (e.g. Dean Davis — empty home is fine for a test).
5. Click **Edit Profile** (or equivalent).
6. Change only **Address1 / City / State / ZIP** (small test change you can revert in WellRyde later).
7. Click **Save Changes** (or **Save**).
8. In Network, find the **POST** that fired on save (often URL contains `users`, `save`, `update`, or `mdm`).
9. Right‑click that row → **Copy** → **Copy as cURL (bash)**.
10. Paste the full cURL into a file and send it to Cursor:
    - Save as: `HiatmeToolSuite/docs/captures/wellryde-user-save.curl.sh`
    - Or paste directly in chat.

## Also helpful (optional)

- The **Request URL** and **Form Data** / **Payload** from that same POST (screenshot or copy).
- The driver’s **SEC-** id from the page URL, e.g. `.../portal/users/SEC-W-...`.

## What we will build after capture

- On **Save** in Supey driver editor: if the row has a `WellRydeSecId`, POST the same form WellRyde expects (address + hidden fields + CSRF).
- Other desks: **Pull from WellRyde** → refreshed home address → BUILD geocodes correctly.

## Do not capture

- Passwords or login POST — we already have Spring login in code.
- Trip billing (`saveBillData`) — different endpoint.

## Privacy

Redact cookies if you post publicly; for Cursor chat on your machine, the full cURL is fine so we can match `Cookie` / `_csrf` behavior.

What's new in 4.0.0.23:

Logins
- Fixed saved logins (Modivcare, WellRyde, Gmail) disappearing after an update. If a desk ended up running the Suite from a different folder, Windows started it with a blank settings store and the old logins were never found. The Suite now looks through every previous install location instead of just the current one
- Recovery also picks the genuinely newest saved logins. It used to sort versions as text, so "4.0.0.9" beat "4.0.0.21" and a desk could come back with months-old credentials
- If logins still come up empty after an update, the Suite now reports it to the AI server so we hear about it instead of finding out from you

Driver Habits
- "Billed skip" is gone. Billing a ticket late never proved the driver skipped the trip, so it is no longer flagged, counted, or held against anyone — including on past days
- Sched PU and Sched DO now show the real pickup and drop-off times on Unfinished rows. They used to both show the drop-off, which made an on-time pickup look wildly early
- "Billed too soon" still shows when billing closes a ticket before the driver had a chance to finish, and still never counts against the driver

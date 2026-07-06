What's new in 4.0.0.4:

Schedule Builder — stability
- Cancel stale map/OSRM work when you edit trips or switch tabs (fewer freezes and crashes)
- Debounced map refresh so rapid drags and cuts coalesce into one reload
- Load map for the active driver tab only after BUILD (other tabs load on first visit)
- Context menus open instantly; routing probe runs in the background
- Mileage HUD debounced and capped on large groups so arrow-key browsing stays responsive
- Map route pens disposed on redraw (less memory creep during long sessions)

Trip Scout — bell carousel
- Click the live bell to step through will-call and cancellation alerts one trip at a time
- Carousel highlights and scrolls to the trip in the list (Locate / prev / next)
- Matches full WellRyde bell trip IDs to shortened list IDs (e.g. 1-20260706-54178-B)
- Cancellations pop into the carousel automatically when Live is on

Includes all 4.0.0.3 features (updater carousel, Schedule Builder map tools, Suggest driver, Modivcare new trips, Trip Scout live HUD).

What's new in 4.0.0.57:

Schedule Builder no longer freezes on desks away from the server
- 4.0.0.56 moved the panel *probe* off the UI thread, but chat / presence /
  activity still started HttpClient from a WinForms timer. On .NET Framework that
  can block the window for the whole connect wait (~8–10s, every ~10s). Those
  calls now hop to a background thread first
- Panel HTTP also skips WinHTTP proxy autodetection (the probe already did;
  everything else did not). That search is what made remote desks hang while the
  server desk, with "no proxy", stayed smooth
- Local OSRM health now caches failures, not only successes, and never waits on
  the UI thread. Desks without Docker were re-probing 127.0.0.1:5000 on every
  map route until a 15s timeout

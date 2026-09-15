What's new in 4.0.0.58:

Schedule Builder map no longer freezes the window for 10 seconds at a time
- 4.0.0.57 moved chat/heartbeat off the UI thread. That was real, but it was not
  the freeze you still felt: GMap.NET downloads OpenStreetMap tiles with a 5s
  HttpWebRequest on the UI thread, then retries once. The server desk never
  waits because its tile cache is already warm
- UI-thread tile requests now fail in 80ms instead of hanging 10s. Missing tiles
  download in the background and the map redraws when they land
- Stall log now dumps the UI stack on freezes longer than 1s, so the next miss
  names the caller instead of "(outside any watched scope)"

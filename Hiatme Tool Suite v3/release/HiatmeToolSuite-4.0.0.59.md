What's new in 4.0.0.59:

Maps still draw — they just don't freeze the window to do it
- 4.0.0.58 aborted UI-thread tile downloads so the window wouldn't lock. That
  stopped the freeze but starved the map
- Paint now reads only the tile cache (no network on the UI thread). The tiles
  you are looking at download on a worker with a full 15s budget, land in the
  same cache the server desk already had warm, and the map redraws when they
  arrive
- Pan / zoom queues the new viewport the same way. Cached tiles show immediately

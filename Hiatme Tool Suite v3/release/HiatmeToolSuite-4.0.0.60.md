What's new in 4.0.0.60:

The ~10 second freeze was not the map
- On this desk the panel probe walked 127.0.0.1 (2s), office LAN 192.168.1.4 (6s),
  then another 192.168.1.x (2s) before the public panel that actually answers
- Schedule Builder map routing re-ran that walk for every road line, and the UI
  thread waited on the same lock, so the window locked in lockstep with the probe
- Probes no longer hold that lock. Dead LAN/loopback addresses time out in 400ms
  instead of 2–6s. A working public URL is reused for 45s. Each route line uses
  the already-resolved panel instead of probing again
- Maps still download tiles in the background and paint from cache

What's new in 4.0.0.56:

Fixes the freezing that only happened away from the server desk
- Found it: after the AI panel restarted or moved, a desk would re-check where the panel
  lives from the UI thread, one address at a time, with a 6 second timeout each. The window
  was locked for the whole walk. The server desk never saw it because the first address it
  tries is itself
- That check now happens in the background and the window never waits on it
- Every incoming "saved" toast was also reading a revision file off the OneDrive-synced
  Desktop while it drew, which can block for seconds on a syncing folder. That read is now
  cached and refreshed off-thread, so toasts no longer touch the disk
- Net effect: cutting, pasting and dragging trips stay smooth while chat and activity
  messages pop in

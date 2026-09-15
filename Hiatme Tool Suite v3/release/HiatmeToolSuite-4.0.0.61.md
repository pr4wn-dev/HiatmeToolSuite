What's new in 4.0.0.61:

Maps fill in while you pan, without freezing the window
- Paint still reads only the tile cache (no OSM wait on the UI thread)
- Missing tiles download two at a time (OpenStreetMap's limit), show up as they
  land instead of waiting for the whole viewport, and pan/zoom is debounced so a
  drag does not queue hundreds of overlapping downloads

# Hiatme Tool Suite 4.0.0.27

## Driver Habits

**Improved driver strip navigation so every driver tab is reachable.**
The left/right nav buttons are now larger and styled to match the driver
tabs, and paging logic better detects overflow so hidden drivers can be
reached reliably.

**Fixed cancel alert tab lighting to target the correct driver tab.**
Cancel flashes no longer light `All` for every cancellation. Cancel alerts now
light only the driver tab that owns the trip, and `Reserved` lights when the
cancelled trip is reserve-owned.

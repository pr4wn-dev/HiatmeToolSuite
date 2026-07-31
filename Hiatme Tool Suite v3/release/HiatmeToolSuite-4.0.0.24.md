# Hiatme Tool Suite 4.0.0.24

## Driver Habits

**New look for the scorecard and driver buttons.** The flat accent bar is gone.
Filter buttons and driver tiles now share a single silhouette with the top-left
and bottom-right corners sheared off and solder dots on the cuts. Idle and
active buttons keep the same border; only the lighting changes when one goes
live, so a blinking button no longer changes shape twice a second. Everything
is drawn from the active theme accent, so it recolors with your preset.

**Filter buttons now light up on an alert.** Previously a live Late PU alert
flashed the driver tile and the trip row while the Late PU button itself stayed
dark. Hot habits now flash their own button in step with everything else.
Cancels have no button of their own, so they flash All in amber.

**Fixed: alert blink never stopped running.** The 550 ms blink timer was only
stopped by the 60-second live poll, which is off in Day, Week, and Month mode.
After a single alert, leaving Live mode left the timer running for the rest of
the session, repainting the whole trip grid twice a second. It now stops itself
once every alert window has closed.

**Removed the accent strip that framed the scorecard.** The hero card lit its
whole left edge whenever a driver was selected.
What's new in 4.0.0.65:

The stray gray arrows are gone
- A small pair of gray left/right arrows could appear over any page, sometimes
  flashing during a resize and sometimes parking on top of a panel. Windows was
  adding its own tab scroller behind the scenes; the tab strip is now laid out so
  it is never created

Schedule Builder stays fast through a long day
- Section headers and gap notes were leaking a drawing handle every time the trip
  list repainted, so the whole window got slower the longer it stayed open. They
  are reused now instead of rebuilt

Freeze reports are accurate again
- Locking your desk or letting it sleep was being recorded as one enormous freeze,
  which buried the real ones. Sleep is logged separately and no longer counted
- Driver Habits Live refresh is now timed in detail so the slow step shows up by
  name in the next report

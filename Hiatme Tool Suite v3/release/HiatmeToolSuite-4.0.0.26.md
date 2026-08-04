# Hiatme Tool Suite 4.0.0.26

## Driver Habits

**Fixed: a later accommodated ticket time could still penalize the driver.**
Modivcare is normally the schedule authority, but it can retain an old
drop-off time after dispatch changes the live WellRyde ticket. The scorecard
only honored the newer ticket if it personally watched that change happen, so
a ticket first seen after the edit could still ding the driver against
Modivcare's old time.

Now, if Modivcare says a drop-off was late but the live WellRyde ticket has a
later scheduled drop-off and the driver met that ticket time, no late-dropoff
penalty is created. It also removes an existing false row made before the
ticket update was observed. WellRyde can only prevent a false penalty here; it
cannot create a new one.

This corrected Tina Mackie's August 3 trip: Modivcare still said 10:25 AM, the
ticket was moved to 10:52 AM, and the 10:33 AM drop-off had been incorrectly
counted as eight minutes late.

## AI panel connectivity and will-calls

Includes the 4.0.0.25 fixes for desks outside the office network, will-calls
showing "Will call" instead of an invented schedule, and the will-call bell
stopping once a driver has answered the trip.

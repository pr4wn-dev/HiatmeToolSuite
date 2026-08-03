# Hiatme Tool Suite 4.0.0.25

## Connecting to the AI panel from outside the network

**Fixed: "failed to connect to the AI panel" on a desk away from the office.**
Only the first address the tool tried got a full 6 seconds; every other one got
2, which is too tight for a connection coming in over the internet. A laptop
that had once run in the office also kept the office address as its last known
good one, so it spent the whole budget on an address that no longer answers and
then gave the real one two seconds. Addresses are now timed out on whether they
are local rather than on what order they happen to be in, and an office address
is tried last when you are not on that network.

The failure message used to say "failed to connect" no matter what went wrong,
including when the panel answered and turned you away over the API token. It
now tells you which it was. Note the token is not optional from outside the
network — the panel lets the office through without one, and nobody else.

## Driver Habits

**Fixed: the will-call bell rang all day for rides that were already done.**
WellRyde tells us about a will-call through the same message list its portal
bell reads, and a message stays in that list until somebody clicks it in the
portal — which has nothing to do with whether the ride happened. So an
activation kept ringing for hours after the driver had been and gone. One from
this morning was still chiming at 11:34 for a rider picked up at 8:22 and
dropped off at 8:29. A will-call now drops off the bell as soon as its trip has
a recorded pickup or leaves the board. A trip we cannot place still rings, since
missing a real one is the worse mistake.

**Fixed: will-calls showed a scheduled pickup they never had.** Modivcare marks
a will-call with a 00:00 pickup, meaning nobody has called it in yet. That was
being read as a missing time and quietly filled in from WellRyde, so a trip
could be marked 98 minutes early against a clock the driver was never given.
Once the invented time went away the trip stopped being re-scored, which left
the false flag stuck there for good. Will-calls now say "Will call" in the
Sched PU column instead of borrowing a time or showing a blank cell, including
in history days later.

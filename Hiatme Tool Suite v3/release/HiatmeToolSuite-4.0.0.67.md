# Hiatme Tool Suite 4.0.0.67

## A desk could get locked out of saving, and could not get itself out

If a desk lost track of which published version its copy was based on, the server refused
every save it made — correctly, because a save from an unknown starting point could quietly
overwrite newer work. The problem was that the desk had no way back. The marker recording
that version is only written after a save succeeds, so each retry reported the same unknown
starting point and was refused again. One desk hit this four times in three minutes.

The cause was where the marker was kept: in `Desktop\SCHEDULES FOR {year}\`, next to the
workbook. That folder is OneDrive-synced, so a per-desk fact was living in a shared place —
it arrived after the workbook it described, arrived from whichever desk wrote it last, or
never arrived at all while the workbook did. A desk that picked a schedule up through
OneDrive rather than through the Suite's own pull therefore never knew where it stood.

What changed:

- The marker now lives in this machine's own local app data, where the sync service cannot
  reach it. An existing marker is adopted once on first run, so no desk starts from scratch.
- When a desk's copy is byte-for-byte what is published, the Suite records that version
  instead of downloading identical content again. This is what keeps desks out of the bad
  state in the first place.
- A rejected save now backs the workbook up first. The fix for a rejection is to load the
  published copy over your own, and until now those edits existed nowhere else.
- The message is honest about what happened. At an unknown starting point it used to say
  another desk had published first, sending people to ask a colleague who had done nothing.
  It now says this desk lost track of its version.

Saves from a genuinely out-of-date copy are still refused, exactly as before. Nothing about
the protection against overwriting someone else's work has been loosened.

## The AI dock asks a question on its own

Questions about how much lateness a client can actually take used to arrive only as a
follow-on to an assistant reply, so a desk that never typed into the dock was never asked
anything. Opening the dock now pulls one, and answering chains to the next. Skipping means
"not right now" rather than never — a skipped client comes back around after a month.

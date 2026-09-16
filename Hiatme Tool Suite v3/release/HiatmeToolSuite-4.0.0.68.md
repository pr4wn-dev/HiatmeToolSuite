# Hiatme Tool Suite 4.0.0.68

Includes everything from 4.0.0.67, which was packaged but never uploaded. Install this instead.

## The panel can ask you things while you work

Questions used to appear only inside the AI dock. Nobody building a schedule has the dock open,
so the panel had asked nothing of anybody — hundreds of clients had enough history to be asked
about and not one had ever been answered.

Questions now arrive in the bottom-right corner with the rest of the notifications, and you
answer with one click without leaving the board.

- Only the words **Yes** and **Skip** respond. Clicking anywhere else on the card does nothing,
  so brushing it on the way to the trip list cannot answer on your behalf.
- One question at a time, never on top of another notification, and never while the window is in
  the background.
- Ignoring one is not an answer and nothing is recorded. It backs off for twenty-five minutes
  rather than asking again straight away.
- Answering means the next one comes sooner; you are clearly willing.

### What it asks about

**Standing pairings.** Eleven clients ride with one driver on 85–100% of their trips — one of
them 285 times out of 285. Confirming one means that driver is placed first whenever the trip
fits them.

**Corrections nothing explains.** When one dispatcher moves another's placement, the panel works
out what the move bought: shorter empty miles, the client's usual driver, a relieved connection,
a flatter load. Most moves are accounted for and are never raised. The ones that aren't get
asked about, at most three a day and only for a week or so afterwards.

**How late a client can run.** Per client, because a dialysis chair will not wait and a pharmacy
run does not care.

## From 4.0.0.67: a desk can no longer get locked out of saving

The marker recording which published version your copy is based on lived beside the workbook, in
a OneDrive-synced folder — so it was not your desk's state at all. It could sync in from another
desk, arrive after the file it describes, or never arrive.

A desk in that state could not recover: the marker is only written after a save succeeds, and the
server refuses every save made without one. One desk was refused four times in three minutes.

- The marker now lives on your machine, where it belongs. Existing ones are adopted on upgrade.
- A copy byte-identical to the published one is recognised as based on it.
- A rejected save keeps a backup of your edits first, since loading the published copy overwrites
  them and they exist nowhere else.
- The message on a rejection no longer blames a colleague when the cause is a lost marker.

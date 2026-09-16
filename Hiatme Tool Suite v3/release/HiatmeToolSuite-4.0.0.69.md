# Hiatme Tool Suite 4.0.0.69

Includes everything from 4.0.0.67 and 4.0.0.68. Install this instead of either.

## Questions now actually appear in the app

4.0.0.68 added playbook questions to the notification corner so you could answer them while
building a schedule, without the AI chat open. In practice they only ever showed up in the chat.

Two reasons, both fixed.

**Opening the chat silenced the corner.** The chat pulls a question when you open it, and it kept
hold of that question after you collapsed it again. The corner deliberately stays quiet while the
chat has one pending, so the same question is never put to you in two places at once — but since
the chat never let go, one visit to the chat silenced the corner until somebody went back and
answered it there. Which is the exact habit the corner exists to stop relying on. The chat now
releases the question when it closes.

**The first one took five minutes.** A desk opened in the morning got asked nothing for five
minutes, so anyone checking whether the feature worked concluded it did not. The first question
now comes about forty-five seconds after launch. The gap between later ones is unchanged.

Behaviour otherwise as described in 4.0.0.68: only the words **Yes** and **Skip** respond, never
on top of another notification, never while the window is in the background, and ignoring one
records nothing and backs the questions off for twenty-five minutes.

Note that questions still do not appear in the corner while the AI chat is open, which is
intended — the chat is asking you itself at that point.

## From 4.0.0.68: what it asks about

**Standing pairings.** Clients who ride with one driver on 85–100% of their trips. Confirming one
means that driver is placed first whenever the trip fits them.

**Corrections nothing explains.** When one dispatcher moves another's placement, the panel works
out what the move bought — shorter empty miles, the client's usual driver, a relieved connection,
a flatter load. Most are accounted for and never raised. The rest get asked about, at most three
a day.

**How late a client can run.** Per client, because a dialysis chair will not wait and a pharmacy
run does not care.

## From 4.0.0.67: a desk can no longer get locked out of saving

The marker recording which published version your copy is based on lived beside the workbook in a
OneDrive-synced folder, so it was not your desk's state at all. A desk that lost it could not
recover, because the marker is only written after a save succeeds and the server refuses every
save made without one.

- The marker now lives on your machine. Existing ones are adopted on upgrade.
- A copy byte-identical to the published one is recognised as based on it.
- A rejected save keeps a backup of your edits first.
- A rejection no longer blames a colleague when the cause is a lost marker.

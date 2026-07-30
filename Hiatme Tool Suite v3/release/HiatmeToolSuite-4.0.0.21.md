What's new in 4.0.0.21:

Reliability / AI
- The Tool Suite now reports to the AI server: it auto-reports crashes and UI-thread errors, plus startup/shutdown, so the AI can tell you when the tool was unhappy
- Outgoing Schedule Builder driver emails are recorded to the AI server (driver, date, recipient, subject) so the assistant can remember what was sent
- All reporting is fire-and-forget and fully silent if the server is unreachable — it never slows down or interrupts the app

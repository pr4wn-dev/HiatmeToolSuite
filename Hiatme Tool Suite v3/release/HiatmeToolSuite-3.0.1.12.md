What's new in 3.0.1.12:

- Schedule Builder: on load, verify Reserves → Reroutes trips against Modivcare (lookup only — no reroute submit)
- Schedule Builder: trips already rerouted on Modivcare show red before the list appears; __FSRR markers persist on save/load
- Schedule Builder: fix TripReroutes.aspx detection so already-rerouted trips are marked red instead of cleared
- Schedule Builder: reroute verify skips trips already marked red, reuses page tokens between lookups, and pauses 400 ms between probes

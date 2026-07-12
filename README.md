# DMU Mits

DMU Mits is a planned Dalamud plugin for mitigation planning during DMU.

The plugin shows a compact list of upcoming mitigation mechanics using a baked DMU mitigation timeline.

Current local build:

- Phase-local timers with a once-per-second sync loop while inside DMU.
- Cast/status/object-table sync points to keep the active phase timer aligned.
- A compact upcoming-mechanic bar list sorted soonest first.
- A top mitigation callout based on the local player's assigned slot.
- Party slot assignment settings for MT, OT, healer jobs, and D1-D4, with auto-sort plus manual reassignment.
- P4/P5 timings are derived from FFLogs observed mechanic hits where available; the final Forsaken Null enrage row remains a fallback because the reference kill ended before it resolved.

Puni setup will be added later.

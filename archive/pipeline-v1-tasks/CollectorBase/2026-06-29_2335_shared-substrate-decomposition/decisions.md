# Decisions

- Proceed via "reconstruct plan, then move" — user-chosen after context loss; design rebuilt + verified before any code move.
- Floor (`TransportErrorHandling`) moves first — it is the leaf and unblocks both peers.
- Pure structural carve only — no behavior change in this task.
- Build + full test suite green is the gate between every phase.
- Design doc is a required deliverable (portability for possible upstream replay), independent of A1's resolution.
- Verified dependency facts are treated as constraints, not re-litigated during execution.

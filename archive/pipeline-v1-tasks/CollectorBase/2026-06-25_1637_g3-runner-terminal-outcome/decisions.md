# Decisions

- Both fixes are one-liners with established shapes; execution applies them, does not redesign.
- A-M1 cap is the declared `MaxInProcessDelaySeconds` (the YAML's "short waits in-process" knob) → MaxServerSuggestedDelay. This is the architecturally-correct mapping of the "short honored / long externalized" invariant.
- A-M2 reuses the exact PartialSuccess(emitted,page,vendorName,flowName) helper the sibling terminal exits use — consistency, not a new path.
- Shared resilience policy order/values stay fixed (YAML supplies values only).
- A-M1 makes the externalization path live; it must be covered by a new test (was dead before).

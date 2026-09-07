# Assumptions

- A1 — Test the mismatch via the harness (ResumeAsync with a v-current but fingerprint-mismatched checkpoint →
  assert a from-start fetch in RequestedUrls, NOT the resumed stale cursor) rather than a pure unit assertion.
  STATUS: OPEN — LEAN harness (proves end-to-end the authoritative gate starts fresh). A direct CanUseState/Restore
  assertion may supplement.

- A2 — Keep `CanResume` coarse (no event-dependent strengthening). STATUS: OPEN — LEAN keep coarse + document;
  any StepIndex-sanity tightening is optional and must not require the event.

- A3 — The existing resume tests build checkpoints with the MATCHING fingerprint (happy path); the new tests must
  construct a v-current checkpoint whose fingerprint deliberately differs (changed input, e.g. `since`, or stream)
  so `CanUseState` rejects it. STATUS: OPEN — must hold for the test to exercise the real mismatch path.

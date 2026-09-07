# Task

Refresh the root `state.json` of
`ai/active/2026-05-24_1427_failure-handling-redesign/` to reflect the
sign-off of phases P0a, P0b, and P1 (which closed after the root was last
updated at 2026-05-24T15:35Z).

Surface — do not silently resolve — the scheduling inconsistency for the
`UnknownFlowRetryPolicy.IsUnknownRetryCandidate` marker-leak fix:

- `phase-0b/state.json` says: scheduled as Phase 4 prerequisite.
- `plan.md` rev 4 (per the status-report W4 reading): not scheduled — wait
  for P5 marker deletion.

Operator owns the reconciliation call.

No production code changes. No phase artifacts modified.

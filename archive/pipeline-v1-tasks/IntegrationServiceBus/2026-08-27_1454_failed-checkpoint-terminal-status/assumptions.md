# Assumptions

All OPEN. This skill holds no evidence.

## Prior Art

Tags: `integration-service-bus`, `checkpoint-resume`, `persisted-state`. The immediately preceding
attempt at this feature minted seven rows; the five that bear on this design are seeded below.

- OPEN — Retaining a failed run's checkpoint is a storage change with no lifecycle consequences.
  Refuted: the delete was also releasing the claim. source: lessons.md#L-d13589c6 (verifier, 2026-08-27)
- OPEN — A recovery sweep predicate and a TTL cleanup predicate over the same column should agree.
  Refuted: they answer different questions, and harmonising them resurrected week-old runs.
  source: lessons.md#L-716f3c62 (verifier, 2026-08-27)
- OPEN — Extending how long a row is retained has no security dimension. Refuted: `platform_event_json`
  can hold plaintext vendor credentials. source: lessons.md#L-44a4039e (verifier, 2026-08-27)
- OPEN — The retention branch in `CompleteExecutionAsync` is reached by every collector-reported
  failure. Refuted: shutdown cancellation and claim-loss return before it.
  source: lessons.md#L-5d94327f (verifier, 2026-08-27)
- OPEN — The migration and raw SQL added for retention are correct. Untested: Docker unavailable.
  source: lessons.md#L-910c8f52 (verifier, 2026-08-27)

## Task assumptions

- A1 OPEN — Writing `Status = Failed` in place of the delete reaches every collector-reported failed
  done and no other outcome.
- A2 OPEN — Nothing writes to a row after it is marked `Failed`, so `UpdatedAtUtc` is a stable
  failure timestamp for the retention window.
- A3 OPEN — Adding `Status != Failed` to `GetRecoverableAsync` fully prevents re-dispatch; there is
  no second dispatch path that would offer a terminal row.
- A4 OPEN — Narrowing `DeleteExpiredAsync` to terminal rows only leaks nothing, because non-terminal
  rows are ended by recovery or the unservable escalation.
- A5 OPEN — `Checkpoint:TtlHours` keeps deleting stop-request rows unchanged once the checkpoint
  predicate stops using it.
- A6 OPEN — `DeleteStoppedCheckpointsAsync` still deletes a terminal row for a stopped correlation.
- A7 OPEN — Releasing the claim in the same write prevents the redelivery dead-letter the previous
  attempt found.
- A8 OPEN — Stripping `Credentials` from `platform_event_json` damages nothing else in the payload,
  and nothing reads them back.
- A9 OPEN — `CheckpointStatus.Failed` can be written with no schema change; the column is plain text
  with no CHECK constraint.
- A10 OPEN — `ProcessEventCommandHandler.Handle`'s early guard, which today only considers
  `ScheduledWait`, behaves correctly when a redelivery meets a `Failed` row.
- A11 OPEN — Both stores can express the new predicates identically.

---

# Disposition (verifier-3, 2026-08-27) — terminal

Full citations in `review/verifier-3.md` §2. Three review cycles ran; cycles 1 and 2 each found a
defect introduced by the previous cycle's fix, cycle 3 found none.

| id | status | actor |
|----|--------|-------|
| A1 | **REJECTED** (first half) — shutdown cancellation and the CLAIM_LOST returns exit before the branch, by design. Second half VALIDATED: no non-failure outcome marks. | verifier |
| A2 | **REJECTED** — a terminal row IS written to again. `TryAcquireExecutionLockAsync` revives `Failed → Idle`, deliberately, so a redelivery is not stranded. The retention clock restarts if it does. | verifier |
| A3 | **REJECTED** — the sweep exclusion alone does not prevent re-dispatch; the select-then-claim window meant a row marked terminal mid-sweep was still claimable. The `status <> 'Failed'` guard on `TryClaimAsync` is the load-bearing half. | verifier |
| A4 | **REJECTED** — it leaked. Repurposing the one cleanup predicate made non-terminal rows immortal; two methods on two horizons was the fix. | verifier |
| A5 | VALIDATED — stop-request deletion byte-identical to baseline | verifier |
| A6 | VALIDATED by inspection; its only test is Docker-gated | verifier |
| A7 | VALIDATED | verifier |
| A8 | VALIDATED with caveat — a redelivery restores the full payload, so the guarantee holds only while the row stays terminal | verifier |
| A9 | VALIDATED (schema half); the write itself is runtime-unverified | verifier |
| A10 | VALIDATED behaviourally; the *semantics* remain the operator's open question | verifier |
| A11 | VALIDATED for the predicates; **NOT** for the strip's edge cases until the added tests are counted | verifier |
| L-d13589c6 | VALIDATED as refuted — the lesson held and got stronger | verifier |
| L-716f3c62 | VALIDATED as refuted | verifier |
| L-44a4039e | VALIDATED as refuted | verifier |
| L-5d94327f | VALIDATED as refuted | verifier |
| L-910c8f52 | NEVER-TESTED — Docker still unavailable | verifier |

## Must not be re-assumed without new evidence

- That a terminal row is inert (A2) — it is revivable by design.
- That excluding a status from a sweep's SELECT prevents dispatch (A3) — select-then-claim needs the
  guard on the claim.
- That one cleanup predicate can serve two horizons (A4).
- That the secret strip is unconditional (A8) — a redelivery restores the payload.
- That the Postgres raw SQL works (L-910c8f52, A9) — it has never executed.

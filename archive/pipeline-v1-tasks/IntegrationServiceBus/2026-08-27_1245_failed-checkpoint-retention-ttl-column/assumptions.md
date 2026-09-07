# Assumptions

All entries OPEN. This skill holds no evidence.

## Prior Art

Tags: `integration-service-bus`, `checkpoint-resume`, `cross-repo-contract`. Matched 11 rows;
`L-57acd253` head-filtered (superseded by `L-9c94d22d`). Triaged the newest 10; 3 are relevant here.

- OPEN — `UnservableTerminalAge` is a plain `CheckpointTtl * 0.75`, so the sweep's give-up bound
  scales cleanly with the TTL key. Refuted last task: it is
  `max(CheckpointTtl*0.75, StaleClaimThreshold*6)`. Bears directly on the "do not raise TtlHours"
  constraint. source: lessons.md#L-16f8c597 (verifier, 2026-08-27)
- OPEN — ISB's flat checkpoint columns are inert bookkeeping the collector ignores. Refuted last
  task: three collectors read them inside `CanResumeFrom`. Bears on whether adding a column can
  perturb collector behaviour. source: lessons.md#L-6f1353b8 (verifier, 2026-08-27)
- OPEN — state lives cleanly in ISB and readability cleanly in the collector. Drifted last task.
  Bears on whether a retention stamp is purely ISB state. source: lessons.md#L-f80a1c15
  (verifier, 2026-08-27)

## Task assumptions

- A1 OPEN — The failure arm of `CompleteExecutionAsync` is reached by every collector-reported
  failure and by no other outcome, so stamping there is both complete and non-over-reaching.
- A2 OPEN — `FlushAndCleanupCheckpointAsync` can be given the run outcome without disturbing its
  refused-write early return, which must keep precedence.
- A3 OPEN — Adding a nullable column to `adapter_checkpoints` requires no backfill and breaks no
  existing query, index, or upsert.
- A4 OPEN — `GetRecoverableAsync` gaining `RetainUntilUtc == null` fully prevents re-dispatch of a
  retained row, with no second path into recovery that bypasses that query.
- A5 OPEN — `DeleteExpiredAsync` can carry two cutoffs in one predicate without changing behaviour
  for rows where `retain_until_utc` is null.
- A6 OPEN — `DeleteStoppedCheckpointsAsync` should continue deleting retained rows for stopped
  correlations, i.e. an explicit human stop outranks retention.
- A7 OPEN — Guarding the unhandled-exception delete against a retention stamp does not strand rows,
  because the cleanup job's retention cutoff still reaps them.
- A8 OPEN — The upsert path will not clear `retain_until_utc` on a subsequent write from a late or
  duplicate execution.
- A9 OPEN — `InMemoryCheckpointRepository` mirrors every changed predicate, so tests that pass
  against it reflect Postgres behaviour.
- A10 OPEN — The cleanup job can read a second config key through its existing job-data-map wiring
  without a new registration shape.
- A11 OPEN — No test currently asserts that a failed run deletes its checkpoint; if one does, it
  encodes the old behaviour and must be updated rather than worked around.

---

# Disposition (verifier-2, 2026-08-27) — terminal

Full citations in `review/verifier-2.md` §2. **A4 was re-disposed by the orchestrator after
verifier-2 ran**, because the predicate it judged was reverted afterwards; that row is marked.

| id | status | actor |
|----|--------|-------|
| A1 | **REJECTED (first half)** — a collector-reported failure during pod shutdown returns at `ProcessEventCommandHandler.cs:279` before `CompleteExecutionAsync` and stamps nothing; same for the CLAIM_LOST returns. Second half VALIDATED (no non-failure outcome stamps). | verifier |
| A2 | VALIDATED — refused-write guard still first; retention branch below it | verifier |
| A3 | NEVER-TESTED — `MigrateAsync()` never ran (Docker). Its "no upsert" clause is now deliberately false: the lock's `DO UPDATE SET` names the column. | verifier |
| A4 | **VALIDATED after revert** — `GetRecoverableAsync` is `RetainUntilUtc == null` (`CheckpointRepository.cs:677`), so a held row is never swept for its whole life. Verifier-2 had REJECTED this against the interim `\|\| <= nowUtc` form, which was reverted on its own recommendation. Caveat stands: `GetScheduledWaitsDueByAsync` carries no retention filter. | orchestrator |
| A5 | VALIDATED (InMemory only) — Postgres twin is the same expression tree, never executed | verifier |
| A6 | VALIDATED (InMemory only) — stop still outranks a hold | verifier |
| A7 | VALIDATED — a held row is unclaimed and unswept, so `updated_at_utc` stays frozen and it dies at its deadline | verifier |
| A8 | VALIDATED — `R1_UpsertDoesNotClearRetention` green; the lock is the only upsert naming the column | verifier |
| A9 | **REJECTED** — in-memory `UpsertAsync` can set a hold Postgres cannot; in-memory `TryClaimAsync` returns true unconditionally (pre-existing), making one new test vacuous there | verifier |
| A10 | NEVER-TESTED — superseded before execution; no second config key exists | verifier |
| A11 | VALIDATED — all five pre-existing `DeleteAsync` assertions are `Times.Never`; nothing encoded delete-on-failure | verifier |
| L-16f8c597 | VALIDATED — `Checkpoint:TtlHours` was not raised; a separate key was added, as the lesson advised | verifier |
| L-6f1353b8 | VALIDATED — flat columns are not inert; this change reads and writes them deliberately | verifier |
| L-f80a1c15 | NEVER-TESTED — the state/readability split was not exercised; no collector code was touched | verifier |

## Must not be re-assumed without new evidence

- That the retention branch is the sole terminus of every failing run (A1) — shutdown cancellation
  and claim-loss both bypass it.
- That a green in-memory retention test implies Postgres behaviour (A9).
- That a stamped row is undispatchable by every path (A4) — `GetScheduledWaitsDueByAsync` is unfiltered.
- That the migration is sound (A3) — it has never executed.

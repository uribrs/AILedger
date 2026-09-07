# Phase 3 — Persist retry budget on checkpoint

Source: `plan.md` rev 4 §6 Phase 3 (lines 623-662).

Introduce `RetryBudget` that reads three reserved keys
(`_retry.attemptCount`, `_retry.lastErrorCode`, `_retry.lastAtUtc`) from
`AdapterCheckpoint.AdapterState` on resume, writes them via
`progressContext.SetState(...)` from inside the runner on each retry
transition, and clears them on successful run.

`DefaultFailurePolicy` consults the budget for a cross-redelivery cap
(max attempt count across redeliveries).

Persistence durability resolved per Prereq 1 option (2): the runner
calls `progressContext.AdvancePage(0, 0)` after writing the
`_retry.*` keys to trigger the platform's `OnCheckpoint` callback —
matching every existing collector's checkpoint flush pattern.

Out of scope: enabling classified-retry on any collector (P4), Falcon
behaviour changes (P4), Shape A convergence (P5).

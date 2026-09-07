# Execution Notes — Group 2: resume / fingerprint integrity

## Outcome
`dotnet build` clean; `dotnet test CollectorBase.slnx` → **79/79** (77 prior + 2 new). No behavior change; doc
corrected; the two untested start-fresh invariants are now locked.

## What changed
- `CollectorExecutor/docs/multi-step-flows.md:227` — corrected the false claim that `CanResumeFrom` checks the
  fingerprint. Now: `CanResumeFrom` is a COARSE pre-check (SDK gives it only the checkpoint, not the event → can't
  compute the fingerprint; checks v + non-empty stream only); the AUTHORITATIVE gate is `Restore`/`CanUseState`
  (has the event; checks v + fingerprint + step-shape + step-index; mismatch → fresh, never wrong data).
- `CollectorExecutor/Execution/CollectorExecutorCheckpointManager.cs` — one clarifying comment above `CanResume`
  (no behavior change).
- `Tests/CollectorExecutor.Test/CollectorExecutorTests.cs`:
  - `ResumeAsync_FingerprintMismatch_StartsFresh_NotStaleCursor` — v-current checkpoint at cursor=2 with a
    fingerprint written for different inputs (start_time). `CanResumeFrom` true (coarse), but Restore rejects the
    mismatch → FRESH: 3 records (a cursor=2 resume would give 1) + a from-start (non-`after=2`) page fetched.
  - `ResumeAsync_SchemaVersionBump_StartsFresh` — checkpoint fingerprint computed at schemaVersion 2 vs the
    profile's schema_version 1 → mismatch → fresh (3 records).

## Why no wrong-data (the structural point)
`CanResumeFrom(AdapterCheckpoint)` has no dispatch event, so it cannot compute the fingerprint — it is necessarily
coarse. The authoritative gate `Restore`/`CanUseState` has the event and rejects any fingerprint/step-shape/
schemaVersion mismatch by starting fresh. A coarse "true" from the pre-check followed by a Restore-fresh is
correct (fresh re-fetch under changed inputs), never a stale-cursor resume.

## Assumption resolutions
A1 harness test (end-to-end ResumeAsync proof) · A2 CanResume kept coarse (comment only, no event-dependent logic)
· A3 checkpoints constructed with deliberately mismatched fingerprints. See assumptions.md.

## No-regression
Full suite green (79/79), incl. the matching-fingerprint resume happy-path tests.

## Commands
- `dotnet build CollectorBase.slnx` → clean. `dotnet test CollectorBase.slnx` → 79/79.

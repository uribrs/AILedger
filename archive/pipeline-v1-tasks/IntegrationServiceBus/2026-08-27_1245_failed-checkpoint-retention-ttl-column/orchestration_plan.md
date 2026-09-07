# Orchestration Plan

## Problem Classification

- Contract sufficiency: sufficient
- Classes:
  - `checkpoint-resume` — existing ledger tag; the change alters what survives a failed run.
  - `integration-service-bus` — existing ledger tag; single-repo ISB change.
  - `persisted-state` — new slug; a schema column plus two predicate changes over it.
- Additional classified prior art: none new beyond the designer's block. `L-16f8c597`
  (UnservableTerminalAge is not a plain 0.75 multiple) is the live one — see R4.
- Recon correction: confirmed. Recon narrowed the config design (below) but did not change the classes.
- New or changed artifacts:
  - `CheckpointEntry.RetainUntilUtc` → `CheckpointDbContext` mapping → a new `jsonb`-adjacent
    nullable timestamptz column; **not** added to either raw upsert column list, which is what makes
    a later checkpoint write preserve the hold (`CheckpointRepository.cs:59-65`, `:81-96`).
  - `ICheckpointRepository.SetRetentionAsync` → two production implementations **and two hand-written
    test decorators** (`StoppingIsNotEndingTests.cs:71-93`,
    `CheckpointRecoveryCompatibilityTests.cs:606-625`) → absent forwarding members are a compile
    error that reads as unrelated breakage.
  - `Checkpoint:FailedRetentionDays` → read in `ProcessEventCommandHandler` via already-injected
    `IConfiguration` (`:34`) → stamped as an absolute deadline; the cleanup job compares, so it needs
    no second key and `DependencyInjection.cs` is untouched.
  - Retention hold outliving `Checkpoint:TtlHours` → `CheckpointRecoveryHandler.cs:692-698` and
    `CheckpointCleanupJob.cs:21-25` doc comments assert the two horizons are one number → those
    comments become false for retained rows. **Design-invalidating for the docs**, hence R4.

### Attention Items

| id | failure mode | causal path and impact | planned handling | source |
|---|---|---|---|---|
| R1 | A later checkpoint write clears the retention hold | `UpsertAsync`'s `DO UPDATE SET` names every column; adding `retain_until_utc` there would silently reset the deadline on the next page advance, and the row would be deleted at the 24h TTL instead of the 7-day hold | `test:UnitTests/...Infrastructure.Core.UnitTests/CheckpointRetentionTests.cs::R1_UpsertDoesNotClearRetention` | `CheckpointRepository.cs:81-96` |
| R2 | Retention stamped on a run that was cancelled or skipped | `CancelledResult` and `PartialResult` set `Success = false`; `SkippedResult` sets `true`. A `!result.Success` predicate retains cancelled runs (contradicting the stop path, which deletes by correlation id) and a `Success`-based one mis-handles skip | `test:UnitTests/...Application.UnitTests/ProcessEventCommandHandlerTests.cs::R2_OnlyFailureStatusesStampRetention` | recon §13, landmine 2/3 |
| R3 | Retention stamped on a row this execution no longer owns | `FlushAndCleanupCheckpointAsync:1219-1224` early-returns on a refused write precisely so a claim-lost execution touches nothing; a retention branch placed above it would write to another owner's row | `test:UnitTests/...Application.UnitTests/ProcessEventCommandHandlerTests.cs::R3_RefusedWriteTakesPrecedenceOverRetention` | `ProcessEventCommandHandler.cs:1219-1224` |
| R4 | Two doc comments become false for retained rows | Both assert the cleanup horizon and the sweep's give-up bound are the same number; a 7-day hold outlives the 24h TTL while invisible to the sweep. `docs/coding-standards.md:131-135` forbids knowingly-stale comments | `guard:CheckpointRecoveryHandler.cs:692` + `guard:CheckpointCleanupJob.cs:21` — both updated in this change | landmine 9 |
| R5 | Postgres predicate changes ship unverified on a Docker-less machine | `CheckpointRepositoryTests` is Testcontainers-gated and `CheckpointCleanupJob` has zero coverage, so `DeleteExpiredAsync`'s new predicate may be exercised only through InMemory | `accept: report the Docker gate explicitly in execution_notes.md and the final response rather than reporting green` | landmine 11/12 |

### Research Questions

No research needed — every question is answerable from source, and the archived recon already
resolved the cross-repo ones. Nothing here depends on external system behaviour.

## Complexity Decision
- Path: decompose
- Axis scores: Complexity medium | Separability medium | Coupling low | Dependency order strict | Execution risk medium | Worker clarity high
- Rationale: hard trigger — recon named disjoint file sets, and tests are separable from
  implementation. Coupling is read off recon, not estimated. Dependency order is strict: the test and
  application sets do not compile until the Domain property and interface member exist.

## Research Decisions
- External research: none needed.
- Internal recon: complete → `research/internal-recon.md` (narrow confirmation pass; the broad map is
  the audited archive at `ai/done/2026-08-27_1153_failed-checkpoint-retention-and-retry/`).

## File Ownership
- Disjoint sets found: 3
- W1 owns (schema + persistence): `Domain/.../Models/CheckpointEntry.cs`,
  `Domain/.../Interfaces/ICheckpointRepository.cs`, `Domain/.../Constants/ConfigurationKeys.cs`,
  `Infrastructure.Postgres/Persistence/CheckpointDbContext.cs`,
  `Infrastructure.Postgres/Persistence/CheckpointRepository.cs`,
  `Infrastructure.Postgres/Migrations/*` (new migration + Designer + snapshot),
  `Infrastructure.Core/Services/InMemoryCheckpointRepository.cs`
- W2 owns (lifecycle + doc truth): `Application/Commands/ProcessEventCommandHandler.cs`,
  `Application/Services/CheckpointRecoveryHandler.cs` (R4 comment only),
  `Infrastructure.Core/Jobs/CheckpointCleanupJob.cs` (R4 comment only)
- W3 owns (all tests): `Tests/...API.UnitTests/Checkpoints/*`,
  `UnitTests/...Application.UnitTests/ProcessEventCommandHandlerTests.cs`,
  `UnitTests/...Infrastructure.Core.UnitTests/*`
- Shared surface frozen in phase 0: `research/internal-recon.md` §"Shared surface to freeze" —
  the property, column name/type, config key/default, `SetRetentionAsync` signature, and the two
  frozen predicates. Written before any worker starts; no worker may renegotiate it.

## Worker Plan
- W1 — scope: schema + persistence  owns: set above  inputs: frozen surface  phase: 1  continuity: fresh
- W2 — scope: stamp retention on the failure arm, guard the `:880` delete, fix the two R4 comments  owns: set above  inputs: W1's interface member  phase: 2  continuity: fresh
- W3 — scope: all tests incl. the two decorator forwarding members  owns: set above  inputs: frozen surface only, never W2's implementation  phase: 2  continuity: fresh

W2 and W3 run concurrently after W1. W3 codes against the frozen contract, not against W2's code —
that isolation is the reason tests are a separate set.

## Synthesis Approach
Main thread builds the solution after W2 and W3 return, reconciles any `BLOCKED:` returns, and runs
the affected test projects. Pre-existing failures (one in `Application.UnitTests`) are confirmed by
stashing before any failure is attributed to this change.

## Verification Obligations
- Cross-check against `prompt_contract.md` Success Criteria, noting that SC "wired into the cleanup
  job" is superseded by the recon-driven design (drift, recorded in `decisions.md`).
- Dispose A1–A11 plus the three prior-art entries.
- Confirm `retain_until_utc` appears in neither upsert column list (R1).
- Confirm the stamping predicate names `AdapterResultStatus` members, not `Success` (R2).
- Confirm the refused-write early return still precedes the retention branch (R3).
- Report the Docker gate honestly (R5).

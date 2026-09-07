# Orchestration Plan

## Problem Classification

- Contract sufficiency: sufficient
- Classes: `checkpoint-resume`, `integration-service-bus`, `persisted-state` (all existing ledger tags)
- Additional classified prior art: none new — the designer seeded the five rows the immediately
  preceding attempt minted, and they are the classified delta.
- Recon correction: not re-run. The audited maps at
  `ai/done/2026-08-27_1153_.../recon_report.md` and `ai/done/2026-08-27_1245_.../research/internal-recon.md`
  cover this exact subsystem; this change is a strict subset of ground already mapped.
- New or changed artifacts:
  - `ICheckpointRepository.MarkFailedAsync` → two production implementations **and two hand-written
    test decorators** (`StoppingIsNotEndingTests.cs`, `CheckpointRecoveryCompatibilityTests.cs`) →
    missing forwarding members are a compile error that reads as unrelated breakage.
  - `CheckpointStatus.Failed` first production writer → `GetRecoverableAsync` and `DeleteExpiredAsync`
    → both must change in the same round, or a terminal row is swept in 5 minutes.
  - Retention config value → read where the cleanup cutoff is computed.

### Attention Items

| id | failure mode | causal path and impact | planned handling | source |
|---|---|---|---|---|
| R1 | The sweep re-dispatches a terminal row | `GetRecoverableAsync` excludes only `ScheduledWait`; a `Failed` row is unclaimed after the release, so the 5-minute sweep claims and re-dispatches it — resurrecting a run that already published its done. Both reviewers of the rejected attempt found this independently. | `test:R1_SweepNeverOffersAFailedRow` | prior review, `CheckpointRecoveryJob.cs:18` vs `CheckpointCleanupJob.cs:19` |
| R2 | Retention applied to the wrong outcomes | `CancelledResult`/`PartialResult` set `Success=false`, `SkippedResult` sets `true`; a `!result.Success` predicate retains cancelled runs and mishandles skip | `test:R2_OnlyReportedFailureMarksTerminal` | prior recon landmines 2/3 |
| R3 | A claim-lost execution marks another owner's row terminal | The refused-write early return in `FlushAndCleanupCheckpointAsync` exists so such an execution touches nothing; a branch above it would write anyway | `test:R3_RefusedWriteTakesPrecedence` | prior recon landmine 4 |
| R4 | Redelivery dead-letters | The replaced delete was also releasing the claim; leaving the row claimed means `TryAcquireExecutionLockAsync` denies the redelivery, and `EXECUTION_LOCKED` is not in `NonTransientErrorCodes` | `test:R4_MarkingTerminalReleasesTheClaim` | prior review B1 |
| R5 | The credential strip silently no-ops | It targets a JSON key by name; a wrong key leaves credentials in place with the write still succeeding. Failure mode is silence. | `test:R5_StrippedPayloadKeepsEverythingExceptCredentials` + key derived from `nameof` | prior review I3 |

### Research Questions

No research needed — no external-system behaviour is in play, and the internal ground is mapped.

## Complexity Decision
- Path: decompose
- Axis scores: Complexity low | Separability high | Coupling low | Dependency order weak | Execution risk medium | Worker clarity high
- Rationale: hard trigger — tests and implementation are both in scope and separable, and the tests
  exist to pin behaviour the previous attempt got wrong twice. A worker owning both would write
  tests against whatever its code does; that is the failure this split prevents.

## Research Decisions
- External research: none.
- Internal recon: reused, not re-run → the two audited artifacts named above.

## File Ownership
- Disjoint sets found: 2
- W1 owns: `Domain/.../Interfaces/ICheckpointRepository.cs`, `Domain/.../Constants/ConfigurationKeys.cs`,
  `Infrastructure.Postgres/Persistence/CheckpointRepository.cs`,
  `Infrastructure.Core/Services/InMemoryCheckpointRepository.cs`,
  `Application/Commands/ProcessEventCommandHandler.cs`
- W2 owns: `Tests/...API.UnitTests/Checkpoints/*`,
  `UnitTests/...Application.UnitTests/ProcessEventCommandHandlerTests.cs`,
  `UnitTests/...Infrastructure.Core.UnitTests/*`
- Shared surface frozen in phase 0 (below); no phase-0 worker needed, it is one signature and two predicates.

### Frozen surface

```csharp
Task<bool> MarkFailedAsync(
    string tenantId, string correlationId, PlatformType platformType,
    AdapterCategory category, string instanceId, CancellationToken cancellationToken = default);
```
Owner-guarded on `instanceId`. One statement: `status = Failed`, `claimed_by_instance = NULL`,
`claimed_at_utc = NULL`, credentials stripped from `platform_event_json`, `updated_at_utc = now`.
Returns true when a row matched.

- `GetRecoverableAsync` adds `&& c.Status != CheckpointStatus.Failed`
- `DeleteExpiredAsync` becomes `c.Status == CheckpointStatus.Failed && c.UpdatedAtUtc < cutoffUtc`,
  where `cutoffUtc` is now minus the retention window. Stop-request deletion is untouched.
- Credentials key: `nameof(PlatformEvent.Credentials)`, exposed as a const the tests can reference.

## Worker Plan
- W1 — scope: the three changes plus the handler branch  owns: set above  phase: 1  continuity: fresh
- W2 — scope: all tests plus the two decorator forwarding members  owns: set above  phase: 1  continuity: fresh

Both run concurrently; W2 codes against the frozen surface, never against W1's implementation.

## Synthesis Approach
Main thread builds, runs the affected suites, reconciles any `BLOCKED:` return, then verifier and a
blind code-reviewer.

## Verification Obligations
- Success Criteria in `prompt_contract.md`; dispose A1–A11 and the five prior-art entries
- Confirm no new migration and no new column exist
- Confirm every non-failure outcome still deletes
- Confirm stop-request deletion is unchanged
- Report the Docker gate honestly

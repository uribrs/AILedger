# Orchestration Plan

## Problem Classification

- Contract sufficiency: sufficient
- Classes: `checkpoint-resume`, `integration-service-bus`
- Additional classified prior art: none new — the designer seeded the four rows the two prior passes minted.
- Recon correction: **the design changed before execution.** The marker was specified to ride
  `PlatformEvent.Metadata`; a narrow check showed `Metadata` is serialized into `platform_event_json`
  (`ProcessEventCommandHandler.cs:134`) and would be replayed by the sweep. It now rides
  `ProcessEventCommand` instead.
- New or changed artifacts:
  - `AdapterRunMessage.<marker>` → `IsbPlatformEventDispatcher` → sets the command flag. ISB-owned
    Domain, additive; absent = false.
  - `ProcessEventCommand.ForceResume` → `ProcessEventCommandHandler` → passed to
    `ExecuteWithResumeAsync`, which skips the gate. Init-only, never serialized — same shape as
    `CapacityPreAcquired` (`ProcessEventCommand.cs:19`).
  - Bypassed gate → `IResumableAdapter.ResumeAsync` → runs against state `CanResumeFrom` would have
    declined. Intended; the failure must be loud.

### Attention Items

| id | failure mode | causal path and impact | planned handling | source |
|---|---|---|---|---|
| R1 | The marker reaches an automatic sweep dispatch and overrides the 23h bound fleet-wide | A marker on any field of `PlatformEvent` is serialized at `ProcessEventCommandHandler.cs:134` into `platform_event_json`, which `CheckpointRecoveryHandler` rehydrates on every later sweep of that row | `test:R1_SweepDispatchNeverForcesResume` — plus the structural guarantee that the flag is not on the serialized type | `ProcessEventCommandHandler.cs:134`; `CheckpointRecoveryHandler.cs:390` |
| R2 | A forced resume that fails falls through to a silent restart | `ExecuteWithResumeAsync` currently falls through to `ProcessAsync` on decline; if the forced path inherits that, a failed override recollects two days of data silently | `test:R2_ForcedResumeFailureDoesNotRestart` | `ProcessEventCommandHandler.cs` resume path |
| R3 | An unmarked dispatch changes behaviour | Every existing caller builds `ProcessEventCommand` without the flag; a default or inversion mistake would alter the automatic path | `test:R3_UnmarkedDispatchIsUnchanged` | 16 construction sites, `grep "new ProcessEventCommand"` |
| R4 | The override is invisible after the fact | A resume that skipped the collector's own judgement, with nothing in the logs, is unexplainable when someone asks why a 2-day-old checkpoint was used | `test:R4_ForcedResumeIsLogged` | contract Success Criteria |

### Research Questions

No research needed — no external-system behaviour; the ground is mapped by two audited recon passes.

## Complexity Decision
- Path: decompose
- Axis scores: Complexity low | Separability high | Coupling low | Dependency order weak | Execution risk medium | Worker clarity high
- Rationale: hard trigger — tests and implementation are both in scope and separable. Execution risk
  is medium rather than low because every prior pass on this path found a blocker, two of them
  introduced by the previous pass's own fix.

## Research Decisions
- External research: none.
- Internal recon: reused from the two archived passes; a narrow confirmation of the three edit sites
  was done in the main thread and produced the design correction above.

## File Ownership
- Disjoint sets found: 2
- W1 owns: `Domain/.../Messaging/AdapterRunMessage.cs`,
  `Application/Commands/ProcessEventCommand.cs`,
  `Application/Messaging/IsbPlatformEventDispatcher.cs`,
  `Application/Commands/ProcessEventCommandHandler.cs`
- W2 owns: all test projects
- Shared surface frozen below.

### Frozen surface

- `AdapterRunMessage` gains an optional bool marker. Absent = false.
- `ProcessEventCommand` gains `public bool ForceResume { get; init; }`, beside `CapacityPreAcquired`.
- `ExecuteWithResumeAsync` takes the flag; when set **and** a checkpoint row exists, call
  `resumable.ResumeAsync(...)` without calling `CanResumeFrom`. When set and **no** row exists,
  behave as today (there is nothing to resume).
- A forced resume that fails returns that failure. It must not fall through to `ProcessAsync`.
- `PlatformEvent` is not touched, in any field.

## Worker Plan
- W1 — scope: the four files above  phase: 1  continuity: fresh
- W2 — scope: all tests, including R1–R4  phase: 1  continuity: fresh

Concurrent; W2 codes against the frozen surface, never against W1's implementation.

## Synthesis Approach
Main thread builds, runs the suites, then verifier and a blind code-reviewer with minimal context.

## Verification Obligations
- Success Criteria; dispose A1–A8 and the four prior-art rows
- **Demonstrate** the marker cannot reach a sweep dispatch — structurally, not by assertion
- Confirm all 16 existing `new ProcessEventCommand` sites are unaffected
- Confirm a failed forced resume does not restart
- Report the Docker gate honestly

# Orchestration Plan

## Complexity Decision

- Path: **decompose** (2 workers + a phase-0 freeze held in the main thread)
- Axis scores: Complexity **medium** | Separability **medium** | Coupling **low** | Dependency order **simple** | Execution risk **high** | Worker clarity **high**
- Rationale: the implementation is one control-flow method plus a small config mirror, which on its own
  argues for `direct` — and recon recommended exactly that. The override is the third hard trigger: tests and
  implementation are both in scope and separable. On a concurrency change that isolation is the point. A single
  agent writing both will assert whatever ordering its implementation happened to produce; a test worker barred
  from `src` asserts the ordering guarantee in the contract. Execution risk is scored high because the failure
  mode here (a race that passes CI and corrupts published-object order in production) is not one the build
  catches.

## Research Decisions

- External topic: `crowdstrike-spotlight-throughput` — triggered by assumptions A3, A4 — status: **complete**
  - Redirected mid-flight to the authoritative docs checked into the repo at
    `src/.../FalconCollector/FalconDocs/OfficialDocs/` (`spotlight.pdf`, `crowdstrike-auth.pdf`,
    `discover.pdf`) rather than the open web.
  - **Blocks the concurrency default only.** All plumbing, ordering, and cancellation work is independent of
    it. Workers do not wait; the default literal is injected at phase 0 close.
- Internal recon: complete → `research/internal-recon.md`

## File Ownership

- Disjoint sets found: **2** (implementation vs tests)
- **W1 owns** (implementation, may not write tests):
  - `src/.../Collectors/FalconCollector/Flows/Findings/FalconFindingsFlow.cs`
  - `src/.../Collectors/FalconCollector/Processing/Configuration/FalconCollectorConfiguration.cs`
  - `src/.../Collectors/FalconCollector/Processing/Configuration/FalconCollectorConfigurationBuilder.cs`
  - any new file under `Flows/Findings/Correlated/` that W1 introduces for the buffer/pump
  - **granted mid-flight 2026-08-18** (W1 reported the gap rather than reaching across the line):
    `Dtos/FindingsDtos/FindingsFlowRunConfig.cs` and `Processing/FalconFlowRunPreparer.cs` — the flow never
    receives a `FalconCollectorConfiguration` (`FalconFindingsFlow.cs:106-110`, ctor `:85-92`), so every knob
    reaches it through the `FindingsFlowRunConfig` DTO built at `FalconFlowRunPreparer.cs:62-75`. Without these
    two ~1-line additions the degree cannot reach the pump. No other worker owns them.
- **W2 owns** (tests and test infrastructure, may not write `src/`):
  - `src/.../UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.FalconCollector.Test/**`
  - including the `FakeHttpClientFactory.RequestedUrls` race fix
- Shared surface frozen in phase 0: the config property (name, type, default, clamp bounds), the
  behavioural contract W2 tests against, and the cancellation semantics. See below.

## Phase 0 — Frozen Shared Surface

Held in the main thread. No worker may change these; a worker that needs a change returns `BLOCKED:`.

1. **Config property** — `MaxConcurrentSpotlightBatches`, `int`, on `FalconCollectorConfiguration`,
   clamped in `FalconCollectorConfigurationBuilder.ExtractFields` mirroring the `aidBatchSize` idiom at
   `FalconCollectorConfigurationBuilder.cs:172-175` and folded via `cfg with { ... }` at `:217-220`.
   Clamp range **`[1, 16]`** (corrected 2026-08-18 from [1,32] by the A3 research). **Default literal: 4** —
   final, from research; basis recorded in `decisions.md`.
   **The knob exists on two types, with one clamp between them:**
   - `FalconCollectorConfiguration.MaxConcurrentSpotlightBatches` — user-facing; the ONLY place `Math.Clamp(v, 1, 16)`
     runs, in `ExtractFields`.
   - `FindingsFlowRunConfig.MaxConcurrentSpotlightBatches` — flow-facing; a straight carry of the already-clamped
     value, default 4, deliberately NOT re-clamped. A second clamp would be a second authority over one number.
   Flow-level tests set the degree on the DTO; clamp-boundary tests go through the builder against the config.
2. **Ordering guarantee** — published object order, content, object naming, and `outputPage` numbering are
   identical to the sequential implementation for identical input, at any degree.
3. **Degree-1 equivalence** — degree 1 is behaviourally indistinguishable from today.
4. **Consumer exclusivity** — `AdapterProgressContext`, `BatchScopedStorage`, publish, checkpoint write, and
   `AdvancePage` are touched by the single consumer only. Enforced by construction, because
   `AdapterProgressContext` thread-safety is unverifiable (external package, no local source).
5. **Cancellation semantics** — on `OperationCanceledException`, `RecordCooperativeYield` continues to record
   only what the **consumer published**. Producer completions are counted separately and never leak into the
   yield position. Every fetched-but-unpublished batch is re-done on resume; none is skipped.
6. **Checkpoint** — format v4 unchanged. No new keys, no version bump.

## Worker Plan

- **W1** — scope: implement bounded concurrent fetch + serial consumer.
  owns: the implementation paths above. inputs: phase-0 surface, `research/internal-recon.md`.
  output: working implementation + `execution_notes.md` entry (memory formula, chosen default and its basis,
  cancellation semantics as implemented). phase: 1. continuity: **fresh**.
- **W2** — scope: tests proving the phase-0 behavioural contract, plus the test-infra race fix.
  owns: the test project only. inputs: phase-0 surface (**not** W1's implementation).
  output: tests + a statement of what each success criterion is proven by. phase: 1. continuity: **fresh**.

W1 and W2 run concurrently against the frozen surface. W2 writes against the contract, not against W1's code —
this is deliberate and is the reason the path is `decompose`.

## Synthesis Approach

Main thread integrates: run W2's tests against W1's implementation. Disagreements are adjudicated against the
phase-0 surface, not against either worker's output — if W1's behaviour and W2's test disagree, the frozen
contract decides which one is wrong. Re-engage the owning worker (resumed, since it is feedback on its own
output) rather than patching across ownership lines.

## Verification Obligations

- Cross-check against `prompt_contract.md` Success Criteria 1-9.
- Dispose A1-A9. A8 is already contradicted by recon (`research/internal-recon.md`, question 5) — the verifier
  must record it REJECTED with that citation, not NEVER-TESTED.
- Confirm no write occurred in `/Users/user/Dev/cymulate-integration-adapters` (the read-only main checkout).
- Confirm publish→checkpoint adjacency survives: no await introduced between them.
- Confirm the `FakeHttpClientFactory` race fix landed, since every concurrency test result is worthless without it.

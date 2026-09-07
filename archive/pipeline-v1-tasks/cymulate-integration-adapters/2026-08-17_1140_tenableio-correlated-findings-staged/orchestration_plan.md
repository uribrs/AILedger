# Orchestration Plan

Scope of this run: **phase 1 only**, per explicit operator instruction. Phases 2 (vuln emit) and 3
(zero-vuln sweep) are deliberately not started; the operator reevaluates first.

## Scoping correction

The contract and the operator brief both say "the spool phase rewritten to stage" and "delete the
overflow/budget machinery". On dev there is **no correlated code at all** — `TenableIoCollector` still
runs the two-lane combined flow, and the prior branch is not being merged. So:

- there is nothing to rewrite: phase 1 is **created fresh** from the prior branch's specification
- there is nothing to delete: the budget, `overflowMarkers`, the host-less chunk-1 case,
  `TenableIoOverflowChannelPublisher` and the `IAssetSpool` windowed-join seam are simply **never
  ported**. The verifier checks their absence, not their removal.

Consequence for wiring: phase 1 lands as standalone, unit-tested units that are **not** wired into
`TenableIoCollector`'s live path. The two-lane flow stays the default until phases 2–3 land and the
switch happens in one move. A half-wired correlated flow with stubbed phases would be a second runtime
path, which the operator has ruled out.

## Complexity Decision

- Path: **direct**
- Axis scores: Complexity **medium** | Separability **low** | Coupling **high** | Dependency order
  **linear** | Execution risk **medium** | Worker clarity **low**
- Rationale: the staging area's shape and the spool phase that writes through it are one design
  decision expressed in two files — a worker boundary between them would be fuzzy and would need the
  same context on both sides. Only one axis (execution risk, from unfamiliar Infra `Ingestion` surface
  and a real S3 run) argues for splitting, and it argues for *care*, not for handoff. Nothing points to
  decompose.

## Research Decisions

**None needed for this slice.** Every OPEN external-behaviour assumption is either out of scope or
already carries a citation:

- A12 (`S3AdapterObjectStore` registered in every environment) — deployment-time, checkable from the
  local ISB repo during execution; blocks rollout, not phase 1.
- A13 (emitter releases batch scope on a throwing publish) — phase 2 concern (S6), not phase 1.
- A18 (`properties` uuid projection) — not needed under the staged spine; probe deferred.
- A15 (parser correlated-shape support) — Fork C, explicitly out of this slice.
- PA3 (parser prefix collision) — already VALIDATED-conditionally with a citation from the Falcon S3
  task. It *does* bear on phase 1, and its mitigation is a deliverable here (path constant + guard
  test), not a research question.
- PA4 (IRSA delete grant) — deletion is off the correctness path by design; storage accumulation is the
  worst case.
- A1/A2/A3 etc. (carried vendor facts) — phase 1 touches only `/assets/export`, whose behaviour is
  already exercised by the live standalone assets lane.

Implementation-side reading of the Infra `Ingestion` surface and Falcon's staging lifecycle is execution
work, not research, and produces no `research/` file.

## Worker Plan

Not applicable — direct path.

## Execution Sequence

1. Read the Infra surface actually needed: `IAdapterObjectStore` / `ObjectLocation` /
   `ObjectWriteRequest` / `ObjectStat`, `GuardedObjectStore` read+write signatures, `StagingManifest`,
   and Falcon's `FalconStagingArea` for base-URL resolution and generation lifecycle.
2. `TenableIoAssetStagingArea` — generation id, `_staging/{generation}/spine/{uuid}` keyed writes,
   keyed read returning raw bytes, manifest written last, completed-generation discovery, prefix
   listing for the future sweep, GC entry points.
3. `TenableIoAssetSpoolPhase` — the `/assets/export` poll/stream loop from the prior branch, staging
   each record instead of compressing it into a dictionary, with a bounded write fan-out.
4. Tests: staging-path guard (`^(assets|findings)` cannot match), keyed round-trip preserves bytes
   verbatim, missing key reads as null, manifest-last ordering, generation discovery.
5. Build + `dotnet vstest` on the TenableIo test dll only.
6. Prepare the phase-1 proof run in `LocalAdapterRunner` (dev-only surface, mirroring the existing
   Falcon simulation flags) and report what the operator must supply to execute it.

## Verification Obligations

- Cross-check against `prompt_contract.md` Success Criteria, scoped to phase-1 deliverables only;
  criteria covering phases 2–3 are out-of-slice, not failures.
- Assert the *absence* of every not-ported item (budget, `overflowMarkers`, host-less chunk-1,
  overflow channel publisher, windowed-join seam).
- Assert no `PriorStateStore` usage on the spine path, and no round-trip through a typed DTO — `host`
  bytes must be verbatim.
- Assert the live `CollectFindings` path is unchanged by this slice.
- Confirm deletion is not on the correctness path.
- Confirm write concurrency is bounded by the collector, since the façade guards reads only.

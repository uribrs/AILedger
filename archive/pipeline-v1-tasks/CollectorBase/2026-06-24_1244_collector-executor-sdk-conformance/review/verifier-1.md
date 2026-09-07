# Verifier Report — collector-executor-sdk-conformance

Date: 2026-06-24
Verifier: independent (skeptical)

## Per-criterion verdict

### SC1 — `dotnet build CollectorBase.slnx` clean — **PASS**
`dotnet build CollectorBase.slnx` → `Build succeeded. 0 Error(s)` (12 warnings, all NU1507 pre-existing
package-source-mapping warnings, unrelated to this change).

### SC2 — Adapter implements full interface set + ProcessAsync via AdapterBusEntrypointRunner / DelegateCollectorBusEntrypointSource (13 delegates) — **FAIL**
`CollectorExecutorAdapter.cs:18` still declares only `IIntegrationAdapter<ICollectorCapability>, IResumableAdapter`.
No `IAssetsCollectorAdapter`, `IFindingsCollectorAdapter`, `ICollectorAdapter`, `ICollectorEventSink`, or
`IAsyncDisposable`. Only 3 events (`ProgressChanged`/`OperationCompleted`/`ErrorOccurred`), not the 5 SDK collector events.
`ProcessAsync` (line 86-91) calls `_runner.RunAsync(...)` directly — no `CollectorBusEntrypointDefinitionBuilder`,
no `DelegateCollectorBusEntrypointSource`, no `AdapterBusEntrypointRunner`, zero of the 13 delegates.
**Not implemented.** This matches the honestly-reported "NOT LANDED" status in execution_notes.md and state.json (S1-S3 = pending).

### SC3 — Resume via CollectorResumeRunner + CollectorResumeDefinition<CheckpointState> — **FAIL**
`ResumeAsync` (line 93-99) and `CanResumeFrom` (line 101) call `_runner.RunAsync(...)` / `CollectorExecutorRunner.CanResume(...)`
directly. No `CollectorResumeRunner` or `CollectorResumeDefinition` anywhere in the adapter. **Not implemented**;
matches reported status (S4 = pending).

### SC4 — Per-emit-target persisted monotonic page counter, byte-sliced to MaxBytesPerBatch; live run contiguous, no overwrite — **PASS**
Code (CollectorExecutorRunner.cs):
- `runCtx.MaxBytesPerBatch = ThrottlingOptions.Resolve(_context.Services).MaxBytesPerBatch` (line 126) — native source of the limit.
- Publish loop (lines 338-348): `foreach (slice in SliceByBytes(records, runCtx.MaxBytesPerBatch))` →
  `pageNo = isFindings ? ++runCtx.FindingsPage : ++runCtx.AssetsPage` → one `Publish{Findings|Assets}Utf8PageAsync` per slice.
  Page number is the OUTPUT sequence, fully decoupled from the pagination cursor/offset/page (those flow separately
  through `pstep`/`WriteCheckpoint` at line 354-355). Confirmed decoupled.
- `SliceByBytes` (lines 949-968): serialize each record once; flush prior slice when adding would exceed budget;
  a single oversized record forms its own slice (never dropped — the `slice.Count > 0` guard); `maxBytes > 0` guard
  disables slicing for a non-positive budget. Byte-slice logic is correct, including the oversized-record edge case.
- Resume restore present: lines 162-163 seed `runCtx.AssetsPage = state.AssetsPage; runCtx.FindingsPage = state.FindingsPage`.
- Persisted: `CheckpointState.AssetsPage/FindingsPage` (CheckpointState.cs:38-39); written in every `WriteCheckpoint`
  (line 630).

Live run (`out_verify/`): 56 files `assets_000001.json .. assets_000056.json`, **contiguous, zero gaps** (checked 1..56);
total **55,012** NDJSON record lines (files 1-55 = 1000 each, file 56 = 12). Prior buggy behavior produced exactly 1
overwritten file — the collision is eliminated. (Contract cited ~54,937; actual 55,012 — a vendor-data/run-time delta,
immaterial to the defect being fixed and far above 1.)

Note (not a defect): the counter is persisted at step end (WriteCheckpoint after the publish loop), so idempotent-overwrite
on interrupted re-run is at STEP granularity — a step interrupted mid-publish replays from the prior step's persisted base
and overwrites the same page files. This is consistent and matches the native chunk model; the "mid-step re-run re-publishes
from the persisted base" claim is accurate at that granularity.

### SC5 — Existing Tests/CollectorExecutor.Test pass — **PASS**
`dotnet test CollectorBase.slnx` → `Passed! Failed: 0, Passed: 43, Skipped: 0, Total: 43`.

## Consistency check (notes/state vs reality)
Accurate and honest. execution_notes.md and state.json explicitly report S1-S4 as NOT LANDED (designed, not built) and
S5/S6/S7 as complete, with a stated rationale (terminal-model inversion is a behavior-changing refactor, deferred to keep
the build green). The code on disk matches exactly: adapter untouched (SC2/SC3 fail), page-numbering contract landed
(SC4/SC5 pass). No overclaiming.

## Constraint-drift check
No vendor identity leaked into the engine control flow. Grep of CollectorExecutorRunner.cs / Seams.cs / CheckpointState.cs
for tenable/falcon/qualys/crowdstrike/defender/cortex returns only 4 hits, all in explanatory COMMENTS (lines 150, 432,
456, 824) describing strategy behavior — no branching on vendor name. Engine remains vendor-agnostic.

## Correctness concerns in landed code
None blocking. The page-counter change correctly decouples output numbering from the pagination cursor, restores on
resume, and byte-slices correctly (including a single oversized record). Step-granularity idempotency on resume is
acceptable and native-consistent.

## Unmet criteria
- SC2 (adapter interface set + bus-entrypoint routing) — FAIL / not landed.
- SC3 (resume via CollectorResumeRunner + CollectorResumeDefinition) — FAIL / not landed.

## Overall verdict
**PARTIAL** — the page-numbering defect fix (SC1, SC4, SC5) is correctly and verifiably landed; the structural SDK/bus
conformance (SC2, SC3) is honestly reported as not landed and remains the open contract objective.

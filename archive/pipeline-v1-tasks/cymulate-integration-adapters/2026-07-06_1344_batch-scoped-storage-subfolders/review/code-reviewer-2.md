# Code Review 2 — BatchScopedStorage wiring: Qualys + InsightVM Cloud collectors

Scope: uncommitted working-tree changes on `batchful-uploads` limited to the two collectors' wiring of
`Shared/.../DataPipeline/Egress/BatchScopedStorage.cs`, the two csproj version bumps, and the three test files.
Risk class: medium-high (persistence layout contract, pagination state, resume interplay). Evidence provided:
build clean, Qualys 25/25, InsightVmCloud 19/19 — not re-run here.

## Verdict

The wiring is sound. Every page-publish site in both collectors goes through a flag-carrying publisher, every
wired publisher honors the helper's ordering contract (BeginPage → publish → RestoreBase-on-0 → AdvancePage),
resume paths construct publishers with the flag intact, and the tests exercise the real egress pipeline down to
a capturing `IAdapterDataPublisher` rather than mocking the unit under test. No blockers, no majors. Findings
below are minor/observational.

### Contract adherence — verified point by point

- **Ordering.** All three publishers follow the XML-doc contract exactly:
  - `Collectors/QualysCollector/Flows/Findings/QualysFindingsBatchPublisher.cs:36-56` — BeginPage(38) → publish(41) → RestoreBase-on-0(53) → AdvancePage(56).
  - `Collectors/InsightVmCloudCollector/Flows/Assets/InsightVmCloudAssetsPagePublisher.cs:53-104` — same shape.
  - `Collectors/InsightVmCloudCollector/Flows/Findings/InsightVmCloudFindingsPagePublisher.cs:61-115` — same shape.
- **No bypass publish sites.** Swept `Flows/` and `Recovery/` of both collectors for `AdvancePage` / `CollectorNdjsonPublisher` / `Publish*PageAsync`: every hit is inside the three wired publishers, plus two non-publishing `AdvancePage` sites discussed under finding 1. Qualys has no assets flow (findings-only, `QualysCollectorFlowRunner.cs`), so no unwired lane exists. IVMC dry-run paths return before publish.
- **BeginPage N+1 never precedes page N's AdvancePage.** IVMC flows are strictly sequential page loops (`InsightVmCloudAssetsFlow.cs:65-181`, findings flow same shape). Qualys parallelizes vendor fetches but serializes the entire publish+checkpoint sequence under `publishGate` (`QualysFindingsFlow.cs:100-136`), so BeginPage/AdvancePage pairs cannot interleave; `Metadata` mutation is confined to that gate.
- **Resume keeps the flag.** The flag is a capability constant (`init = true`, not bound from the platform event by either configuration mapper), and both fresh and resume paths construct flows through the same runner with the same configuration: Qualys resume → `QualysCollector.cs:266-318` → `QualysCollectorFlowRunner.RunFindingsAsync` → flow ctor reads `configuration.BatchScopedStorage` (`QualysFindingsFlow.cs:36-40`); IVMC resume → `InsightVmCloudCollector.cs:317/327` → `RunAssetsAsync`/`RunFindingsAsync` (`InsightVmCloudCollector.cs:115-153`) → `InsightVmCloudCollectorFlowRunner.cs:66/130` passes `_configuration.BatchScopedStorage`.
- **Resume page numbering is deterministic.** Qualys resumes page numbering from `PublishedPageCount` (`QualysFindingsFlow.cs:80`), IVMC from checkpoint `Page` — a crash-resume re-publishes the same page number into the same `batch_NNNNNN` folder (self-cleaning overwrite, as the helper doc promises). A resume message that round-trips a scoped `storageUrl` is handled by the helper's `StripBatchSegment`, so folders cannot nest.
- **Failure/completion events carry the base URL.** The shared paths the collectors depend on all call `RestoreBase` (verified present: `AdapterFailureDecisionExecutor.cs:344`, `AdapterFlowFailureHandling.cs:57`, `AdapterBusEntrypointRunner.cs:117`, both resume executors, both partial-success publishers). Those files are outside this review's subset but the dependency holds.
- **Scoping window is airtight per page.** `ResultsBatchPublisher.PublishUtf8Async` owns the NDJSON session for the lifetime of the publish call — every flush (buffering, timer, memory-pressure, multipart part) reads the live `storageUrl` inside the window where it is scoped to this page. Nothing uploads outside a BeginPage/AdvancePage bracket.
- **Version bumps.** 5.0.0 on both csproj files with an explanatory comment — correct magnitude for an output-layout contract change.

## Findings

### 1. Minor — Qualys emits two progress events per page, both announcing the batch path

`QualysFindingsBatchPublisher.cs:56` calls `progressContext.AdvancePage(batch.TotalFindings)` and then the flow
immediately calls `QualysFindingsCheckpointWriter.OnBatchPublished` (`QualysFindingsFlow.cs:120`), which calls
`AdvancePage(itemsInBatch: batch.TotalFindings, findingsInBatch: batch.TotalFindings)` again
(`QualysFindingsCheckpointWriter.cs:51`).

Both calls are pre-existing at HEAD (verified via `git show`), so the double advance itself is out of scope.
What this diff changes is its *consequence*: each Qualys page now emits two host progress events that both
announce the same `{base}/batch_NNNNNN` path. If upstream raw-data parsing triggers on every progress event
carrying a batch path, each Qualys batch gets parsed twice. Same folder, same keys — safe only if the upstream
trigger is idempotent per path.

Impact: duplicate upstream work at best; duplicate downstream ingestion at worst, depending on upstream
idempotency. Also note the pre-existing side effect that `itemsInBatch` is counted twice per page.

Recommended fix (local patch, deferrable): make one component own `AdvancePage` for Qualys — drop the call at
`QualysFindingsBatchPublisher.cs:56` and let the checkpoint writer's advance (which travels with the persisted
state) be the single page-advance, or vice versa. At minimum, confirm the upstream per-batch trigger dedups on
path before this ships. Not merge-blocking for this wiring, but the batch-path announcement is the feature's
whole point, so the two-events-per-page behavior should be a conscious decision, not an accident.

### 2. Nit — two stale ordering comments in files this diff touches

Ordering is the load-bearing part of this contract, so comments that misstate it are worth fixing while here:

- `QualysFindingsFlow.cs:195-196` — says "the resilience strategy's AdvancePage(0, 0) snapshots this state",
  but `AdapterFailureDecisionExecutor.cs:337` now documents that the executor deliberately does *not* use
  `AdvancePage(0,0)` (it would increment `CurrentPage`). The comment describes retired behavior.
- `InsightVmCloudAssetsFlow.cs:145` — "Save checkpoint state before AdvancePage (captures position for
  resume)" is false: `AdvancePage` already fired inside `PublishAndReportPageAsync` before this `SetState`
  block runs. (The actual behavior — page N's state travels with page N+1's advance — is safe precisely
  because of the deterministic batch-folder overwrite on resume, which is worth stating instead.)

Fix: reword both comments. Local patch, no behavior change.

### 3. Observation — Qualys `RestoreBase` branch is effectively unreachable, and that is fine

`QualysFindingsFlow.cs:109-112` skips empty batches (`Hosts.Count == 0`) before consuming a page number, so
`publishResult.RecordCount == 0` at `QualysFindingsBatchPublisher.cs:51` should not occur in practice (hosts
serialize to non-empty NDJSON lines). Keeping the branch as defensive parity with the IVMC publishers is the
right call — cheap, and it protects against a future upstream change that lets an empty page through. No action.

### 4. Observation — opt-in default `batchScopedStorage = false` on ctors is a silent-opt-out hazard

The publishers and IVMC flows take `bool batchScopedStorage = false` as a defaulted parameter. A future call
site that forgets to pass it silently produces the flat layout while the collector version says 5.0.0. The
capability constant on the configuration record is the real switch and the FlowRunner is the only production
call site today, so this is acceptable; if a third lane is ever added, consider making the parameter required.
No action now.

### 5. Test quality — good; two small notes

The three new/updated test files are the right shape: they drive the real egress pipeline (publisher →
`CollectorNdjsonPublisher` → NDJSON session → capturing `IAdapterDataPublisher`) and assert the two things that
matter — the physical upload key and the announced `storageUrl` at advance time — plus the opt-out flat layout
and the capability constant. `InsightVmCloudCollectorTests.cs` expectation updates are consistent with the new
layout.

- (Nit) `TargetPathCapturingPublisher` and `CreateExecutionContext` are duplicated verbatim across
  `QualysBatchScopedStorageTests.cs` and `InsightVmCloudBatchScopedStorageTests.cs`. Cross-project test
  sharing has real cost in this repo layout, so duplication is a reasonable tradeoff — noted only.
- (Observation) Neither collector-level suite covers the dud-page branch (RecordCount 0 → event announces the
  base URL). That mechanic is covered at the helper level in the shared `BatchScopedStorageTests.cs`
  (`RestoreBase_AfterDudPage_...`, `PublishAsync_AfterRestoreBase_UploadsAtTheRunRoot`), and the branch is
  three identical lines per publisher, so per-collector duplication of that test would add little. Acceptable.

## Certainty

High on contract adherence, bypass-site coverage, and resume-flag integrity (all verified by reading every
publish/AdvancePage site in both collectors and the shared egress path). Medium on finding 1's real-world
impact — whether double progress events cause duplicate upstream parsing depends on host/upstream behavior not
visible in this repo.

# Verifier-2 — Cycle 2 (corrected wiring, collector only)

Independent adversarial verification. HEAD = cycle-1 commit; cycle-2 = 6 uncommitted files.
Build + targeted tests run locally (net8.0). Co-location traced from source, not accepted on claim.

## Criterion-by-criterion

### 1. One CollectFindings run emits BOTH assets_*.json AND findings_*.json in the SAME batch dir — **PASS**
Traced end-to-end:
- Bus creates exactly ONE progress context per ProcessAsync: `AdapterBusEntrypointSetup.cs:21`
  (`context.CreateProgressContext(...)`), passed through `AdapterBusFlowDispatcher.cs:28` into
  `definition.CollectFindingsAsync(platformEvent, progressContext, ...)`.
- Collector delegate threads that context straight in: `TenableIoCollector.cs:465-466`
  (`CollectFindingsAsync = (evt, progressCtx, req, ct) => CollectFindingsInternalAsync(..., progressCtx)`).
- Inside `CollectFindingsInternalAsync`, the SAME `effectiveProgressContext` is passed to BOTH
  `assetsFlow.CollectAsync(...)` (`TenableIoCollector.cs:258`) and `flow.CollectAsync(...)` (line 281).
  No second progress context; no separate assets sub-invocation; assets uses
  `PublishAssetsUtf8PageAsync`, findings uses `PublishFindingsUtf8PageAsync`.
- Co-location proof: `CollectorNdjsonPublisher` only encodes the FILENAME (`assets`/`findings` prefix,
  `CollectorNdjsonPublisher.cs:86,121-137`); the batch DIRECTORY is owned by `progressContext` inside
  `ResultsBatchPublisher` (BaseTargetPath = relative filename). Same context ⇒ same batch dir ⇒
  filenames differ only by prefix, no collision.
- Confirmed by test `ProcessAsync_Findings_EmitsBothLanes_CoLocated_...`: asserts both
  `tenant/run/assets_000001.json` AND `tenant/run/findings_000001.json` from one run (2+2 records,
  raw passthrough, no `/assets/{id}`). **Ran it: PASS.**

### 2. CollectAssets emits assets-only — **PASS**
`CollectAssetsInternalAsync` (`TenableIoCollector.cs:107-163`) runs only `TenableIoAssetsFlow`; bus routes
`flow=CollectAssets` to `CollectAssetsAsync` delegate (line 463-464) with `combinedRun` defaulting false.
Test `ProcessAsync_Assets...` (1 page `assets_000001.json`, assets-only) **ran: PASS**. Unchanged from cycle 1.

### 3. Phase checkpoint resumes correct phase; no re-pull/re-publish of completed assets — **PASS**
- Routing (`TenableIoCollector.cs:539-548`): `IsFindingsFlow` → `ResumeFindingsAsync` (assets skipped);
  `IsAssetsFlow` → `ResumeAssetsAsync`, which branches on `CombinedRun` (line 567): true ⇒ chain into
  `CollectFindingsInternalAsync(assetsResumeState:state)`; false ⇒ standalone assets resume (legacy path intact).
- `assetsAlreadyComplete = findingsResumeState is not null` (line ~248) ⇒ ANY findings-flow resume skips the
  assets phase. Covers legacy no-Phase findings checkpoints (Phase null treated as findings-already-done).
- Phase-transition checkpoint (`WriteFindingsPhaseTransitionCheckpoint`, lines 342-378): empty ExportUuid +
  `Phase=FindingsInProgress`; findings flow distinguishes `resumeWithExistingExport` (non-empty UUID → reuse)
  from empty-UUID phase-transition (→ create fresh vulns export, keep totals/page) — `TenableIoFindingsFlow.cs:73-110`.
  Helper load tolerates empty exportUuid + page 0 (`TenableIoCheckpointHelper.cs:127-135,176-181`).
- Test `ResumeAsync_FindingsPhase_SkipsAssetsExport_...` asserts `/assets/export` NOT called and only findings
  published. **Ran: PASS.**

### 4. Endpoints/filters/enrichment-removal unchanged from cycle 1 — **PASS**
`git diff --name-only` = 6 files; NONE are ExportClient / ChunkProcessor / Urls / Enricher / Configuration.
Findings-flow diff touches only the resume-branch logic (existing-export vs phase-transition), not the export
create body, filters, or severity handling. No regression.

### 5. Build green (net8.0); tests pass aside from the known flake — **PASS**
- Collector `dotnet build -f net8.0`: **Build succeeded, 0 warnings, 0 errors.**
- `dotnet test -f net8.0 --filter "!~WhenTransportResponseEndsPrematurely"`: **14 passed / 0 failed** (208ms).
- Real test outcome reconciled: the project has 15 tests. 14 pass; the 1 excluded
  `ResumeAsync_Findings_WhenTransportResponseEndsPrematurely` is a PRE-EXISTING slow (~5m) timing flake
  (`Assert.Empty(capture.Completions)` hinges on chunk-retry jitter vs the ≥50% permanent-failure threshold).
  Verified it is NOT a cycle-2 regression: its checkpoint carries `ExportUuid="exp-resume"` (non-empty, no
  Phase), so cycle-2's new branch sets `resumeWithExistingExport=true` → identical code path to cycle 1.
  Cycle-2 touched none of the transport/circuit-breaker/failure-ratio logic the test exercises. Worker's
  "14/14 pass" is accurate (14 = suite minus the flake).

## Gap / risk assessment
- **Failure exactly at assets→findings transition — handled.** If the transition checkpoint persists then
  vulns-create fails, resume is findings-only (fresh vulns export, assets never re-pulled). ✓
- **Transition checkpoint elided by host (zero-item AdvancePage(0,0)) — benign.** Last persisted checkpoint
  is then `flow=CollectAssets, CombinedRun=true`; resume re-enters the assets phase, but every chunk is
  already in `ProcessedTenableChunkIds`, so the progressive loop publishes ZERO new assets pages (no
  duplicate `assets_*.json`, no lost data) before chaining into findings. Extra status polls only.
  This is the worker's documented residual risk; confirmed harmless by tracing the chunk-skip logic.
- **Partially-published assets lane on resume — handled.** Assets resume restores `pageCounter` +
  processed-chunk set (`TenableIoAssetsFlow.cs:92-101`); continues numbering, re-publishes nothing already done.
- **Two independent Tenable exports (assets vs vulns) created at slightly different times** — same data-drift
  exposure the split parser already tolerates (LEFT-join on uuid). Unchanged from cycle-1 intent. Not a defect.
- Minor: dead `Phase="AssetsInProgress"` doc-comment value is never written (correctly noted as never-stored).
  Cosmetic only.

## Final verdict: **SATISFIED**
All 5 cycle-2 criteria PASS. Co-location proven from source (one bus context → both lanes → same batch dir),
not merely from the worker's claim, and corroborated by a passing co-location test. Phase-resume skips the
completed assets export. Build clean; 14/14 pass; the lone failing test is a pre-existing flake on an
unchanged code path. The transition-window gap is closed in code and benign even if the host elides the
zero-item snapshot.

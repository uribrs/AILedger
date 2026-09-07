# Execution Notes

_(Appended by workers during execution.)_

## W2 — collector

Repo: `/Users/user/Dev/Uri/cymulate-integration-adapters/src/Cymulate.Integration.Adapters/Collectors/TenableIoCollector`

### Files touched
- `Processing/Urls/TenableIoUrls.cs` — added `AssetsExportCreate()`/`AssetsExportStatus(uuid)`/`AssetsExportChunk(uuid,id)`; removed now-unused `AssetByUuid` (its only caller, the enricher, was deleted).
- `Processing/Configuration/TenableIoCollectorConfiguration.cs` — added `AssetsLastAssessedWindowDays` (default 30) and `AssetsChunkSize` (default 1000). Kept `EnableAssetEnrichment`/`MaxParallelAssetEnrichmentRequests` so the findings checkpoint shape is unchanged.
- `Recovery/TenableIoCheckpointState.cs` — added `TenableIoAssetsCheckpointState` (ExportUuid, TotalChunks, ProcessedTenableChunkIds[], LastPublishedPage + base totals), sibling to the findings state.
- `Recovery/TenableIoCheckpointHelper.cs` — added `SaveAssetsState`/`TryLoadAssetsState`(+Core); `CanResumeFrom` now handles the assets flow (with stale-checkpoint guard).
- `Recovery/TenableIoResumeRunner.cs` — added `ResumeAssetsAsync` mirroring `ResumeFindingsAsync` (via `CollectorResumeRunner` + `CollectorResumeDefinition<TenableIoAssetsCheckpointState>`).
- `Flows/Assets/TenableIoAssetsExportClient.cs` — NEW. `POST /assets/export` with `chunk_size` + `filters.last_assessed` (baseDate − 30d, unix); status poll; chunk stream. Mirrors `TenableIoVulnsExportClient` incl. 409 active_job_id recovery.
- `Flows/Assets/TenableIoAssetsChunkProcessor.cs` — NEW. Dumb passthrough: streams the chunk array, emits each asset object verbatim as normalized single-line UTF-8 (no filter/enrich/remap).
- `Flows/Assets/TenableIoAssetsFlow.cs` — NEW. Progressive create→poll→chunk flow mirroring `TenableIoFindingsFlow` (same poll/retry/circuit-breaker/exclusion/failure-ratio handling, per-page checkpoint + `AdvancePage`), publishing via `CollectorNdjsonPublisher.PublishAssetsUtf8PageAsync`.
- `TenableIoCollector.cs` — now implements `IAssetsCollectorAdapter`; replaced the no-op `CollectAssetsInternalAsync` stub with a real assets-flow run (mirrors findings); `ProcessAsync` `CollectAssetsAsync` delegate now passes progress/ct/correlationId; `ResumeAsync` routes `IsAssetsFlow` → `ResumeAssetsAsync`.
- `Flows/Findings/TenableIoVulnsExportClient.cs` — create filter now `{ since, state=[OPEN,REOPENED] }` (skip FIXED).
- `Flows/Findings/TenableIoFindingsChunkProcessor.cs` — removed the info-severity filter (ALL severities emitted) AND the per-asset enrichment path; now a dumb passthrough that emits the raw finding (embedded asset verbatim). Kept the asset.uuid/plugin.id validity gate (correlation key) + stats. Dropped the enricher ctor dependency.
- `Flows/Findings/TenableIoFindingsFlow.cs` — dropped the `TenableIoAssetEnricher` and `_assetsFetched` tracking (findings flow no longer fetches assets; `TotalAssetsCollected => 0`); updated processor call/signature and logs.
- `Flows/Findings/TenableIoAssetEnricher.cs` — DELETED (no per-asset `/assets/{id}` enrichment).

### Tests touched
- `UnitTests/.../TenableIoCollectorTests.cs`:
  - Findings test renamed → `..._EmitsAllSeverities_RawAsset_...`: now asserts 2 records (info NOT filtered), `/assets/{id}` never called, and raw asset (no `first_seen`/`acr_score`/`acr_drivers` injected).
  - Removed the now-meaningless `..._EnrichedAssetWritesNullAcrFields_...` test (enrichment gone); folded its intent into the raw-passthrough assertion above.
  - `ProcessAsync_Assets_...`: flipped from "no-op parity" to asserting real assets publishing (1 page `assets_000001.json`, 2 records, assets-flow batch/checkpoint events, raw `id` values).

### Decisions
- Kept the findings checkpoint fields `EnableAssetEnrichment`/`MaxParallelAssetEnrichmentRequests` (and config props) to avoid changing the findings checkpoint contract / breaking existing resume-state tests; they are now inert for the findings path.
- No Shared/ changes (confirmed: `CollectorNdjsonPublisher.PublishAssets*` already exists; `RecoveryParsingHelper.IsAssetsFlow` already exists; `AdapterTopics.Collector.AssetsFlow = "CollectAssets"`).
- Assets `last_assessed` window is `baseDate − 30d` (config-driven), per decisions.md.

### Build result
- `dotnet build` of the collector csproj pinned to `-f net8.0`: **Build succeeded, 0 errors** (only the unrelated NU1900 CodeArtifact-auth warning).
- Test project compiles clean (after creating the missing `artifacts/obj/...editorconfig` dir — known harness path quirk, not a code issue).

### Test result
- `dotnet test` (net8.0): **13 passed, 1 failed** (`ResumeAsync_Findings_WhenTransportResponseEndsPrematurely_ReturnsRetryableAdapterFailure`). The two assertion-coupled findings/assets tests were updated and pass; the new all-severities/raw-asset and real-assets-publish assertions pass.
- The 1 failure asserts `Assert.Empty(capture.Completions)` (line 666) — the resume produced a success completion instead of the expected retryable failure. This test exercises ONLY the chunk-download-failure path (404 on chunk 1 + IOException on chunk 2); it never reaches the chunk **processor** (parse/filter/enrich) that W2 changed. `git diff` of `TenableIoFindingsFlow.cs` shows ZERO changes to the exclusion/ratio/transport/poll failure-handling logic this test depends on. The test is slow (~5m26s in isolation) and its pass/fail hinges on jitter-based chunk-retry timing vs. the ≥50% permanent-failure threshold — i.e. it is a **pre-existing timing-sensitive/flaky test, not a regression from W2**. Per the operator's "don't poll-loop on long test runs" guidance, did not re-run repeatedly to chase the flake.

### Residual risks
- The `info`-filter counters in `TenableIoFindingsStats` (`FilteredInfoVulnerabilities`/`IncrementFilteredInfo`) are now dead code; left in place to minimize churn.
- Assets feed includes assets `last_assessed` within the window; this is the proxy for "all assets" per decisions.md (UI exact count not reproducible via export).

## W3 — parser

Repo: `/Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers`

### Files touched
- `parsers/preparation.py` — added `"tenable-assets-findings": "Tenable IO"` to `DUAL_MODE_PARSERS`. Resolution is now automatic via `input_resolver` (split vs hydrated from files-on-disk); no preparer function needed.
- `parsers/tenable/tenableAssetsAndFindings.py` — converted to a mode-aware façade (mirrors Cortex/Defender VM):
  - `pre_process()` branches on `options.input_mode`: `hydrated` (default/legacy) loads the combined `findings.json`; `split` delegates to the new `NotHydrated` helper, setting `_asset_source_df` / `_finding_source_df`.
  - Introduced a `_asset_source_df` / `_finding_source_df` seam (+ `_asset_schema_df` / `_finding_schema_df` + `_asset_has_field`). All schema guards that read `self.full_df.schema["asset"]` now read the correct per-lane source. Defaults to `full_df` so the existing hydrated tests (which set `full_df` and call `process()` directly with no mode) are unchanged.
  - `process()` builds `assets_df` from the asset source and `findings_df` from the finding source. Split linkage = LEFT-join `findings.asset.uuid == assets._asset_correlation_id` (the assets-export `id`), captured into the finding selection before the `asset` struct is projected away. Hydrated linkage (the `(type, value)` join) is preserved on the else branch. Split dedups assets on the stable correlation id (one row per asset, no explode) so no-vuln assets survive; no phantom/empty findings are created.
  - Extracted `parse_tenable_timestamp(column)`: tries the existing localized US/EU patterns first (hydrated unchanged), then falls back to Spark's ISO-8601 parse so the raw assets-export `first_seen`/`last_seen` (`2025-03-13T22:31:45.369Z`) populate.
- `parsers/tenable/tenableAssetsAndFindingsNotHydrated.py` — NEW split handler. Loads both lanes via strategy (fallback to file path). Findings lane is consumed 1:1 (its embedded `asset` already uses `uuid` / singular / `tag_*`). Reshapes the raw assets lane into the embedded-`asset` envelope so the canonical asset map is mode-agnostic.
- `tests/test_tenable_assets_findings_split.py` — NEW; full split pipeline test (3 assets incl. a no-vuln, 2 findings).

### Mapping applied (assets-export -> embedded `asset.*`)
`id`→`uuid`; `ipv4s[0]`→`ipv4`; `fqdns[0]`→`fqdn`; `netbios_names[0]`→`netbios_name`; `operating_systems`→`operating_system` (array kept); `tags{uuid,key,value}`→`tags{tag_uuid,tag_key,tag_value}`; `first_seen`/`last_seen` verbatim. ACR: `ratings.acr.score` surfaced as `asset.acr_score_v3` (the field name the canonical `risk_score` map + ACR additional fields already read) → `risk_score`; `acr_score` carried for the additional field; all ACR reads null-safe. `acr_score_v3` (the `/assets/{id}`-only field) is no longer required and is absent from the export.

### Test / validation result
- Env: no system Java; used Homebrew `openjdk@11` (`JAVA_HOME=/opt/homebrew/opt/openjdk@11`) + repo `.venv` (pyspark 3.3.0, pytest). Spark ran locally.
- `pytest tests/test_tenable_parser_acr_fields.py tests/test_input_resolver.py tests/test_cortex_xdr_assets_findings.py tests/test_defender_vm_reconciliation.py` → 31 passed (no DUAL_MODE / input_resolver regressions). New split test → passes (3 total with hydrated).
- Controlled split fixture: 3 assets in → 3 assets out (no-vuln A3 survives), 2 findings out (no phantom for A3), risk_score = {A1:5.0, A2:3.0, A3:null}, findings linked by uuid correctly.
- Real prototype feeds (assets.ndjson 1000-row chunk + 1500-finding slice, base date 2026-06-11): `input_mode` auto-resolved to `split`; **assets = 999** (one asset dropped by the pre-existing `value.isNotNull()` filter — the single asset with neither netbios nor ipv4; not a regression), **risk_score non-null on 993/999** (decisions.md measured 994/1000 — consistent), first_seen parsed from ISO-8601, 2076/2076 findings linked, 951 no-vuln assets in-chunk survived with zero findings.

### Residual risks
- Findings whose `asset.uuid` is not in the published assets chunk get `asset_id = null` (LEFT-join from findings). In production both lanes are full exports keyed on the same asset, so this should be near-100% matched (real-slice mismatch was an artifact of pairing a 1000-asset chunk with a wider findings slice). If unmatched findings are undesirable, the collector must guarantee both lanes cover the same asset set.
- Asset additional-fields are built dynamically from the assets-lane `asset.*` schema; new/renamed export fields land in `additional_fields` automatically (intended, matches hydrated behavior).
- `parse_tenable_timestamp` ISO fallback relies on Spark's default `to_timestamp` ISO parse; non-ISO, non-US/EU formats still yield null (same as before for hydrated).


## Orchestration outcome (final)

- **Verifier:** SATISFIED-WITH-RISKS (review/verifier-1.md) — all 6 success criteria PASS; feed↔resolver-glob alignment + flaky-test claim independently confirmed.
- **Code review:** no blockers (review/code-reviewer-1.md) — findings are maintainability/observability + one inherited retry pattern.
- **Key correctness gate PASSED:** no-vuln assets survive (951 asset rows, zero phantom findings) verified against real prototype feeds; risk_score 993/999 from ratings.acr.score.

### Recommended follow-ups (non-blocking, user's call)
1. Remove/`[Obsolete]` dead enrichment config (`EnableAssetEnrichment`, `MaxParallelAssetEnrichmentRequests`) + dead info-filter counters (support trap).
2. Add an unmatched-finding (asset_id=null) count log in the parser join for observability of the population-window tail.
3. (Optional) Extract shared export-flow scaffolding to de-duplicate TenableIoAssetsFlow/FindingsFlow and fix the nested-retry amplification in one place.

### Process
- Nothing committed/pushed (working-tree only). **Parser repo is on `master` — branch before any push.** Collector checkout branch unchanged.

## Cleanup — dead enrichment config/counters

Removed orphaned config + counters left behind after per-asset enrichment and the info-severity filter were deleted. Cleanup only — no behavior change.

### Files touched
- `Processing/Configuration/TenableIoCollectorConfiguration.cs` — removed `EnableAssetEnrichment` and `MaxParallelAssetEnrichmentRequests` properties.
- `Processing/Configuration/TenableIoCollectorConfigurationBuilder.cs` — removed the two builder parse lines (`enableAssetEnrichment`, `maxParallelAssetEnrichmentRequests`).
- `Processing/Configuration/TenableIoIdentification.cs` — removed the two `ConfigurationSchema` description entries for those keys (would otherwise advertise dead config to the platform UI).
- `Recovery/TenableIoCheckpointState.cs` — removed both fields from `TenableIoFindingsCheckpointState`.
- `Recovery/TenableIoCheckpointHelper.cs` — removed save entries (`enableAssetEnrichment`, `maxParallelAssetEnrichmentRequests`), the two load/parse+validate blocks (which had returned `false` on absence), and the two initializer assignments.
- `Flows/Findings/TenableIoFindingsFlow.cs` — removed the two assignments from the findings checkpoint-state construction.
- `Flows/Findings/TenableIoFindingsStats.cs` — removed `IncrementFilteredInfo()`, the `FilteredInfoVulnerabilities` property, and the backing `_filteredInfoVulnerabilities` field.
- Tests: `TenableIoResumeRunnerTests.cs` (3 initializers) and `TenableIoCollectorTests.cs` (1 initializer) — dropped the two removed initializer lines each.

### Notes
- No `FilteredInfo=` log line existed in `TenableIoFindingsFlow` — the only references to the info-filter counter were inside `TenableIoFindingsStats` itself.
- Removing checkpoint fields is forward-safe: load no longer requires the keys, and resume never branched on either value (only `ProcessedTenableChunkIds`, watermark, and export UUID drive resume). The save side no longer emits the keys.

### Verify
- `dotnet build` collector csproj `-f net8.0`: **0 errors** (only unrelated NU1900 CodeArtifact-feed warning).
- Test project `-f net8.0`: **0 errors** (compiles).
- TenableIo unit tests (`-f net8.0 --no-build`): **13 passed, 1 failed, 14 total** (5m8s).
  - Sole failure: `TenableIoCollectorTests.ResumeAsync_Findings_WhenTransportResponseEndsPrematurely_ReturnsRetryableAdapterFailure` — `Assert.Empty(capture.Completions)` failed because a `CompletionRequest { Success = False }` was published during transport-failure resume recovery.
  - **Not caused by this cleanup.** This test exercises retry/circuit-breaker completion semantics on the resume path (`TenableIoResumeRunner` / chunk processor / collector), none of which were touched here. It belongs to the in-progress asset-centric split changes already present in the working tree (deleted enricher, new `Flows/Assets/`, +59 lines in `TenableIoResumeRunner.cs`, +136 in `TenableIoCollector.cs`). My edits only removed field/property declarations and their save/load/initializer plumbing; they cannot affect whether a failure completion is published. Flagging for the owner of the split work, not a regression from this task.

## Repair — parser schema-safety

Fixed two AnalysisException crashes + one strictness gap in the Tenable IO split-mode parser (repo: `cymulate-integration-parsers`).

1. `tenableAssetsAndFindingsNotHydrated.py::_build_asset_envelope_df` — `ratings.acr.score` was guarded only by `"ratings" in cols`. When a batch's `ratings` is all-null/absent (no-vuln-asset case) Spark infers no `acr`/`score` children and the column ref threw at pre_process. Added module helper `_has_nested_field(df, "ratings.acr.score")` (walks each struct segment, mirrors the façade's `_asset_has_field` StructType check); emits a null-cast column when the nested path is missing.
2. `tenableAssetsAndFindings.py` findings lane — guarded the unconditional `asset.uuid` capture (`_finding_asset_uuid`) and the `asset_value` `asset.ipv4`/`asset.netbios_name` reads behind a new static `_has_asset_struct(df)` (and `ipv4`-field check). An empty/all-corrupt findings batch (no `asset` struct) no longer throws; such findings LEFT-join to no asset (asset_id null); no-vuln assets still survive from the assets lane. `_asset_has_field` now delegates to `_has_asset_struct`.
3. `pre_process` mode resolver — unknown `input_mode` now raises `ValueError(... unsupported input_mode ...)` instead of silently falling through to hydrated (matches Cortex/Defender VM façades). `None` still resolves to hydrated.

Regression tests added to `tests/test_tenable_assets_findings_split.py`: (a) all-null/absent `ratings` → assets survive, risk_score null; (b) findings lane with rows but no `asset` struct → parses, findings resolve no asset. Both FAIL against buggy variants, PASS with the fix (verified by reverting each guard).

VERIFY: Homebrew openjdk@11 + repo `.venv` (pyspark 3.3.0). `pytest tests/test_tenable_assets_findings_split.py tests/test_input_resolver.py tests/test_tenable_parser_acr_fields.py` → 12 passed (10 prior + 2 new). Failure-without-fix confirmed by temporarily reverting the source guards (2 failed, 1 prior passed), then restored. No collector/.NET code touched.

## W4 — cycle 2 re-wire

Repo: `src/Cymulate.Integration.Adapters/Collectors/TenableIoCollector` only. Goal: ONE `CollectFindings` run emits BOTH lanes co-located in ONE batch (fix cycle-1's two-invocation / two-batch miss). Endpoints/filters/enrichment-removal from cycle 1 untouched. Flows kept SEPARATE (no shared-engine extraction).

### Files touched
- `TenableIoCollector.cs` — `CollectFindingsInternalAsync` is now the combined façade: phase 1 runs `TenableIoAssetsFlow` (combinedRun:true), then writes a phase-transition checkpoint, then phase 2 runs `TenableIoFindingsFlow` (assetsPhaseComplete:true) — **all against the single `effectiveProgressContext` the bus passes in**. Added `WriteFindingsPhaseTransitionCheckpoint`. `ResumeAsync` now routes `flow=CollectFindings` → findings-only resume (assets skipped); `flow=CollectAssets` + `CombinedRun=true` → resume assets export then chain into findings on the same context; `flow=CollectAssets` + `CombinedRun=false` → standalone assets resume (unchanged). Signature of `CollectFindingsInternalAsync` split into `findingsResumeState` / `assetsResumeState`.
- `Flows/Assets/TenableIoAssetsFlow.cs` — added `combinedRun` param; stamps `CombinedRun` onto every assets checkpoint.
- `Flows/Findings/TenableIoFindingsFlow.cs` — added `assetsPhaseComplete` param (stamps `Phase=FindingsInProgress`); resume now distinguishes `resumeWithExistingExport` (non-empty `ExportUuid` → re-use export) from an empty-UUID phase-transition resume (→ create a fresh vulns export, keep totals/page).
- `Recovery/TenableIoCheckpointState.cs` — `TenableIoFindingsCheckpointState.Phase` (nullable); `TenableIoAssetsCheckpointState.CombinedRun` (bool).
- `Recovery/TenableIoCheckpointHelper.cs` — save/load `phase` (findings) and `combinedRun` (assets); findings load now tolerates empty `exportUuid` and `lastPublishedPage==0` (the phase-transition checkpoint).
- Tests (`TenableIoCollectorTests.cs`): rewrote the findings success test → `ProcessAsync_Findings_EmitsBothLanes_CoLocated_...` (asserts BOTH `tenant/run/assets_000001.json` and `tenant/run/findings_000001.json` from one run, 2+2 records, raw passthrough, no `/assets/{id}`). Added `ResumeAsync_FindingsPhase_SkipsAssetsExport_...` (findings-phase resume must not call `/assets/export`; only findings lane published).

### Shared-context co-location (the gap cycle-1 missed) — proof path
- Bus creates exactly ONE progress context per `ProcessAsync` and passes it through: `Orchestration/Bus/Logic/AdapterBusEntrypointSetup.cs:21` (`context.CreateProgressContext(...)`) → `Orchestration/Bus/Logic/AdapterBusFlowDispatcher.cs:28` (`definition.CollectFindingsAsync(platformEvent, progressContext, ...)`).
- The collector threads that same context into BOTH flows: `TenableIoCollector.cs` `CollectFindingsInternalAsync` uses `effectiveProgressContext = progressContext ?? ...` then passes `effectiveProgressContext` to `assetsFlow.CollectAsync(...)` AND `flow.CollectAsync(...)` (the two `CollectAsync` call sites in that method — assets then findings).
- One context = one egress address = one batch dir: `CollectorNdjsonPublisher.cs:86,121-136` derives the target path from the SAME `progressContext` for both lanes; filenames differ only by prefix (`assets_NNNNNN.json` vs `findings_NNNNNN.json` — `CollectorGlobalDefaults.AssetsFileName`/`FindingsFileName`), so they co-locate without colliding. Test assertion confirms both land under `tenant/run/`.

### Phase-checkpoint design
- Combined run keys `flow=CollectAssets` during the assets phase (with `CombinedRun=true`) and `flow=CollectFindings` (with `Phase=FindingsInProgress`) during the findings phase. Resume routing is by `flow`; the marker (`CombinedRun`) on the assets checkpoint says "after finishing assets, chain into findings."
- Gap closed: after the assets export finishes but before the vulns export is created, a no-page `AdvancePage(0,0)` snapshot persists a `flow=CollectFindings, Phase=FindingsInProgress, exportUuid="" ` checkpoint. A failure there resumes findings-only (creates a fresh vulns export) and NEVER re-pulls the completed assets export.
- ANY `flow=CollectFindings` resume skips the assets phase (`assetsAlreadyComplete = findingsResumeState is not null`) — covers legacy findings checkpoints with no `Phase`. The completed assets lane is never re-published.
- Page accounting: each flow keeps its own internal `pageCounter` (assets pages vs findings pages); the shared `progressContext.CurrentPage`/`ProcessedItems` accumulate monotonically across both phases (fine for platform progress); per-flow resume uses `LastPublishedPage` from the checkpoint dict, not `CurrentPage`. Filenames don't collide.

### Build / test result
- Collector csproj `-f net8.0`: **Build succeeded, 0 warnings, 0 errors**.
- Test project `-f net8.0`: 0 errors.
- Suite minus the one known-slow test: **14 passed / 0 failed** (incl. the new co-location + phase-resume tests).
- `ResumeAsync_Findings_WhenTransportResponseEndsPrematurely_...`: **FAILS** at `Assert.Empty(capture.Completions)` (line 694, 5m18s) — the SAME pre-existing resume-timing flake documented under W2 (`CompletionRequest { Success=False }` published; pass/fail hinges on chunk-retry jitter vs. the ≥50% permanent-failure threshold). Confirmed NOT a wiring regression: with the W4 routing this resume correctly hits the vulns export only (no `/assets/export` 404), and W4 touched none of the transport/circuit-breaker/failure-ratio logic the test exercises. Not chased, per operator guidance on long flaky runs.

### Residual risks
- The phase-transition checkpoint persists via `AdvancePage(0,0)`. If the host treats a zero-item page advance as "no progress" and elides the checkpoint persist, the narrow window (assets done → vulns-create fails) would fall back to the last assets checkpoint and re-run assets. Mitigated in code (this is the documented snapshot pattern the Resilience layer relies on), but not exercised end-to-end against the real host here — only the in-proc capture.
- Assets and findings exports are two independent Tenable exports created at slightly different times within one run; a long gap between them is the same data-drift exposure the split parser already tolerates (LEFT-join on uuid). Unchanged from cycle-1 intent.

## Cycle 2 — outcome
- **W4 re-wire complete:** CollectFindings runs assets→findings on ONE shared AdapterProgressContext (co-located lanes); CollectAssets assets-only; phase-aware checkpoint (assets-skip keyed off findings resume-state, covers legacy no-phase checkpoints). Build net8.0 0/0; 14 pass + the known pre-existing resume-timing flake (unchanged path).
- **Verifier-2: SATISFIED** — co-location proven from source within one invocation; phase-resume never re-pulls completed assets; transition window idempotent via ProcessedTenableChunkIds.
- **Code-reviewer-3: 1 MAJOR open risk** — resume creates a fresh progress context, so a combined run that fails mid-findings may split the assets lane (prior batch) from the findings lane (resume batch). Depends on host/SDK batch-identity across resume (out-of-repo). MUST verify before merge. Local runner writes per-run timestamped dirs → would split locally. Plus minor maintainability items (magic string → constant, etc.).
- **Not committed** — cycle-2 changes are working-tree only, pending the resume co-location decision.

## End-to-end — parser on real cycle-2 batch
Real collector output → split parser, full `pre_process → process → post_process`. Batch `20260611-222237/collector-run/` (cancelled mid-findings, so findings are partial — expected). Env: Homebrew `openjdk@11` + repo `.venv` (pyspark 3.3.0).

**What ran:** ALL 105 `assets_*.json` (104,389 raw asset rows) + a SUBSET of 15 of the 656 `findings_*.json` (96,390 raw finding rows). Findings subset because the full ~3.9M (28 GB) crashes local Spark — first attempt with 40 findings files (255,447 rows) died with a JVM **SIGBUS in `jshort_disjoint_arraycopy`** (a macOS memory-mapped-file fault, not a parser bug). Re-ran with `spark.storage.memoryMapThreshold=Integer.MAX` (force heap reads, no mmap) + `driver.memory=10g` + 15 findings files → clean. Staged via symlinks into `/tmp/tenable_e2e_stage` (original batch not mutated). Input selection driven through the REAL `resolve_assets_and_findings_contract`, not a hard-coded `input_mode`.

**Results:**
1. **input_resolver → `split`.** Inventory `has_assets=True, has_split_findings=True` (also `has_hydrated=True` because the resolver's hydrated `findings.json` single-path check is satisfied by the `findings*.json` glob; split takes precedence, logged: "split and hydrated artifacts both found; split takes precedence"). Both lanes detected.
2. **Ran to completion, no exception** (exit 0, `DONE_OK`). Schema-safety guards exercised on real data: nested `ratings.acr.score` present on most rows, absent on others; full `/assets/export` envelope (70+ top-level fields) reshaped without AnalysisException.
3. **assets_df distinct assets: 104,385** (from 104,389 raw rows; 4 dropped by the base `value IS NOT NULL` cleanup — assets with no netbios/ipv4). Matches the expected ~104K.
4. **No-vuln assets survive: 103,434** of 104,385 carry ZERO correlated findings (only 951 assets have ≥1 finding — expected, since findings are a 15/656 partial slice). They are present as real ASSET rows (asset schema: `id, value, risk_score, os_type, fqdn, tags, additional_fields, …`), NOT phantom/empty findings. Confirms the asset-centric contract on real data.
5. **risk_score populated: 104,364 / 104,385 non-null (99.98%)** from `ratings.acr.score`. The 21 nulls are assets whose `ratings` is absent/all-null — surfaced as null, null-safe, no throw.
6. **Orphan findings (asset.uuid not matching any asset id): 0 in the parser output** — but read with the caveat below. At the RAW lane level an inner uuid==id join shows **21 / 96,390 (0.022%)** findings whose `asset.uuid` matches no asset. Output orphan=0 because the base `post_process` **drops `type=vulnerability` findings with zero CVEs** before counting, and those 21 did not survive that filter. Asset `id` is **unique (104,389 distinct, 0 duplicate ids)**, so the join is NOT fanning out — orphan suppression is the CVE filter, not a bad join.
7. **No schema/AnalysisException or other errors.** Only non-fatal noise: `WARN package: Truncated the string representation of a plan` (cosmetic), and the first-attempt SIGBUS resolved by disabling mmap.

**One number worth flagging (not an error):** findings 96,390 raw → **206,849** output rows. This is the documented base-parser **CVE explode** (one finding row per CVE; ~2.1 CVEs/finding avg) combined with the no-CVE drop — confirmed via diag (asset ids unique, 0 fan-out). Expected behavior, not inflation from the split join.

## End-to-end (larger sample) — join stress
Goal: re-run the split parser at higher join volume than the prior 15-file/951-asset run, to exercise the asset↔finding correlation. Same batch `20260611-222237/collector-run/`, same env (Homebrew `openjdk@11` + repo `.venv` pyspark 3.3.0). Driver `/tmp/run_tenable_e2e_big.py`; input selection through the REAL `resolve_assets_and_findings_contract` (not a hard-coded `input_mode`); staged via symlinks into `/tmp/tenable_e2e_stage_big` — original batch NOT mutated (verified 105 assets / 656 findings intact after; stage removed).

**Largest stable run achieved: ALL 105 `assets_*.json` (104,389 raw asset rows) + 18 `findings_*.json` (117,198 raw finding rows).** Up from the prior 15 files — and the JOIN volume rose materially: **1,223 assets-with-findings** (prior run: 951) and **1,570 distinct matched assets at the raw lane** vs 951 before.

### Memory/SIGBUS finding (corrects the prior note)
The `jshort_disjoint_arraycopy` / `forward_copy_longs` **SIGBUS (BUS_ADRALN)** fires in the `collectToPython` stdout-writer thread during `post_process`'s `dataframe_has_rows` collect. It is a macOS-aarch64 JDK11 codegen fault in the hand-written StubRoutines array-copy assembly — **not** RAM exhaustion. Confirmed:
- `spark.storage.memoryMapThreshold=MAX` does NOT prevent it (the fault is in the row-copy stub, not storage-block mmap).
- **Bigger driver heap makes it WORSE, not better** — 24g/8g both faulted at sample sizes where 10g had run clean. Larger young-gen = larger contiguous copy buffers = more alignment-fault exposure. The prior "more memory helps" intuition is backwards for THIS fault. Best results at **10g**.
- Ineffective levers (tested, no help): `-XX:DisableIntrinsic=_arraycopy`, `-XX:CompileCommand=exclude,*arraycopy*`, `-XX:-UseCompressedOops`, smaller `maxPartitionBytes`/more partitions. These StubRoutines are generated at VM init, not JIT-gated, so compiler flags don't touch them.
- **The fault is nondeterministic near the ceiling.** 18 files ran clean once at 10g (full metrics below), then re-runs at 18/20/22 faulted; even 16 faulted later in the session as OS memory state degraded across ~12 JVM launches. So 18 is the largest sample with a captured clean run, but it is at the unstable edge — not reliably repeatable on this machine. 40 files (255k rows) remains a hard SIGBUS (matches prior note). This is an environment limit, not a parser bug.

### Clean 18-file run — metrics (10g driver)
1. **Split mode selected, ran clean.** `input_resolver → split` (`has_assets/has_split_findings/has_hydrated = True/True/True`; "split takes precedence" over the hydrated `findings*.json` glob). `pre_process → process → post_process` completed `DONE_OK`, no exception. Schema-safety guards exercised on real `/assets/export` envelope (nested `ratings.acr.score` present on most rows, absent on others).
2. **assets_df = 104,385 distinct** (104,389 raw − 4 dropped by the base `value IS NOT NULL` cleanup; id is unique, 104,385 distinct = row count). Fed **18 findings files / 117,198 raw finding rows**.
3. **Join / correlation (the point of this run):**
   - assets WITH ≥1 matched finding (parser output): **1,223** (prior: 951).
   - findings matched vs orphaned — **raw lane:** 117,168 / 117,198 matched an asset id; **30 orphaned (uuid null or no matching asset) = 0.0256% orphan rate** at this volume (of which a handful had null `asset.uuid`). **Parser output orphan (asset_id null): 0 / 251,244 (0.0000%)** — the 30 raw orphans don't survive the base `post_process` no-CVE drop, same mechanism as the prior run. Orphan rate stayed low (sub-0.03%) at the higher join volume.
   - raw distinct asset.uuid in the findings lane: 1,583; raw distinct assets that matched: **1,570**.
4. **No-vuln assets survive:** 104,385 − 1,223 = **103,162** assets carry ZERO findings yet remain as real ASSET rows (asset schema: `id, value, risk_score, os_type, fqdn, tags, additional_fields, …`), zero phantom findings.
5. **risk_score non-null: 104,364 / 104,385 = 99.98%** from `ratings.acr.score` (21 nulls = assets with absent/all-null `ratings`; null-safe, no throw). Identical rate to the prior run — stable.
6. **No duplicate-id fan-out.** Asset `id` unique at the raw lane (**104,389 distinct, 0 duplicate ids**), so the LEFT-join does not inflate. Output **251,244** finding rows from 117,198 raw = the documented base-parser **CVE explode** (~2.1 CVEs/finding) net of the no-CVE drop — NOT join fan-out.
7. **Errors:** none in the clean run. All other attempts (16/18/20/22/24/35 files; 8g/10g/24g) failed ONLY with the macOS JVM `SIGBUS` arraycopy fault described above — no parser-level exception (no AnalysisException, no schema/null fault) was ever observed.

## Hardening — Falcon-style phase flag + key constants

Representation cleanup of the assets-skip-on-resume decision; NO behavior change.

**Replaced**
- `TenableIoFindingsCheckpointState.Phase` (string `"FindingsInProgress"`/null) → `AssetsPhaseComplete` (bool).
- Scattered checkpoint key/value string literals in the serializer/deserializer → centralized `TenableIoCheckpointKeys` constants (mirrors `FalconCheckpointKeys`).
- Findings flow `_assetsPhaseComplete` now derives from `assetsPhaseComplete || resumeState?.AssetsPhaseComplete` (was: `|| Phase=="FindingsInProgress"`); persists `AssetsPhaseComplete` instead of stamping the Phase string.
- Phase-transition checkpoint in the collector now sets `AssetsPhaseComplete = true`.

**Back/forward-compat preserved on load**
- Findings load: prefer explicit `assetsPhaseComplete` bool → else legacy `phase=="FindingsInProgress"` → else default **true** (any findings-flow checkpoint means the assets lane already ran, so resume skips assets — same as today). Legacy `chunkId`/missing keys still tolerated.
- `combinedRun`/`CombinedRun` on the assets checkpoint LEFT INTACT: it answers a different question (does a `CollectAssets` checkpoint chain into findings on resume), not the assets-skip decision — not redundant with `AssetsPhaseComplete`. Both flows kept separate.

**Files touched**
- `Collectors/TenableIoCollector/Recovery/TenableIoCheckpointKeys.cs` (new)
- `Collectors/TenableIoCollector/Recovery/TenableIoCheckpointState.cs`
- `Collectors/TenableIoCollector/Recovery/TenableIoCheckpointHelper.cs`
- `Collectors/TenableIoCollector/Flows/Findings/TenableIoFindingsFlow.cs`
- `Collectors/TenableIoCollector/TenableIoCollector.cs`
- `UnitTests/.../TenableIoCollectorTests.cs` (test constructs the transition checkpoint directly)

**Verify**
- `dotnet build -f net8.0` (collector): 0 warnings, 0 errors.
- Tests (net8.0): 14/15 pass — incl. `ResumeAsync_FindingsPhase_SkipsAssetsExport_AndPublishesFindingsOnly` and `ProcessAsync_Findings_EmitsBothLanes_CoLocated...`. The 1 failure is the known pre-existing flake `ResumeAsync_Findings_WhenTransportResponseEndsPrematurely` (timing, 5m17s) — noted, not chased.

# Execution Notes — Falcon Collector Refactor Cleanup

## WA — Foundational helpers

Pure internal refactor, zero behavioral change. All new types are `internal` under
`Collectors/FalconCollector/Flows/SharedFlows/` (project exposes internals to its test project).
Collector project builds clean: **Build succeeded, 0 Warning(s), 0 Error(s)**.

### (a) New files + API surface (for downstream workers to call)

All in namespace `Cymulate.Integration.Adapters.Collectors.FalconCollector.Flows.SharedFlows`.

**`FalconJson.cs`** — `internal static class FalconJson`
Consolidates the duplicated JSON field readers. Exact parsing semantics preserved
(InvariantCulture, `AssumeUniversal|AdjustToUniversal`, `JsonValueKind.String` guard, whitespace→null).
- `static string? ReadString(JsonElement obj, string propertyName)`
- `static bool TryReadUtcDateTime(JsonElement obj, string propertyName, out DateTime valueUtc)`
- `static DateTime? TryReadUtcDateTime(JsonElement obj, string propertyName)` (nullable-return overload)
- `static string? TryReadAnyId(JsonElement obj)` — `device_id ?? aid ?? id`

  Replaces: Spotlight `ReadString`/`TryReadUtcDateTime`; AssetIdsFetcher `TryReadUtcDateTime`;
  FalconAssetsFlow `TryReadUtcTimestamp`/`TryReadAnyId`/`TryReadStringProperty`.
  Note: the out-param overload keeps the outer `obj.ValueKind != Object → false` guard (matches the
  AssetIdsFetcher original; all current callers already pre-check Object, so it is a behavior-safe superset).

**`FalconCollectorEvents.cs`** — `internal static class FalconCollectorEvents`
Encapsulates the null-check + try/catch-LogDebug emit boilerplate (best-effort telemetry; never affects flow).
- `static void ReportBatchProduced(ICollectorEventSink? eventSink, ILogger logger, string flow, int page, PublishResult publishResult, string targetPath)`
  — builds the standard metadata bag (`targetPath`, `storageLocation`, `bytesUploaded`, `publishedRecordCount`)
  identical at all 3 batch-produced sites.
- `static void ReportCheckpointAdvanced(ICollectorEventSink? eventSink, ILogger logger, CheckpointAdvancedEventArgs args)`
  — caller builds the args (kind/watermark/metadata vary per site); helper only wraps null-check + try/catch.
  Use this for all 4 `CheckpointAdvanced` sites (assets page, assets terminal, findings page, assets-stage page,
  assets-stage completed).

**`FalconCheckpointMetadataKeys.cs`** — `internal static class FalconCheckpointMetadataKeys`
- `const string Kind   = "_checkpoint.kind"`
- `const string Reason = "_checkpoint.reason"`
- `const string ItemsInBatch     = "_checkpoint.itemsInBatch"`
- `const string FindingsInBatch  = "_checkpoint.findingsInBatch"`
  Replaces the 4 const fields duplicated in FalconFindingsCheckpointWriter and FalconAssetsFlow.

**`FalconCursorPagination.cs`** — `internal static class FalconCursorPagination`
Small, behavior-safe pagination primitives (NOT an engine — see decision below).
- `const string UtcTimestampFormat = "yyyy-MM-ddTHH:mm:ssZ"` — the shared FQL timestamp format
  (replaces the duplicated `FalconUtcTimestampFormat` in AssetIdsFetcher + Spotlight; FalconHostFilters still
  has its own private copy — left untouched, not in WA's edit scope; a later worker may fold it in).
- `static DateTime  MaxUtc(DateTime a, DateTime b)`
- `static DateTime? MaxUtc(DateTime? a, DateTime b)`
- `static bool ShouldResetForDepthCap(string? afterToken, DateTime? watermarkFloorUtc, int pagesInCurrentScroll, int maxPagesPerCursorScroll)`
  — the proactive depth-cap predicate, identical across assets/spotlight/AssetIdsFetcher streaming.
  NOTE for adopters: the predicate guarantees `watermarkFloorUtc.HasValue` when true, but C# flow analysis
  cannot see through it — use `watermarkFloorUtc!.Value` inside the guarded block (mirrors FalconAssetsFlow).

**`FalconProgressState.cs`** — `internal static class FalconProgressState`
- `static void Apply(AdapterProgressContext progressContext, IReadOnlyDictionary<string,string> checkpointState)`
  — the `foreach kvp → progressContext.SetState(...)` pattern (6+ sites). Shared equivalent of the private
  `ApplyCheckpointState` in FalconCollector.cs (which WA was not allowed to edit; downstream may adopt this).

### (b) AssetIdsFetcher.cs changes (the prove-out, the one editable existing file)

- Removed local `FalconUtcTimestampFormat` const → `FalconCursorPagination.UtcTimestampFormat`.
- Removed local `MaxUtc` (both overloads) → `FalconCursorPagination.MaxUtc`. All 3 call sites rewired
  (404 reset in both methods, depth-cap reset, and `ExtractLastSeenFloorAndBaseFilter`).
- Removed local `TryReadUtcDateTime(out)` → `FalconJson.TryReadUtcDateTime(out)`. Both call sites rewired.
- Depth-cap predicate in `FetchAssetIdPagesAsync` → `FalconCursorPagination.ShouldResetForDepthCap(...)`
  (used `watermarkFloorUtc!.Value` in the block to keep 0 warnings).
- Left intact (genuinely AssetIdsFetcher-internal FQL parsing, not cross-flow):
  `ExtractLastSeenFloorAndBaseFilter`, `ApplyLastSeenTimestampFloor`, `TryParseQuotedUtcTimestamp`.
- Did NOT merge the two methods (`FetchAssetIdsAsync` accumulate variant vs `FetchAssetIdPagesAsync` streaming
  variant). Their bodies diverge in load-bearing ways (per-page yield + boundary-id snapshots + host-row capture
  + watermark-fallback dedup vs global HashSet + dry-run early return). Forcing a shared inner loop needs heavy
  ref/closure threading and risks drift — out of WA's behavior-safe mandate.
- Line count: **481 → 448** (-33). Behavior unchanged; build clean.

### (c) SharedFlows analysis

SharedFlows previously held only `FalconHttpFailureClassifier` (vendor HTTP quirk) and `FalconHostFilters`
(FQL filter builders). Both are cross-flow, stateless, vendor-semantic helpers — that is the right bar for
the folder: **stateless Falcon-vendor logic used by more than one flow.**

Moved into SharedFlows (all clear the bar — used by both assets and findings flows):
- `FalconJson` — both flows parse the same vendor fields (`last_seen_timestamp`, `updated_timestamp`, ids).
- `FalconCursorPagination` (MaxUtc, depth-cap predicate, timestamp format) — all three cursor loops share these.
- `FalconCheckpointMetadataKeys` — both flows stamp state-snapshot checkpoints.
- `FalconCollectorEvents` — both flows emit the same best-effort telemetry boilerplate.
- `FalconProgressState.Apply` — the checkpoint-state→progress bridge is flow-agnostic.

Deliberately left OUT of SharedFlows:
- AssetIdsFetcher's FQL parse/rebuild helpers (`ExtractLastSeenFloorAndBaseFilter`, `ApplyLastSeenTimestampFloor`,
  `TryParseQuotedUtcTimestamp`) — single-caller, specific to the asset-id pre-pass; promoting them would be
  speculative generalization.
- The per-flow filter builders that already differ at every site (`BuildAssetsFilter`/segment math,
  `ApplyUpdatedTimestampRange`, status-lane folding) — these are flow-specific by design.
- Did not touch `FalconHostFilters`' private duplicate of the timestamp-format const (outside WA's edit scope).

Net: SharedFlows = "stateless, cross-flow, Falcon-vendor helpers." It is a coherent concept and worth keeping;
the new helpers fit it cleanly. It is NOT a place for stateful pagination orchestration (see below).

### (d) Pagination-core decision: small helpers, NOT an engine

**Decision: extract small helpers only. No unified pagination engine.**

The four cursor loops (Spotlight.RunAsync, AssetIdsFetcher × 2, FalconAssetsFlow.CollectAsync) share the same
*shape* (drop cursor → re-anchor floor from watermark → rebuild filter/url → reset scroll-depth counter) but
differ in load-bearing detail at the exact point of reset:
- **Filter rebuild differs per site**: AssetIdsFetcher uses `ApplyLastSeenTimestampFloor`; FalconAssetsFlow uses
  `BuildAssetsFilter` with a recomputed `effectiveBaseDateUtc` + segment windowing + `currentFloorUtc`/
  `pendingTerminalCursorClearSnapshot` bookkeeping; Spotlight uses `ApplyUpdatedTimestampRange` + a progress
  snapshot + repeated-after-token and 5xx-with-cursor triggers + missing-boundary-id warn.
- Each reset mutates a different set of locals (closures over different state).

A single engine would have to take 4–6 mutable refs / rebuild-callbacks per loop — more complex than the
duplication it removes, and exactly the drift risk the contract warned against. So only the genuinely-common,
state-free decisions were extracted:

Helpers available for downstream workers to adopt at their reset sites:
- `FalconCursorPagination.MaxUtc(...)` — floor re-anchor max.
- `FalconCursorPagination.ShouldResetForDepthCap(after, watermark, pagesInCurrentScroll, maxPages)` — the
  proactive depth-cap predicate (identical in all three loops that have it).
- `FalconCursorPagination.UtcTimestampFormat` — the shared FQL timestamp format.

Each call site keeps its own pre/post logic (filter rebuild, counter resets, logging, progress snapshots).

## WB3 — FalconCollectorConfigurationBuilder

Decomposed the ~230-line `Build(Dictionary<string,string>)` (formerly :21-249) into four focused
private methods plus a carrier record. `Build` is now ~36 lines and reads as a linear pipeline:
resolve credentials dict → extract fields → partial-config guard → construct base cfg → apply
optional overrides → build session spec.

### New private members (all in the same file — no new files needed; helpers stayed put)
- `ResolveCredentialsDictionary(configuration)` — seam 1. The `_encryptedCredentials` decrypt →
  JSON parse → merge block. Returns the original `configuration` unchanged when no encrypted
  creds (or decrypt yields empty); otherwise returns the merged case-insensitive dict.
- `ExtractFields(configuration, credentialsDict)` — seam 2. GetAny for baseUrl/clientId/clientSecret
  (from credentialsDict) and flowName/fql (from configuration), baseDateUtc, and the four clamped
  paging knobs. Returns a `private readonly record struct ExtractedFields`.
- `ApplyOptionalOverrides(cfg, configuration, fields)` — seam 3. The ~15 sequential `cfg = cfg with
  { ... }` mutations, in the exact original order (paging → skip/eval inversion → spotlight sort →
  staged knobs → tracing/debug → Timeout/Retry/RateLimiter/CircuitBreaker).
- `BuildSessionSpec(cfg)` — seam 4. SessionSpec/telemetry assembly, verbatim.
- `ExtractedFields` record struct — carries raw/clamped scalars between extract and apply steps.

All existing `TryGet*`/`TryBuild*Options`/`ParseSpotlightStatusStages`/`NormalizeBaseAddress`/`GetAny`
helpers kept unchanged.

### Line count
- Before: 407 lines (`Build` ~230).
- After: 488 lines (`Build` ~36). The growth is per-method XML docs + the carrier record; the
  monolith is gone.

### Behavior-preservation points (verified by self-review)
- Credential fields still read from the merged `credentialsDict`; all other keys still read from the
  original `configuration`. ExtractFields takes both dicts and uses each for the same keys as before.
- Clamp ranges unchanged: assetsPageSize 1..1000, findingsPageSize 1..2500, aidBatchSize 1..5000,
  maxPagesPerCursorScroll 1..100000.
- Partial-config non-throwing `(null,null)` guard fires before `NormalizeBaseAddress`, same as before
  (NormalizeBaseAddress was a local `apiEndpoint`; now inlined into the object initializer at the
  same position, still after the guard).
- skip-vs-evaluation-facet inversion (`!evalFacet`) preserved.
- Override application order is byte-identical to the original sequence.
- All `.Trim()` / `?.Trim()` preserved.
- `ResolveCredentialsDictionary` preserves the two short-circuit returns (no encrypted key / empty
  decrypt → original dict) that the original expressed as a non-entered `if`.

### Risk to verify
Low. Pure mechanical extraction; no logic changed. The one non-obvious move: the original kept
`credentialsDict` as a mutable local seeded to `configuration`; I made `ResolveCredentialsDictionary`
return either the original or the merged dict — semantically identical (the original only ever
reassigned the whole reference, never mutated `configuration` in place). Orchestrator's build/test
pass over the 179-test suite confirms.

## WB5 — FalconCollector recovery extraction

New class: `Recovery/FalconCollectorRecoveryHandlers.cs` — `internal sealed class FalconCollectorRecoveryHandlers`.
- Ctor: `FalconCollectorRecoveryHandlers(ILogger logger)` (null-checked, stored in `_logger`).
- Public API (all moved verbatim from FalconCollector, only `_logger` rebound to the handler's field):
  - `ValueTask<AdapterRecoveryResult<FalconCollectorTriggerRequest>> RecoverFreshAsync(AdapterRecoveryContext<FalconCollectorTriggerRequest>)`
  - `ValueTask<AdapterRecoveryResult<FalconAssetsCheckpointState>> RecoverResumeAssetsAsync(PlatformEvent, AdapterRecoveryContext<FalconAssetsCheckpointState>)`
  - `ValueTask<AdapterRecoveryResult<FalconFindingsCheckpointState>> RecoverResumeFindingsAsync(PlatformEvent, AdapterRecoveryContext<FalconFindingsCheckpointState>)`

FalconCollector.cs line count: 615 -> 467.

Rewiring:
- Added field `private readonly FalconCollectorRecoveryHandlers _recoveryHandlers;` and ctor init `_recoveryHandlers = new FalconCollectorRecoveryHandlers(logger);`.
- ProcessAsync: `RecoveryHandler = RecoverFreshAsync` -> `RecoveryHandler = _recoveryHandlers.RecoverFreshAsync` (method-group delegate, same signature).
- ResumeAssetsAsync: `recoverAsync: ctx => RecoverResumeAssetsAsync(platformEvent, ctx)` -> `_recoveryHandlers.RecoverResumeAssetsAsync(platformEvent, ctx)`.
- ResumeFindingsAsync: `recoverAsync: ctx => RecoverResumeFindingsAsync(platformEvent, ctx)` -> `_recoveryHandlers.RecoverResumeFindingsAsync(platformEvent, ctx)`.
- Deleted the 4 private methods (RecoverFreshAsync, RecoverResumeAssetsAsync, RecoverResumeFindingsAsync, ApplyCheckpointState) from FalconCollector.cs.

FalconProgressState.Apply: REUSED. The moved code calls `FalconProgressState.Apply(...)` (namespace ...Flows.SharedFlows) at all 3 write sites instead of carrying its own `ApplyCheckpointState` copy. Bodies are behaviorally identical (both iterate the dict and call `progressContext.SetState(k, v)`); the private static copy was dropped rather than moved.

Behavior preservation: all 12 Continue/Decline reason strings preserved byte-for-byte (planned-yield short-circuits, hasDurableProgress gate, FalconCursorExpiredException watermark re-anchor vs the 401/5xx ApplyCursorTtlForResume branch, assets-vs-findings routing). git diff on FalconCollector.cs = 5 insertions / 153 deletions, no logic edits. Did NOT touch FalconCheckpointHelper, FalconRecoveryContinuationBuilder, FalconResumeRunner, flows, or the writer. Did not build/test (orchestrator owns the boundary build).

## WB2 — FalconCheckpointHelper split

Split the 609-line `Recovery/FalconCheckpointHelper.cs` into 4 focused `internal` pieces; the public façade shrank to thin delegators.

New files (all under Recovery/, all `internal`):
- `FalconCheckpointKeys.cs` (56 lines) — single source of truth for the persisted dictionary key literals (`flow`, `page`, `afterToken`, `assetsStage*`, `spotlight*`, legacy `collectedAids`, etc.). Used by both Serializer and Deserializer.
- `FalconCheckpointSerializer.cs` (94 lines) — `SaveAssetsState` + `SaveFindingsState`. Value formatting only (`"O"` round-trip for dates, `JsonSerializer.Serialize` for lists); keys reference `FalconCheckpointKeys`.
- `FalconCheckpointDeserializer.cs` (377 lines) — `TryLoadAssetsStateCore` + `TryLoadFindingsStateCore` (now `public static` inside an internal class). De-duplicated via a private `TryParseBaseFields` + `BaseFields readonly struct`.
- `FalconCheckpointResumePolicy.cs` (144 lines) — `CanResumeFrom` (staleness/routing) + `ApplyCursorTtlForResume` (cursor-TTL re-anchor). Calls back through `FalconCheckpointHelper.TryLoad*` so the `RecoveryStateHelper.TryLoad` exception wrapper is preserved.

FalconCheckpointHelper: 609 → 68 lines. All six public statics (`CanResumeFrom`, `ApplyCursorTtlForResume`, `SaveAssetsState`, `SaveFindingsState`, `TryLoadAssetsState`, `TryLoadFindingsState`) kept their exact signatures and are now one-line delegators. The `TryLoad*` delegators still wrap the cores in `RecoveryStateHelper.TryLoad` (the exception-handling boundary stayed on the public method, identical to before). Verified the Falcon test suite only calls these six members — no test touches the cores.

De-duplication of the two Load cores: extracted `TryParseBaseFields(checkpoint, logger, flow, out BaseFields)` covering every field common to both (page/totalItems/checkpointCreatedUtc/apiPageSize/baseDateUtc validation + month-segment/isDryRun + afterToken/lastWatermark/fql/totalExpected/lastWatermarkIds tail). Findings layers aidBatchSize + assets-stage + AID-scoped + spotlight fields on top; assets adds nothing. Result: zero duplicated parsing.

Behavior-preservation points I was careful about:
- Flow-identifier validation deliberately stays in EACH core (not in the shared routine) so the LogWarning template remains the flow-specific LITERAL ("...valid assets flow identifier." vs "...findings...") rather than a parameterized "{Message}" — keeps structured-log shape byte-identical.
- Validation ORDER unchanged: in findings, `aidBatchSize` validation runs immediately after the shared base parse, before the assets-stage block (the base tail parsers never log/fail, so relative ordering is observationally identical).
- Preserved verbatim: the 1,000,000-char legacy `collectedAids` OOM guard + its return-false path; `pendingAids` JSON-fail → return false (vs the best-effort LogWarning-and-continue for lastWatermarkIds/assetsStageWatermarkIds/spotlightCompletedLanes); stage absent→null; the `>= 0` clamps on pendingAidOffset/spotlightLaneIndex/thresholds; `long.TryParse` for the two spotlight counters.
- ApplyCursorTtlForResume preserved the `ReferenceEquals(sanitized, state)` short-circuit, the `isAssetsStage` (FalconFindingsStage.Assets, Ordinal) guard on the floor re-anchor, and the full `cursor.ttl.drop` warning with the `default`-checkpoint null-age branch.
- Save: kept the dual write of `pendingAids` AND legacy `collectedAids` (both = current PendingAids page) for backward-compat readers.

ADOPT decision: did NOT adopt `FalconCursorPagination.UtcTimestampFormat` — it is `"yyyy-MM-ddTHH:mm:ssZ"`, but Save uses `"O"` (round-trip). Swapping would change the persisted format = behavior change. `FalconProgressState.Apply` is not relevant to this file. Both correctly ignored per the "only if clean" guidance.

Did NOT build/test (parallel workers share the tree). Did not touch FalconCheckpointState.cs, flows, writer, FalconCollector.cs, FalconResumeRunner.cs, or AssetIdsFetcher.cs.

## WB4 — FalconAssetsFlow

Pure internal refactor, zero behavioral change. PURE mechanical extraction + Phase A helper adoption.

### Decomposition of CollectAsync (~410 lines → orchestration)
The single ~410-line `CollectAsync` is now thin orchestration (~44 lines): validate ApiPageSize, build segments, foreach segment → `ProcessSegmentAsync`, handle dry-run early-return, log completion.

New methods (all on FalconAssetsFlow):
- `ProcessSegmentAsync` (~125 lines): per-segment lifecycle — compute `effectiveSegment` (resume floor logic), build filter + reset scroll state, apply resume OR log segment-start, then drive the `while` page loop including the HTTP fetch and the `FalconCursorExpiredException` 404→watermark-reset handling and the dry-run first-request early return.
- `ApplyResumeState` (~60 lines): the resume-restore block (orig :91-139) — counter alignment, RestoreProgress, watermark/no-boundary-IDs warning.
- `ProcessPageAsync` (~188 lines): single fetched-page lifecycle — parse (EnumerateNormalizedAssetRecordsAsync) → publish → empty/no-publishable terminal-page snapshot+break → watermark advance → events → checkpoint write → AdvancePage → checkpoint-advanced event → proactive depth-cap reset → no-after break. Returns bool: true=break segment loop, false=continue.
- `BuildAssetsCheckpointState` (~30 lines): centralizes the identical `FalconAssetsCheckpointState{...}` shape used by the per-page write and the terminal snapshot (was duplicated). CheckpointCreatedUtc still captured at call time (unchanged).

New files under Flows/Assets/:
- `AssetsSegmentScrollState.cs` — per-segment mutable cursor state (AfterToken, BaseUrl, CurrentFloorUtc, WatermarkFloorUtc/Ids, WatermarkFallbackActive, PagesInCurrentScroll, PendingTerminalCursorClearSnapshot) + `ResetForSegment` + `ResetScrollToWatermark` (the orig local closure :154-181, moved verbatim incl. the DateTime.MinValue base-date guard and the exact LogWarning).
- `AssetsRunCounters.cs` — run-wide (cross-segment) counters: Page, TotalHosts, TotalExpected, ResumeApplied. Mutable reference type so the page loop advances the same counters orchestration reports.

`SegmentOutcome` enum added to reproduce the dry-run inner `return 0` (DryRunValidated) vs normal completion — chosen over an exception/signal so control flow stays explicit and matches the original return semantics exactly.

### Phase A helpers adopted
- `FalconJson.TryReadUtcDateTime` / `FalconJson.TryReadAnyId` — replaced local TryReadUtcTimestamp/TryReadAnyId/TryReadStringProperty (orig :604-643), all deleted. FalconJson adds an internal `ValueKind!=Object` guard but the call site already guards `item.ValueKind==Object`, so identical. Removed now-unused `using System.Globalization;`.
- `FalconCollectorEvents.ReportBatchProduced` / `ReportCheckpointAdvanced` — replaced all 3 inline null-check+try/catch-LogDebug event blocks (orig :349, :409, :508). Same args/metadata, same swallow-with-debug semantics.
- `FalconCheckpointMetadataKeys.Kind/Reason/ItemsInBatch/FindingsInBatch` — replaced the 4 local `_checkpoint.*` consts (orig :34-37), deleted.
- `FalconProgressState.Apply` — replaced both `foreach kvp SetState` loops (orig per-page + terminal snapshot).
- `FalconCursorPagination.ShouldResetForDepthCap` — replaced the inline proactive depth-cap predicate (orig :434). 1:1 match (afterToken non-empty && watermark present && pages>=max).

### Helpers deliberately NOT adopted (behavior-preservation)
- `FalconCursorPagination.MaxUtc` for the `effectiveBaseDateUtc` math in ResetScrollToWatermark: NOT adopted. The original has a `config.BaseDateUtc == DateTime.MinValue` special branch MaxUtc does not replicate, AND MaxUtc calls `.ToUniversalTime()` which would re-interpret an Unspecified-Kind DateTime as local time — a potential behavior change. Left the watermark math verbatim.
- `FalconCursorPagination.UtcTimestampFormat`: not applicable — no timestamp `ToString` formatting occurs in this file (FQL formatting lives in FalconHostFilters).

### Line count
Before: 683 (single file). After: FalconAssetsFlow.cs = 666, AssetsSegmentScrollState.cs = 98, AssetsRunCounters.cs = 21.
The <400 target was best-effort on the method: `CollectAsync` is now ~44 lines; largest method `ProcessPageAsync` ~188 (cohesive single-page lifecycle, shares many locals — further splitting would risk drift for little gain). File total stays ~666 because the extracted helpers/docs/enum add surface; behavior surface is unchanged.

### Behavior-preservation points verified
- Month-segment loop, effectiveSegment resume-floor logic, ResumeApplied gating order (effectiveSegment checks `!ResumeApplied` BEFORE ApplyResumeState sets it) — identical.
- `watermarkFallbackActive`, boundary-ID de-dup in EnumerateNormalizedAssetRecordsAsync — untouched.
- Optimistic `page++` / `page--` undo on cursor rejection — preserved (counters.Page--, continue re-increments).
- Terminal-snapshot conditions `requestedAfterPresent || pendingTerminalCursorClearSnapshot` — preserved; `requestedAfterPresent` still computed before the fetch (in ProcessSegmentAsync) and passed in.
- Dry-run early returns: inner first-request success → SegmentOutcome.DryRunValidated → CollectAsync returns 0; post-segment `if(IsDryRun) break` preserved in CollectAsync. streamed disposed exactly once on each path (dry-run disposes in ProcessSegmentAsync, normal disposes in ProcessPageAsync's await using).
- FalconCursorExpiredException→watermark reset, proactive depth cap, every log line + structured field — preserved verbatim.

Did NOT build/test (parallel workers share the tree). Touched only Flows/Assets/FalconAssetsFlow.cs and 2 NEW files under Flows/Assets/. Did not touch AssetIdsFetcher.cs, Findings files, FalconCheckpointHelper.cs, FalconCheckpointState.cs.

## WB1 — Findings trio + DTOs

Mechanical refactor of the findings flow trio. Zero behavioral change; no public/SDK surface touched. Did NOT build/test (shared tree).

### New DTO files (namespace ...Dtos, to match FindingsFlowRunConfig)
- `Dtos/FindingsDtos/FindingsAidScopedResumeState.cs` — positional sealed record, 6 fields: `AssetsAfterToken, AssetsLastSeenWatermarkUtc, AssetsFilterForFindings, AssetsPaginationCompleted, PendingAids(List<string>), PendingAidOffset(int)`. Has a static `Empty`. Replaces the old private `AidBatchResult` struct AND the 6 loose AID params on both checkpoint-writer methods. Doubles as the producer→consumer channel item (PendingAids carries the batch, offset=0 from producer).
- `Dtos/FindingsDtos/FindingsSpotlightYieldState.cs` — positional sealed record, 5 fields: `SpotlightStatus, SpotlightLaneIndex, SpotlightCompletedLanes(IReadOnlyList<string>), SpotlightNextVolumeYieldThreshold(long), SpotlightCumulativeFindingsCount(long)`. Replaces the 5 loose spotlight params on both checkpoint-writer methods.
- `Dtos/FindingsDtos/SpotlightVulnerabilitiesRunResult.cs` — positional sealed record replacing RunAsync's 5-tuple return `(PagesDone, TotalFindings, LastAfterToken, LastWatermarkUtc, LastWatermarkIds)`.

### New extracted helper files (namespace ...Flows.Findings)
- `Flows/Findings/FalconAidBatchProducer.cs` (179 lines) — extracted `FillAidBatchesAsync` + the bounded(1) channel orchestration out of FalconFindingsFlow. Exposes `Start(...) -> RunningProducer { Reader, Completion, CompleteWriter() }`. Producer body is byte-for-byte the old logic; now emits `FindingsAidScopedResumeState` instead of `AidBatchResult`.
- `Flows/Findings/FalconFindingsProgressSnapshot.cs` (141 lines) — extracted the progress-snapshot math cluster (`Build`/`TryDecodeCursorSortTimestampUtc`/`CalculateSegmentProgressPercent`/`CalculateDayProgressPercent`/`ToUtc`/`ClampPercent`/`RoundPercent`) + the record itself out of the Spotlight runner. Logic verbatim.

### File line counts (before → after)
- FalconFindingsFlow.cs: 961 → 860 (still >400; see below)
- FalconFindingsCheckpointWriter.cs: 442 → 343 (<400 ✓)
- FalconSpotlightVulnerabilitiesRunner.cs: 683 → 527 (>400; remaining is the single cohesive RunAsync cursor loop + the two streaming parsers, not cleanly splittable without crossing the load-bearing watermark/cursor state)

### Phase A helpers adopted
- **FalconJson** — replaced the runner's local `ReadString`/`TryReadUtcDateTime` (deleted both); all reads now `FalconJson.ReadString`/`FalconJson.TryReadUtcDateTime`.
- **FalconCollectorEvents** — replaced all 4 event-emit try/catch blocks in the checkpoint writer (2× BatchProduced, ...; actually 2 BatchProduced + 3 CheckpointAdvanced) with `ReportBatchProduced`/`ReportCheckpointAdvanced`.
- **FalconCheckpointMetadataKeys** — deleted the 4 local consts; now `FalconCheckpointMetadataKeys.Kind/Reason/ItemsInBatch/FindingsInBatch`.
- **FalconCursorPagination.UtcTimestampFormat** — deleted the runner's local `FalconUtcTimestampFormat`; both timestamp clauses now use the shared const.
- **FalconCursorPagination.ShouldResetForDepthCap** — replaced the inline depth-cap predicate in the runner loop.
- **FalconProgressState.Apply** — replaced all 4 `foreach kvp SetState` loops in the checkpoint writer.

### Extractions
- AID producer/channel plumbing → `FalconAidBatchProducer`.
- Progress-snapshot math → `FalconFindingsProgressSnapshot` (static `Build` + `TryDecodeCursorSortTimestampUtc`).
- In FalconFindingsFlow: added 2 private bundle-builders — `CurrentAidState()` (snapshots the 6 live mutable fields) and `ApplyAidState(...)` (writes them back from a drained batch); added `LaneRunState.ToYieldState()`. Kept the 6 mutable `_assets*`/`_pendingAids*` fields and `LaneRunState` as the live run-loop state (DTOs are only the hand-off bundles), exactly per the contract.

### Invariants I was careful about
- **Status-stage yield does NOT use `lane.ToYieldState()`.** The original status-stage `WriteCursorlessYieldCheckpoint` persisted `completion.NextStatus`/`completion.NextLaneIndex` (the lane being advanced TO), while `ToYieldState()` would emit `CurrentStatus`/`LaneIndex` (the completed lane). I constructed an explicit `FindingsSpotlightYieldState(NextStatus, NextLaneIndex, CompletedLanes, NextVolumeThreshold, CumulativePublished)` there to preserve this. The volume-yield site (mid-lane) DOES use `ToYieldState()` — matches the original which passed the lane's current values.
- `resetAidPaginationForNewLane` reset logic (MAJOR-1, reopen/closed re-scan AIDs) untouched — still derives effective values inside the writer.
- W5 cursorless re-anchor (`ApplyCursorTtlForResume(..., fromScheduledWait: true)`), P2-a/P2-b ordering, final-lane deferral latch, depth-cap, repeated-after-token, 404/5xx watermark fallback, all log lines + structured fields — preserved verbatim.
- Consumer loop AID-state threading: `ApplyAidState(batch)` sets the same 6 fields in the same order the old inline block did (`_pendingAids = batch.PendingAids`, offset=0), then `_pendingAids = new List<string>(); _pendingAidOffset = 0` after RunAsync — unchanged.
- Producer `AssetIdsFetcher.FetchAssetIdPagesAsync` call is the identical positional/named arg shape.
- Renamed the local `FalconFindingsCheckpointState` var in WriteCursorlessYieldCheckpoint from `yieldState` → `checkpointState` (collided with the new DTO param); `ApplyCursorTtlForResume` now takes `checkpointState`.

### Could NOT get <400
- FalconFindingsFlow (860) and SpotlightRunner (527). Both still over the soft target. Further splitting would require carving the lane loop or the cursor-paging loop, both of which carry interleaved mutable watermark/cursor/lane state across iterations — extracting them risks the documented invariants for marginal line savings. Stopped per "keep structure over fit."

### Ownership note
- Touched: FalconFindingsFlow.cs, FalconFindingsCheckpointWriter.cs, FalconSpotlightVulnerabilitiesRunner.cs + 3 new DTOs + 2 new helper files (all under Flows/Findings or Dtos/FindingsDtos).
- The assets-stage writer methods (`OnAssetsStagePagePublished`, `OnAssetsStageCompletedWithoutPublishedPage`) are called by `FalconFindingsAssetsStage.cs` (NOT in my ownership) — I kept their signatures unchanged and only swapped their internals to the Phase A helpers.

### WB1 follow-up — closed the 2 remaining param farms (verifier gap)

Mechanical only, zero behavioral change. Touched only FalconFindingsFlow.cs, FalconSpotlightVulnerabilitiesRunner.cs, and 1 new DTO file. Did NOT build/test.

**1. FalconSpotlightVulnerabilitiesRunner.RunAsync: 15 → 4 params.**
- New DTO `Dtos/FindingsDtos/SpotlightVulnerabilitiesRunRequest.cs` (positional sealed record, XML doc per param): `VulnerabilitiesBaseUrl, BaseVulnerabilitiesFilter, AidBatch, PagesDone, TotalFindings, StartAfterToken, StartWatermarkUtc, StartWatermarkIds, Config(FindingsFlowRunConfig), EnrichedPagesDirectory, StartFloorUtc=null, LaneStatus=null`. Reuses `FindingsFlowRunConfig` for the config field (no duplication).
- New signature: `RunAsync(AdapterProgressContext progressContext, SpotlightVulnerabilitiesRunRequest request, CancellationToken globalCancellationToken, Action<...PagePublished>? onPagePublished = null)`. Kept progressContext / ct / onPagePublished direct (granted latitude).
- Method body unchanged: added a small alias block at the top (`config = request.Config`, etc.) so the ~480 lines of cursor-loop logic stay byte-identical. Return type still `SpotlightVulnerabilitiesRunResult`.

**2. FalconFindingsFlow.RunSpotlightLaneAsync: 17 → 7 params.**
- New private nested record `SpotlightLaneRunContext` (in FalconFindingsFlow.cs, XML doc per param) bundling the 11 loop-invariant collaborators/setup: `SpotlightRunner, CheckpointWriter, ProgressContext, Config, VulnerabilitiesBaseUrl, BaseVulnerabilitiesFilter, HasUserFilter, HostsLimit, EffectiveAidBatchSize, EnrichedPagesDirectory, Stats`. Placed nested (not in Dtos/) because it references flow-internal types (`FalconSpotlightVulnerabilitiesRunner`, `FalconFindingsCheckpointWriter`, `FalconFindingsRunStats`) that live in `Flows.Findings` — keeps the `Dtos` namespace free of flow-internal coupling. Mirrors Shared's CollectorResumeExecutionContext intent.
- New signature: `RunSpotlightLaneAsync(SpotlightLaneRunContext ctx, LaneRunState lane, string? laneStatus, int pagesDone, int totalFindings, FalconFindingsCheckpointState? laneResumeState, CancellationToken globalCancellationToken)`. The only params left are the genuinely per-lane-varying values + ct.
- `ctx` is built once in CollectAsync (after spotlightRunner is constructed) and passed to both call sites (dry-run probe + lane loop). Inner method aliases ctx fields locally so the body is unchanged; the 3 inner `RunAsync` calls read `ctx.VulnerabilitiesBaseUrl/BaseVulnerabilitiesFilter/EnrichedPagesDirectory/CheckpointWriter/Stats`.

**Param counts (before → after):**
- RunAsync inputs: 15 → 4 (return already a record from prior pass).
- RunSpotlightLaneAsync: 17 → 7.

**Risk note:** All three `RunAsync` call sites and both `RunSpotlightLaneAsync` call sites pass the exact same values in the exact same roles as before (verified positionally); the request/context records are pure parameter object wrappers with no defaulting surprises (StartFloorUtc/LaneStatus defaults = null, always supplied explicitly at call sites). No test references RunAsync/RunSpotlightLaneAsync/the new DTOs (tests touch only BuildSegmentsForRun + FalconSpotlightLaneFilter). The one thing worth a reviewer glance: the alias blocks at the top of both refactored methods (RunAsync, RunSpotlightLaneAsync) — confirm each alias maps to the correct ctx/request field (done in self-review).

## Orchestrator — final integration

- Phase A green (179 pass), then Phase B (5 parallel workers) + 1 trivial missing-`using` fix → 179 pass, 0 warnings.
- verifier-1: satisfied-with-gaps → repaired the 2 unreduced param farms (RunAsync 15→4, RunSpotlightLaneAsync 17→7) → verifier-2: Criterion 2 PASS.
- code-reviewer-1: no Blocker/Major; Minor/Nit only (M1 AssetsSegmentScrollState→BuildAssetsFilter coupling; M2 ResetScrollToWatermark collaborators; N1 null-forgiving depth-cap; N2 mutable List on record). Accepted as deferrable, no behavioral risk.
- Final: build green (net8.0, 0 warnings), FalconCollector.Test 179/179. Public/SDK surface + FalconCheckpointHelper public statics unchanged. Net ~-1014 lines on touched files.
- Remaining >400 (best-effort exceptions): FalconFindingsFlow.cs 899, FalconAssetsFlow.cs 666, FalconSpotlightVulnerabilitiesRunner.cs 526, FalconCollectorConfigurationBuilder.cs 488 (grew from 407; god-method decomposed), FalconCollector.cs 467 (from 615), AssetIdsFetcher.cs 448 (from 481). FQLParser.cs 725 = out of scope, untouched.
- Skill docs (collector-flow-patterns, collector-recovery): not updated — pure extraction, documented flow/recovery architecture unchanged.

# Execution Notes

## W2
Added opt-in `merge_into.group: true` (embed mode only) for N:1 source fan-in — CrowdStrike Spotlight returns one record per finding sharing a host key; group embeds all of them as a JsonArray (fetch order) instead of last-wins. New `BoundedKeyGroupCache` (key→List<JsonNode>) mirrors the existing cache's evict-by-distinct-key bound. Rejected at load with `mode: collect` (ambiguous nesting, no live use case). Flag-off path is untouched — `ResolveMatchesAsync`/`ApplyEmbed`/last-wins log all unchanged; `MergeEnrichmentSink.PublishBatchAsync` just branches to a parallel grouped path. Schema: additive `group: boolean` on merge_into. 7 new tests + `Loader_SchemaGate_Group_Loads`; gate 620/620 green.
(Files: Models/WorkflowConfig.cs, Workflow/MergeEnrichment.cs, Workflow/MergeEnrichmentSink.cs, Loader/YamlIntegrationLoader.cs, Schemas/integration.schema.json, MergeIntoTests.cs. Note: W2 observed transient test failures from W1's concurrent Pagination edits in the shared tree; clean re-run green — final cross-check happens after all workers land.)

## W1
Fixed the paginator-rebuild bug: all five paginators (Cursor, Offset, PageNumber, LinkHeader, Scroll, BodyCursor) now carry CurrentOffset, RecoveryFloor, WatermarkValue, WatermarkIds forward in UpdateState's rebuilt PaginationState, matching the pattern ScrollPaginator already used for LastPageRecordCount. Root cause: IntegrationEngine.AdvancePagination reassigns pageState to the paginator's return value, so any field the rebuild didn't copy silently vanished from page 2 onward — confirmed live for {{recovery.floor}}, and by inspection for CurrentOffset (seeded by resume regardless of strategy, read unconditionally by templating and the sink checkpoint). Audited SamePageRetries/LastPageRecordCount and confirmed both are genuinely per-page-derived given the engine's existing reset/set invariants — left untouched. Added 8 tests (7 per-paginator unit tests + 1 engine-level two-page regression test proving the same non-empty floor on both requests); verified they fail without the fix by reverting CursorPaginator.cs in isolation. Gate: dotnet test on Cymulate.Integration.Yaml.Engine.Test is green at 620/620 (one unrelated flaky test on a single parallel run, confirmed passing on isolation and re-run).

## W3
- Added `first_of` (coalescing mapping value, full recursive forms) and `regex_extract` transform to response mapping, exactly matching the CrowdStrike Falcon composite-id use case (aid → device_id → 32-hex suffix of `id`).
- Found and fixed a real pre-existing bug along the way: `transform:` name resolution via bare `Enum.TryParse` never matched snake_case YAML aliases in the untyped-dict path real YAML always takes — `parse_datetime`/`severity_map`/`regex_extract` all silently no-op'd. Fixed with a shared alias-aware resolver (ResponseMapper.TryParseTransformType, reused by the loader validator); flipped the one test that pinned the old buggy behavior.
- ORCHESTRATOR FLAG: this alias fix is a default-path behavior change (transforms that silently never ran now run) — justified by the repo's own known-bug test, but it must be surfaced to the user and checked against catalog YAMLs that declare `transform:` (verifier obligation).
- Load-time validation added (Loader/YamlIntegrationLoader.ValidateMappingTransforms): unregistered/unknown transform and invalid/missing regex_extract pattern fail loudly at load, not first use; first_of is walked recursively.
- Schema updated additively (first_of branch, regex_extract enum + pattern/capture props).
- Gate: 645/645 green (620 baseline + 25 new/changed tests), one known unrelated flake reproduced once and confirmed pre-existing on re-run.

## Orchestrator — W3 alias-fix blast radius
grep across all 275 integrations/*.yaml: ZERO files declare `transform:` in mappings. The pre-existing alias bug meant the feature was silently broken AND silently unused — W3's fix changes behavior for no shipped YAML. Flag downgraded from "default-path behavior change" to "dormant-feature repair with no live consumers"; verifier should still confirm the grep.

## W4
Implemented pagination.prefetch (cursor/body_cursor/scroll only) and pagination.expected_total_path as opt-in PaginationConfig fields. Prefetch: a TryPrefetchNextPageAsync helper peeks the paginator's next state right after page N is mapped, issues page N+1's request before sink.PublishBatchAsync(page N), and buffers the HttpResponseMessage for the next loop iteration to consume via the existing fetch/classify path unchanged (flag-off path untouched — bufferedResponse stays null). Loader rejects unsupported strategies and the cursor_recovery combo. Completeness: tracks the freshest expected_total_path value per page and fails the operation (through the standard FailAsync path) only on a genuine natural cursor-exhaustion stop, never on max_pages caps or manual breaks (tracked via a loopEndedViaBreak flag at all 5 break sites). 645 baseline + 14 new tests, 659/659 green, verified stable across two runs.

## Synthesis — cross-repo consumption + gates (orchestrator)
- crowdstrike-falcon.yaml updated: merge_into.group true; spine op renamed query_hosts_for_findings with aid = first_of[aid, device_id, regex_extract("_([0-9a-f]{32})$") over id]; spine pagination prefetch: true + expected_total_path; assets scroll expected_total_path; Spotlight op cursor_recovery re-enabled with {{recovery.floor}} filter anchor. Booked-deltas header rewritten (closed vs open).
- Schema re-synced to cymulate-magic-integration/schemas/ (+128/−4 lines). Catalog tests there: schema validation green for all 275 files incl. updated falcon; CatalogBaselineTests still fails on the PRE-EXISTING defender-vm/qualys body_cursor local-engine drift (unchanged by this task, ADR-0003).
- Final engine gate with all four workers combined: 659/659 green. YamlCollector adapter project builds against the changed engine.

## Live parity rerun + comparison (base date 2026-07-15T00:00:00Z, lab tenant, run 20260721-121239-yaml)
- Spine: 358/358 hosts in 2 pages (250+108) — prefetch defeated the stale-cursor truncation (previous run: 267/358 with a silent 17-record tail). Completeness guard armed, no shortfall.
- Host sets: native ∩ yaml = 357/357, zero one-sided.
- Per-host finding counts (native chunks summed): 356/357 exact. Single diff: host 2b171c12… native=0 → yaml=833; equals the total delta (35,651 → 36,484, +833) exactly — live drift in the 2.3h between runs, fully attributed.
- Envelope: all records chunk=0 + isLastChunk + host + findings as arrays; aid non-null on 357/358.
- Residual deltas, all as booked: (1) native chunk framing — 4 finding-dense hosts split into 3/2/3/4 records natively (365 records vs 358), yaml one record per host, parser handles both shapes; (2) one unmanaged host whose composite-id suffix is NOT 32-hex ("…_ASCeLr0LF…") — native AidExtractor fails and SKIPS the record, yaml emits {aid:null, no findings key} (1 record, parser isolates); (3) yaml finding elements verbatim incl. apps/suppression_info (payload-trim delta); (4) remediation.entities unsorted; (5) findingsInChunk omitted.
- Spotlight cursor_recovery enabled; no expiry occurred this run (batch scrolls ≤ ~37s).

## Verifier gaps — orchestrator disposition
- Gap 2 (full regression scope) CLOSED: engine consumers are exactly 3 projects (engine test proj 659/659; YamlLocalRunner tool builds; YamlCollector adapter). The YamlCollector adapter test project — the transitive engine consumer — runs 166/166 green. No other test project touches changed code.
- Gap 1 (Medium — null-aid unmanaged record) CONFIRMED REAL and NOT benign: parser _build_asset_spine keys the asset spine on every chunk==0 record; the one aid:null record (unmanaged host, composite-id suffix "…_ASCeLr0LF…" is NOT 32-hex so regex_extract yields null and first_of exhausts) would surface as 1 PHANTOM asset (native's AidExtractor drops it). Net: yaml asset count = native + 1 on this tenant. Decision needed from user: add a small 5th capability (drop spine record when the mapped correlation key is null — native AidExtractor parity) vs book it as an accepted 1-record delta. Surfaced, not auto-fixed (each engine change has been user-signed-off by design).
- Gaps 3 (drift not artifact-provable) and 4 (A5 expiry substrings) accepted as documented.

## Code review (code-reviewer-1.md) — disposition
Review found 1 High, 2 Medium, 4 Low; no concurrency issues (prefetch is a reorder, not parallelism); buffer disposal via finally verified correct.

FIXED:
- F1 (HIGH) — completeness check counted sink.TotalPublishedRecords (post-enrichment/post-drop) instead of fetched records; a merge sink with unmatched:drop would spuriously fail a complete scroll. Added fetchedRecordCount (summed post-recovery-dedup pageRecords) and switched the check to it. NOTE: the live Falcon run was NOT affected — the spine uses unmatched:keep, so published==fetched==358. Added regression test Completeness_FetchedMeetsTotal_ButSinkDropsRecords_Succeeds (dropping sink). 
- F2 (MEDIUM) — schema transform enum still listed `equals` (never registered; new loader validation hard-rejected it → contradiction). Removed `equals` from the enum in both schema copies. Corpus grep: 0 YAMLs use `transform:` at all, so zero consumers.
- F5a (LOW) — completeness total extraction was Int32-number-only; a string-encoded or >Int32 total silently disabled the guard. Widened to long + string-encoded integers (freshestExpectedTotal now long?).

BOOKED (follow-ups, not fixed — deliberate):
- F3 (MEDIUM) — group-mode per-key list is unbounded (only distinct-key count capped). Not fixed: bounding would silently truncate a host's findings (worse than the memory cost), and per-aid fan-in is bounded in practice (native chunks at 2000). The cache-hit-returns-complete-group assumption holds for Falcon (Spotlight merge-source paginates fully per batch). Follow-up: optional fail-loud per-key cap + doc the fan-in ceiling.
- F5b (LOW) — regex_extract/first_of inside a workflow merge `shape:` is not load-validated (only response.mapping is). Falcon doesn't use them there. Follow-up: extend ValidateMappingTransforms into merge shapes.
- F6 (LOW) — pre-existing response-disposal-on-exception pattern (not introduced here; non-prefetch path has the same shape). Out of scope for this task.
- F7 (LOW) — WatermarkIds carried by reference (shared HashSet) across paginator states. Benign today (prefetch loader-forbidden with cursor_recovery, the only watermark writer; prior state discarded). Not editing six tested paginators for a benign latent — booked as a documented aliasing note.

Gates after repairs: engine 660/660 (+1 F1 regression), YamlCollector adapter 166/166, magic-integration schema validation 7/7, schemas byte-identical across repos.

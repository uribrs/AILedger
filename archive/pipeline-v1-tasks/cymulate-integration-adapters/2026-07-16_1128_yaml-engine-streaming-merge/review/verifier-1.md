# Verifier Report — yaml-engine-streaming-merge

**Verdict: PASS.** All eight Success Criteria are met, verified by reading the code and
running the builds/tests (not by trusting the notes). The full engine suite is green at
**556/556**, the engine and YamlCollector adapter both build clean, and every binding
constraint holds. Three LOW-severity gaps are noted below; none block acceptance.

Scope note: the branch is uncommitted and blends this task with its predecessor (v1
merge_into) and a sibling `body_cursor` stream, so `git diff` shows more than this task's
declared surface (BodyCursorPaginator, PaginationConfig/PaginatorFactory, ResponseMapper
self-envelope, JsonPathHelper). Those are engine-only, build clean, and are covered by the
green suite — not defects of this task.

## Per-criterion

| # | Criterion | Verdict | Evidence |
|---|-----------|---------|----------|
| 1 | Per-page enrich+publish while fetching; no held stream anywhere | PASS | `MergeEnrichmentSink.PublishBatchAsync` enriches each engine-delivered page then streams to the wrapped topic sink (`MergeEnrichmentSink.cs:53-94`). Runner wires it into the target's sink-ful page loop (`WorkflowRunner.cs:356-427`); `ExecuteStageAsync` passes the sink into `engine.ExecuteStageAsync` (`WorkflowRunner.cs:295-296`). `grep -niE "held\|replay\|heldByStage\|forcedFresh"` over the engine returns only legitimate 401-replay comments and "held in memory" — zero merge held-replay remnants. |
| 2 | Checkpoint carries target cursor; resumed paginated target re-enters AT cursor, boundary page not lost/dup'd | PASS | `WorkflowCheckpoint` gains `InTargetStage/TargetCursor/TargetOffset/TargetPage` (`WorkflowCheckpoint.cs:36-45`). Runner primes reserved `_resume_cursor/_resume_offset/_resume_page` (`WorkflowRunner.cs:383-393`), consumed by the engine (`IntegrationEngine.cs:241-258`). Ordering invariant confirmed: engine publishes the page (`IntegrationEngine.cs:651`), THEN `AdvancePagination` calls `UpdateState` (advance) then `SetPaginationState` with NEXT-page values (`IntegrationEngine.cs:1162-1168`) — snapshot always points at the next unit of work. `Resume_MidTarget_ContinuesFromCheckpointedPage_NoLoss_NoDuplication` (`MergeIntoTests.cs:575-602`) truly proves it: run-2 asserts `RequestedDetectionPages == [2]` (page 1 not re-fetched), a single `QID:2` record (page 1 not re-published), `PageNumbers == [2]` (numbering continues, no page-1 overwrite), and findings total `== 2` (1+1, no loss/no dup). |
| 3 | Non-paginated targets + fingerprint mismatch restart fresh; boundary flushes uncheckpointed counts | PASS (LOW gap) | `SetPaginationState` fires only from `AdvancePagination`, gated on `paginationConfig is not null` (`IntegrationEngine.cs:689`), so a non-paginated target emits no `InTargetStage` checkpoint — a resume can only land on a stage boundary, forcing a fresh restart. Documented in `WorkflowRunner.cs:40-41`. `TakeUncheckpointed()` flushes the single-page count at the boundary (`MergeEnrichmentSink.cs:43-48`, `WorkflowRunner.cs:423-426`). No *explicit* test asserts a non-paginated target produces zero mid-stage checkpoints (see Gaps). |
| 4 | Two merges stack in declaration order; later sees earlier enrichment | PASS | `plansByTarget` preserves declaration order (`WorkflowRunner.cs:76-85`); sink applies plans sequentially, feeding each plan the nodes mutated by the prior (`MergeEnrichmentSink.cs:68-80`). `Chaining_TwoMerges_DeclarationOrder_LaterSeesEarlierEnrichment` (`MergeIntoTests.cs:608-629`) proves it: merge 2 joins on `HOSTINFO.SITE_ID` — a field embedded by merge 1 — and asserts the lookup2 batch received `["10"]` (key sourced from merge 1's output). |
| 5 | collect on-as-list: dedupe by source key, first-appearance order, keep→empty array, drop→removed; single-string embed unchanged | PASS | `ApplyCollect` dedupes via `seen` (OrdinalIgnoreCase), first-appearance order, empty→drop when `unmatched:drop` (`MergeEnrichment.cs:115-132`). Tests: `Collect_MultiPathKeySet_DedupedArray_FirstAppearanceOrder` (a once, a<b<c), `Collect_Keep_WritesEmptyArrayWhenNoMatches` (`"defs":[]`), `Collect_Drop_RemovesRecordWithEmptyCollection` (r2 dropped). Embed regression coverage intact: HappyPath, ArrayAnchor, Unmatched keep/drop at record & element level, duplicate-last-wins, numeric canonicalization (both directions) — all single-string `on:`. |
| 6 | Loader lifts one-merge-per-target; still rejects the listed cases | PASS | `ValidateWorkflowMerges` (`YamlIntegrationLoader.cs:199-251`) + `TryParseJoin` (`WorkflowConfig.cs`). Reject tests all present and green: unknown target, later target, topicless target, merge-with-topic, malformed on, missing as, embed-with-list, mixed source keys, unknown mode, target-with-for_each, merge-with-poll. `Loader_MultipleMergesPerTarget_Accepted` confirms the bound is lifted. |
| 7 | Schema: minimal `mode` + `on` string-or-list; chaining+collect loads through validating loader | PASS (LOW gap) | Schema diff adds `merge_into` block with `mode` enum `[embed,collect]` and `on` `oneOf[string, array]` (`integration.schema.json`), targeted. `Loader_SchemaGate_ChainingAndCollect_Loads` loads a chaining+collect doc with `validateAgainstSchema: true`. Doc drift: `page_size` description still says "enriched and published per chunk (default 500)" though the field is inert (see Gaps). |
| 8 | Full suite green; engine + adapter build clean; assertions preserved not weakened | PASS | `dotnet test` → **556/556 passed, 0 failed**. Adapter build: succeeded, 0 warn/0 err. Engine recompiled fresh during the test run. Pre-existing `WorkflowRunnerTests.cs` is tracked and shows **no diff** — stream-through behavior byte-identical, assertions unmodified. |

## Constraints cross-check

- Stream-through byte-identity: `WorkflowRunnerTests.cs` tracked + unmodified (git diff empty). PASS.
- No vendor names in changed engine code: vendor mentions are all pre-existing example comments in unchanged idiom (Models/Auth/CursorRecoveryTracker); the new merge code (WorkflowRunner merge path, MergeEnrichment, MergeEnrichmentSink, MergeIntoConfig) is vendor-neutral. PASS.
- No yaml / csproj / Shared / adapter-surface changes: `git status` confined to engine `.cs` + engine test `.cs` + schema JSON. PASS.
- Grammar backward-compat: single-string `on:` = embed, proven by the embed regression tests. PASS.
- Cache never serialized: `Checkpoint_PerPublishedPage_CarriesPaginationPosition_NoCacheLeak` asserts serialized checkpoint contains no "cache"/"keys"/"title". Cache lives run-scoped on `MergePlan` (`WorkflowRunner.cs:74-85`), never on the checkpoint. PASS.
- page_size inert: declared at `WorkflowConfig.cs:118`, `grep` confirms zero read sites. PASS (behavior); doc drift in schema noted below.

## Gaps (all LOW)

1. **Schema page_size description misleads** (LOW). `integration.schema.json:507` advertises `page_size` as "Target records enriched and published per chunk (default 500)", but the field is inert (never read; the C# model doc at `WorkflowConfig.cs:113-117` correctly says so). Operator-facing drift only — no runtime effect. Suggest aligning the schema description with the "accepted for compat; inert" wording.
2. **Non-paginated restart-fresh not directly asserted** (LOW). SC3's "restart fresh" is architecturally guaranteed (no `SetPaginationState` without pagination) and indirectly covered (HappyPath totals, boundary-flush), but no test asserts a non-paginated target emits zero `InTargetStage` checkpoints. Mechanism is sound; coverage is implicit.
3. **Per-page enrichment-failure path untested** (LOW). decisions.md claims a source-fetch failure fails the page and leaves the checkpoint pointing at it for retry. The mechanism holds (a `WorkflowStageException` from `FetchSourceRecordsAsync` propagates before publish/advance, so no page-level checkpoint moves), but there is no explicit test. Not a listed Success Criterion.

## Commands run
- `dotnet build …Collectors.YamlCollector.csproj -f net8.0` → succeeded, 0/0.
- `dotnet test …Yaml.Engine.Test.csproj -f net8.0` → 556 passed, 0 failed, 0 skipped (489 ms).
- `git status --porcelain`, `git diff` (per file), `git ls-files`/`git diff` on WorkflowRunnerTests.
- `grep` sweeps for held/replay remnants, vendor names, PageSize read sites.

# Verifier Report 2 — Delta Re-verification (repairs since verifier-1 PASS)

## Verdict: PASS

All five repairs landed correctly, are tested, and pass. No Success Criterion from
verifier-1 regressed. Full engine suite 544/544 green (was 536); the 8 net-new delta
tests all pass; build clean on net8.0. Hygiene still holds: no vendor names, no yaml,
changes confined to `Cymulate.Integration.Yaml.Engine` + its test project.

---

## Per-Point Evidence

| # | Repair | Code | Test | Status |
|---|--------|------|------|--------|
| 1 | Gap tests from verifier-1 added | (n/a — tests) | `NumericKeyCanonicalization_TargetNumber_SourceString_Matches`, `NumericKeyCanonicalization_TargetString_SourceNumber_Matches`, `EmptyTargetStream_PublishesNothing_NoSourceInvocations`, `FollowUrl_TakesPrecedenceOverCursorField_WhenBothSet` — all assert real behavior (enriched field present / 0 lookup calls / follow_url wins) | PASS |
| 2 | merge workflows ignore resume, run fresh from stage 0; non-merge keep skip-completed | `WorkflowRunner.RunAsync` top: `if (resume is not null && stages.Any(s => s.MergeInto is not null)) { log; resume = null; }`. Class-doc updated to state the v1 exception. The skip-completed path below is otherwise unchanged | `Resume_MergeWorkflow_IgnoresCheckpoint_RunsFresh_PublishesAllEnriched` (checkpoint claims 2 stages done + findings=99 + page=5 → asserts 2 records, counter=2 not 101, page reset to 1). Non-merge behavior held by the **pre-existing, unmodified** `WorkflowRunnerTests.Resume_SkipsCompletedStages_AndContinuesFanOutPastPublishedChunks` (still green) | PASS |
| 3 | per-chunk matches in a non-evicting Dictionary; `BoundedKeyCache` cross-chunk only | `ProcessMergeChunkAsync` builds `chunkMatches` (plain `Dictionary`, unbounded); `FetchIntoCacheAsync` writes both `chunkMatches` and `cache`; `EnrichArrayAnchor`/`EnrichRecordAnchor` read only `matches` (chunkMatches), never the cache | `Cache_SmallerThanChunkDistinctKeys_AllKeysStillEmbed` (cache_size=1, 3 distinct keys in one chunk → all 3 embed). Cross-chunk reuse/eviction still covered by `Cache_SecondChunkSameKey_DoesNotReInvokeSource` + `Cache_SizeBound_EvictsOldest_ReFetches` | PASS |
| 4 | follow_url host-change logs a warning; logger flows factory→engine | `BodyCursorPaginator` ctor takes `ILogger?` (defaults `NullLogger`); `WarnOnHostChange` logs on host mismatch in `ApplyToRequest`; `PaginatorFactory.Create(strategy, ILogger? logger)` forwards to `new BodyCursorPaginator(logger)`; **engine call site `IntegrationEngine.cs:228` passes `_logger`** | `FollowUrl_HostChange_UpdatesRequest_AndLogsWarning`, `FollowUrl_SameHost_DoesNotWarn` | PASS |
| 5 | schema diff re-applied minimally | `git diff --numstat` = **30 added / 2 removed** (was an 867-line reformat); JSON parses valid. Contains: `body_cursor` in strategy enum + `cursor_json_path`/`cursor_regex`/`follow_url`; `mappingValue` self-envelope branch `{source:"$self", except:[]}`; `merge_into` stage block (target/on/as/unmatched/batch_size/page_size/cache_size, `required:[target,on,as]`, `additionalProperties:false`) — all match runtime aliases | Schema-validating loader tests green (e.g. `EngineXmlResponseTests` runs with `validateAgainstSchema:true`); full suite 544/544 | PASS |

## No Regression Check (verifier-1 criteria)

- Stream-through byte-identical: `DeliverStageRecordsAsync` still routes non-target stages through the unchanged `PublishStageRecordsAsync`; `GetOrCreateSinkAsync` extraction is behavior-preserving. Pre-existing WorkflowRunner/pagination/mapping tests unmodified and green.
- Resume-ignore is gated strictly on the presence of a `merge_into` stage — non-merge workflows are unaffected (confirmed by the pre-existing skip-completed test).
- Cache still never serialized into a checkpoint: `chunkMatches` and `cache` are method-local; `BuildCheckpoint` signature unchanged. `Checkpoint_InvokedPerPublishedChunk_SerializedStateHasNoCache` still green.
- All verifier-1 merge/body_cursor/$self/coercion/xml/loader tests still present and green.

## Residual Notes (non-blocking, informational)

- Numeric-key canonicalization is now tested both directions for **integer** values. A finer edge (e.g. float `1.0` vs string `"1"`) is still not asserted; negligible in practice and not a criterion.
- verifier-1 gaps #3 (source-op failure mid-chunk / no partial rollback) and #5 (scalar array-element can't embed) were not in scope for this repair round and remain as previously documented — neither blocks.

## What I Ran

- `git diff` on `WorkflowRunner.cs` (resume-ignore + chunkMatches), `PaginatorFactory.cs`, `BodyCursorPaginator.cs`, `integration.schema.json`.
- Read: `WorkflowRunner.RunAsync` head + `ProcessMergeChunkAsync`/`FetchIntoCacheAsync`/`Enrich*`; `BodyCursorPaginator` ctor + `WarnOnHostChange`; `IntegrationEngine.cs:228` call site; schema diff; the 5 new/changed delta test methods.
- `git diff --numstat` schema (30/2); `python3 json.load` validity check.
- `dotnet build` test project (net8.0) — clean (recreated the missing `artifacts/obj/ut/...` dir again — environmental).
- `dotnet test` targeted delta filter → 9/9 passed; full engine suite → 544/544 passed.
- Vendor-name grep over changed engine code + new paginator → clean; scope confined to engine + engine test project; no yaml.

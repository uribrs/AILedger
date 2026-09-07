# Verifier Report — YAML Engine Declarative Enrichment

## Verdict: PASS

Every Success Criterion is implemented, has covering tests, and the tests pass
(42 new tests green; full engine suite 536/536 green; engine + test projects build clean
on net8.0). All constraints hold: changes are confined to the engine and its test project —
no yaml files, no Shared/*, no adapter surface, no csproj, no vendor names in new code.

The gaps below are minor and non-blocking (untested edge cases in code that already handles
them, plus one latent nonsensical-usage limitation). None contradict a criterion.

---

## Per-Criterion Evidence

| # | Criterion | Evidence | Status |
|---|-----------|----------|--------|
| 1 | `merge_into` enriches target per-page: keys at `on:` (incl. `PATH[].KEY` array anchor), source op per uncached-key batch with `{{keys}}`, embed under `as:`, `unmatched: keep\|drop`, publish after enrich | `WorkflowRunner.RunMergeStageAsync`/`ProcessMergeChunkAsync` (chunk → extract keys → fetch uncached batches → embed → publish → checkpoint); `MergeIntoConfig.TryParseOn` parses `PATH[].KEY`. Tests: `HappyPath_RecordAnchor_FetchesPerBatch_EmbedsMatches_PublishesToFindings`, `ArrayAnchor_EmbedsMatchOnEachElement`, `Unmatched_{Keep,Drop}_{Record,Element}Level_*`, `BatchSize_SplitsKeysIntoMultipleInvocations_KeysResolvedPerBatch` | PASS |
| 2 | `{{keys}}` resolved | `FetchIntoCacheAsync` sets `sourceInputs["keys"]`; resolves via `TemplateEngine.TryAllScopes` → `context.Input` (bare-token fall-through, same path `{{item.*}}` uses). Verified in engine source + `BatchSize_*` test | PASS |
| 3 | `body_cursor` paginates via json_path/regex cursor as query-param or followed next-URL; stops on null/empty cursor or max_pages | `BodyCursorPaginator` (json_path preferred, regex group-1 fallback, follow_url replaces `RequestUri`). Tests: `JsonPath_*`, `Regex_UpdateState_ExtractsGroupOne_FromBodyText`, `JsonPath_TakesPrecedenceOverRegex`, `FollowUrl_ApplyToRequest_ReplacesRequestUriVerbatim`, `*MissingPath*Completes`, `HasMorePages_MaxPagesReached_ReturnsFalse` | PASS |
| 4 | Captures/poll/body_cursor see post-XML-conversion body | `IntegrationEngine.cs:618` reassigns `lastResponseBody` to converted JSON inside the XML branch (classification at :496 already ran on raw); paginators receive `responseDoc` parsed from converted body. Test: `XmlResponse_LastResponseBody_IsConvertedJson` | PASS |
| 5 | `$self` (with `except:`) emits record as nested object | `ResponseMapper.SelfAsObject` (string `$self`, dict `{source:$self, except:[]}`, typed transform). Tests: `Map_Self_EmitsWholeRecordAsObject_NestedArraysPreserved`, `Map_SelfWithExcept_DropsListedTopLevelKeys`, `Map_SelfWithExcept_FromYamlDict_DropsListedKeys` | PASS |
| 6 | Object at records_path consumed as 1-element array | `JsonPathHelper.ExtractArray` object→`new[]{target}`. Tests: `ExtractArray_ObjectAtPath_CoercedToSingleElement` (+ array/scalar/missing regression) | PASS |
| 7 | Loader/schema reject unknown/later/topicless target, unparseable `on:`, missing `as:` | `YamlIntegrationLoader.ValidateWorkflowMerges`. Tests: `Loader_{UnknownTarget,LaterTarget,TopiclessTarget,MergeStageWithTopic,BadOn,MissingAs,MultipleMergePerTarget}_Rejected` | PASS |
| 8 | All existing engine tests pass; new tests cover join (array anchor, unmatched, dupes, cache bound/eviction, batching), body_cursor (both sources, both modes, termination), $self/except, coercion, capture-post-conversion | 536/536; new-test map above + `DuplicateSourceKeys_LastWins`, `Cache_SecondChunkSameKey_DoesNotReInvokeSource`, `Cache_SizeBound_EvictsOldest_ReFetches`, `Checkpoint_InvokedPerPublishedChunk_SerializedStateHasNoCache` | PASS |
| 9 | `dotnet build` clean; engine test project green | Engine builds 0 warn/0 err; suite 536/536 | PASS |

## Constraint Checks

| Constraint | Result |
|-----------|--------|
| No vendor names / vendor-conditional logic in new engine code | PASS — grep of added lines and new `BodyCursorPaginator.cs` clean |
| New constructs in existing stage grammar, validated at load | PASS — `MergeIntoConfig`/`PaginationConfig`/`$self` on existing models; `ValidateWorkflowMerges` at load |
| Stream-through stages behaviorally unchanged (byte-identical) | PASS — `DeliverStageRecordsAsync` routes non-target stages through unchanged `PublishStageRecordsAsync`; no pre-existing test file modified (all new test files untracked); 536 pre-existing tests green. `PublishStreamAsync`/`EnumerateUtf8` path preserved |
| No changes to adapter surface, ISB host contract, Shared/*, S3 layout, done-event shape | PASS — all changes confined to `Cymulate.Integration.Yaml.Engine` + its test project |
| No yaml files touched | PASS — no `.yaml`/`.yml` in git status |
| No version bump (csproj) | PASS — no csproj changed |
| Merge per-page (no whole-run barrier) | PASS — `ProcessMergeChunkAsync` publishes + checkpoints per chunk |
| Cache bounded evict-oldest, never persisted | PASS — `BoundedKeyCache` FIFO; `BuildCheckpoint` takes no cache arg; `Checkpoint_...SerializedStateHasNoCache` test |
| Duplicate source keys last-wins, logged | PASS — `BoundedKeyCache.Add` overwrites in place + debug log; `DuplicateSourceKeys_LastWins` test |
| Join keys canonical case-insensitive strings | PASS — `CanonicalKey` + `OrdinalIgnoreCase` cache/dedupe |

## Schema ↔ Runtime Consistency

- `merge_into` schema keys (`target/on/as/unmatched/batch_size/page_size/cache_size`) match `MergeIntoConfig` YamlMember aliases exactly; `required: [target,on,as]`, `additionalProperties:false`. Consistent.
- `body_cursor` enum + `cursor_json_path/cursor_regex/follow_url` match `PaginationConfig` aliases. Consistent.
- `$self` dict form `{source:"$self", except:[...]}` accepted by schema `mappingValue` oneOf self-envelope branch AND by `ResponseMapper` (`YamlDictToTransformConfig` → `Source=="$self"`). The self-envelope branch does not collide with the `transform` branch (transform requires `transform` key; self-envelope forbids additionalProperties), so plain paths and existing transforms still validate — confirmed by 536 green.

## Documented v1 Deviations (recorded and reasonable)

- **One merge per target** — enforced (`Loader_MultipleMergePerTarget_Rejected`), documented in decisions.md/execution_notes.md. Reasonable v1 bound.
- **`{{keys}}` via input-scope binding** (not a new templating construct) — documented; verified to resolve through `TemplateEngine.TryAllScopes`. Reasonable; avoids new grammar.

## Gaps / Risks (all non-blocking)

1. **[low] Cross-type numeric key canonicalization untested.** `CanonicalKey` renders a number node via `ToJsonString()`/`GetRawText()`; a target key that is a JSON *number* (e.g. `123`) matched against a source key that is a *string* (`"123"`) relies on identical text. In the primary XML-vendor path every value is a string, so both sides align — but a JSON API returning numeric keys on one side and string on the other could miss a join. No test exercises the mixed-type case. Does not block; recommend a follow-up test if a numeric-keyed JSON vendor is onboarded.
2. **[low] Empty target stream untested.** A merge whose target produced zero held records has no test. Code handles it (empty chunk → no publish → completion checkpoint), but it is unverified.
3. **[low] Source operation failing mid-chunk untested / no partial-chunk rollback.** If the source op throws during one batch, the exception propagates and the stage fails; chunks published earlier remain. No criterion requires partial-chunk resilience; behavior is reasonable but undocumented and untested.
4. **[low] `follow_url` + `cursor_field` both set is deterministic but untested.** `ApplyToRequest` short-circuits on `FollowUrl`, ignoring `cursor_field`. Reasonable precedence; no test pins it.
5. **[info] Scalar array-element anchor (`PATH[]` with non-object elements) cannot embed.** `EnrichArrayAnchor` only writes `as` when the element `is JsonObject`; a scalar element's key is extracted/fetched but never embedded (a scalar cannot carry a field). This is nonsensical usage (enriching a scalar in place), not a criterion gap.

## What I Ran

- `git status` / `git diff` (all product files + new test/untracked files) — confirmed scope confinement.
- Read: contract, constraints, assumptions, decisions, orchestration_plan, execution_notes; `IntegrationEngine.cs` (lastResponseBody flow + template context), `BodyCursorPaginator.cs`, `WorkflowRunner.cs` (merge), `MergeIntoConfig`/`TryParseOn`, `ResponseMapper`/`JsonPathHelper`, `TemplateEngine` (bare-token scope order), `integration.schema.json` (merge_into / body_cursor / mappingValue).
- `dotnet build` engine project (net8.0) — clean.
- `dotnet build` engine test project — clean (after recreating a missing `artifacts/obj/ut/...` dir; environmental, not a code issue).
- `dotnet test` engine test project, filtered to the 5 new classes → 42/42 passed.
- `dotnet test` engine test project, full → 536/536 passed.
- Vendor-name grep over added lines and new files → clean.
- Confirmed no pre-existing test file modified (all new test files are untracked).

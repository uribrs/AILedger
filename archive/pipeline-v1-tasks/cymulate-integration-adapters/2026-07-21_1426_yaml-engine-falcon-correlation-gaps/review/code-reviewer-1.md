# Code Review — YAML engine (Falcon correlation gaps) working diff

Scope: uncommitted working-tree diff under
`src/Cymulate.Integration.Adapters/Collectors/YamlCollector/Cymulate.Integration.Yaml.Engine/`
plus its tests. Reviewed against the diff only; findings substantiated from the changed code.

Overall: the changes are careful, heavily commented, and the flag-off paths are genuinely unchanged.
Prefetch is implemented as a **reorder, not concurrency** (`await TryPrefetchNextPageAsync` completes
before the sink publish), so there is **no shared-mutable-state race** in the page loop or merge sink —
that concern is cleanly avoided. The findings below are led by one real correctness bug; the rest are
medium/low.

---

## F1 (HIGH) — Completeness check counts *published* records, not *fetched* records; a filtering sink makes a fully-complete scroll fail spuriously

`IntegrationEngine.cs:775`

```csharp
var collectedSoFar = sink?.TotalPublishedRecords ?? (isStreaming ? streamedCount : allRecords.Count);
if (collectedSoFar < expectedTotal) { /* fail the operation */ }
```

The two branches measure **different things**:
- no-sink path → `allRecords.Count` / `streamedCount` = records **fetched by pagination** (correct notion of "did the scroll account for the vendor total").
- sink path → `sink.TotalPublishedRecords`.

For `MergeEnrichmentSink`, `TotalPublishedRecords` is `_totalPublished`
(`MergeEnrichmentSink.cs:42`, incremented at `:118` from the **post-enrichment** `nodes` count). Embed/group/collect
with `unmatched: drop` removes anchors before publish, so **published < paginated** on a perfectly complete,
non-truncated scroll.

Failure scenario: primary stage paginates N records; vendor `expected_total_path` reports N; the stage's
sink is a merge enrichment with `unmatched: drop` that drops M unmatched anchors. Natural exhaustion →
`collectedSoFar = N-M < N` → the operation is **failed** and the sink is `FailAsync`'d, despite pagination
having been complete. This is precisely the Falcon/CrowdStrike-Discover correlation workflow this PR
targets (paginated scroll + slow merge-enrichment stage), so the false-positive is realistic, not
theoretical.

The completeness invariant is a **pagination** property and must be measured on the fetched side,
independent of downstream drop. Direction: track a fetched-record counter in the loop (sum of
`pageRecords.Count` across consumed pages, or `pageState`-derived) and compare *that* to
`expectedTotal`; do not use `sink.TotalPublishedRecords`. The divergence between the two branches is the
tell that "fetched" was the intent.

Not covered by tests — every completeness test uses `OrderCapturingSink` whose
`TotalPublishedRecords => PublishedIds.Count` is strictly 1:1 with fetched records
(`EnginePrefetchTests.cs:610`), so the dropping-sink case is never exercised.

---

## F2 (MEDIUM) — Schema still permits `transform: equals` but the new loader validation hard-rejects it

`Schemas/integration.schema.json:387` keeps `equals` in the transform enum, but `equals` is **not** in
`FieldTransformRegistry` (`FieldTransformRegistry.cs:14-18` — only SeverityMap, ParseDatetime, Template,
RegexExtract). The new `ValidateMappingTransforms` (`YamlIntegrationLoader.cs:598-604`) throws
"declared but not registered — it would silently no-op" for exactly that case.

Result: a YAML that is **schema-valid** (`equals` is in the enum) now **fails to load**. Any existing
vendor definition using `transform: equals` breaks at load. This is contradictory surface — fix by either
removing `equals` from the schema enum or registering an `EqualsTransform`.

Broader blast-radius note (same validation): previously an unresolved/typo'd transform name fell through
`Enum.TryParse` to `default(TransformType)` = `SeverityMap` (value 0, which *is* registered) and silently
ran severity-map. Those latent-typo configs now fail at load. Fail-fast is the right direction, but it is
a behavior change over the whole loaded corpus, worth calling out before rollout.

---

## F3 (MEDIUM) — Group-mode cross-page cache retains every source record per key for the whole run; per-key group is unbounded

`Workflow/MergeEnrichment.cs:355-385` (`BoundedKeyGroupCache`).

The FIFO bound caps the number of **distinct keys** (`cache_size`), but each key's `List<JsonNode>` grows
with no bound and is retained for the entire run (never evicted once created, per its own doc). A key with
large fan-in (e.g. one host key shared by hundreds of thousands of findings) accumulates an unbounded list
of `JsonNode` source records held run-long, in addition to the anchor nodes. For high-cardinality N:1
joins this is real memory pressure.

The doc comment acknowledges "assembled entirely from its own source fetch … a cache hit always returns
the complete group" — correct **only if** `fetchSource(batch)` returns *all* source records matching the
batch keys in a single call. If the source lookup is itself capped/paginated, a cached group is silently
incomplete. Worth confirming that assumption holds for the source operations that will use `group: true`.

Direction: at minimum document the fan-in ceiling; consider an optional per-key cap that fails loud rather
than growing without limit.

---

## F4 (LOW–MEDIUM) — Alias-resolution change (direct answer to the review question)

`ResponseMapper.cs:284-330` (`TryParseTransformType` / `BuildTransformAliasMap`) replaces
`Enum.TryParse<TransformType>(…, ignoreCase:true)`.

- The alias map contains **both** the `[YamlMember(Alias)]` value **and** the C# member name. So every
  name that parsed before (member names: `Template`, `SeverityMap`, …) still resolves — **no regression
  for previously-valid configs.**
- It is strictly *more* permissive: multi-word aliases (`severity_map`, `parse_datetime`, `regex_extract`)
  that silently failed `Enum.TryParse` and left `Transform` at its zero-value (`SeverityMap`) now resolve
  correctly. That is a **behavior fix**, but also a behavior *change* for any config that used an alias and
  was quietly mis-transforming — such configs now do what they say. Combined with F2's load validation,
  the previously-silent bad-name path is now loud. This is an improvement; flagging only so the corpus-wide
  effect is a conscious decision.

`ResponseMapperEdgeTests.cs:161` covers the alias-now-works case directly; good.

---

## F5 (LOW) — Completeness assertion silently disables itself for non-Int32 / string-typed totals; merge-shape transforms aren't load-validated

- `IntegrationEngine.cs:658-660`: `freshestExpectedTotal` is set only when the node is
  `JsonValueKind.Number` **and** `TryGetInt32` succeeds. A vendor that returns the total as a JSON string
  (`"total":"1234"`, common) or a value exceeding Int32 leaves the assertion silently off — the exact
  "return truncated result quietly" outcome the feature exists to prevent, with no signal. Consider
  accepting string-encoded integers / `long`.
- `ValidateMappingTransforms` walks only `operation.Response.Mapping` (`YamlIntegrationLoader.cs:557`). A
  `regex_extract`/`first_of` used inside a workflow merge `shape:` is not load-validated, so the
  "declared-but-unregistered would silently no-op" guarantee doesn't extend there. Inconsistent validation
  surface.

---

## F6 (LOW) — Buffered-response disposal on mid-consumption exception; minor waste on the consume path

- The outer `finally` disposes an un-consumed buffer on every early exit/return (`IntegrationEngine.cs:884`)
  — verified correct, including the completeness-failure `return`.
- But once the buffer is moved into the local `response` (`:398`) and `bufferedResponse` is nulled, an
  exception before the `response.Dispose()` at `:514` (e.g. `ReadAsStringAsync` throws) leaks `response` —
  the `finally` only disposes `bufferedResponse`. This is a **pre-existing** pattern (the non-prefetch fetch
  path has the same shape), not introduced here, so low priority — but prefetch adds a second producer of
  such responses.
- `else try { … }` without braces (`:401`) is valid but reads oddly. And when consuming a buffer, the loop
  still builds (`:377`) and auth-applies (`:388`) a `request` that is then discarded undisposed — negligible,
  noting for completeness.

---

## F7 (LOW) — `WatermarkIds` carried by reference across paginator states

All six paginators now copy `WatermarkIds = state.WatermarkIds` (e.g. `CursorPaginator.cs:72`) — the
**same `HashSet` reference**, not a copy, threads through successive states. Benign today: the prior state
is discarded, and prefetch is loader-forbidden with `cursor_recovery` (the only writer of watermark state).
But a shared mutable set aliased across "current" and "next" states is a latent footgun if future code
mutates it in place while both references are live. Consider copying, or documenting the aliasing intent.
`PaginationStateCarryTests.cs` verifies the values carry, not that they're isolated.

---

## Test quality

Strong: loader rejections (unsupported strategy, cursor_recovery combo, empty `first_of`, unknown/unregistered
transform, bad/missing regex pattern), `first_of` fallthrough + nesting + transform entries, regex null/no-match/
non-string/timeout-safe cases, pagination-state carry across all six paginators, group fan-in basics
(fetch-order array, from-subpath, zero-match unmatched fill), prefetch ordering (on vs off), max_pages cap,
buffered-non-success classification.

Gaps (map to findings above):
- **F1**: no completeness test uses a dropping/filtering sink — the one scenario that breaks is unexercised.
- **F6**: no test asserts a buffered response is disposed on early error/stop exit (no `HttpResponseMessage`
  dispose tracking anywhere).
- **F3**: group-mode cache-hit across *multiple anchor pages* sharing a key (the cross-page correctness claim)
  isn't exercised — group tests are single-page.
- Prefetch interacting with a buffered response that classifies as same-page-retry / defer isn't tested.

# Code review — YAML engine merge envelope parity (uncommitted working-tree diff)

**Reviewer:** code-reviewer-1 (independent senior-engineer pass)
**Repo:** `/Users/user/Dev/cymulate-integration-adapters` @ branch `falcon-strategy-parity`
**Scope:** uncommitted diff only (working tree vs HEAD). Six files:
- `Models/WorkflowConfig.cs`, `Workflow/MergeEnrichment.cs`, `Workflow/MergeEnrichmentSink.cs`,
  `Loader/YamlIntegrationLoader.cs`, `Schemas/integration.schema.json`,
  `UnitTests/.../Engine/MergeIntoTests.cs`
**Verification:** ran the merge suite — `dotnet test … --filter FullyQualifiedName~MergeIntoTests`
→ **101 passed, 0 failed** (net8.0). Findings below are substantiated from the diff and, where noted,
empirically confirmed by that run.

## Verdict

The four capabilities (`require_key`, `strip`, `sort`, `chunk`) are implemented soundly. The
high-risk items the review brief flagged — sort re-add / parent-ownership aliasing, chunk sibling
deep-clone, flag-off invariance — are **correctly handled**, not bugs. No High/Critical findings.
What remains is a small set of Low-severity behavioral notes (mostly "confirm this matches native
intent") and test-coverage gaps.

---

## Refuted concerns (verified NOT bugs) — the brief asked to scrutinize these

### R1. Sort `Clear()`/re-`Add()` of live nodes does NOT throw or alias
`MergeEnrichment.ApplySort` (MergeEnrichment.cs:214-226):
```
var ordered = array.OrderBy(e => e?.ToJsonString() ?? string.Empty, StringComparer.Ordinal).ToList();
array.Clear();
foreach (var element in ordered) array.Add(element);
```
- `ordered` is materialized with `.ToList()` **before** `Clear()`, so the sort key is captured while
  the elements are still live — correct ordering.
- `System.Text.Json.Nodes.JsonArray.Clear()` detaches every element (sets `Parent = null`), so the
  subsequent `Add` of the same references does **not** hit the "node already has a parent"
  `InvalidOperationException`. Confirmed empirically: `Sort_*` and
  `Sort_GroupMode_AppliedWithinEachMembersNestedArray` pass.
- No cache aliasing: `array` is resolved by `GetByPath` **inside `node`**, and `node` is itself a
  `DeepClone` of the cached match (EmbedValue, MergeEnrichment.cs:181-184). The live tree being
  mutated is the clone, never the shared cache node. Group mode clones per member via `EmbedGroup`.
- Determinism: `OrderBy` is a stable ordinal sort over the serialized string; the 3-run test asserts
  stability.

### R2. Chunk 1→N expansion does not share JsonNode parents across siblings
`ChunkFrameOnePlan` / `RebuildChunkRecord` (MergeEnrichmentSink.cs:157-231):
- Slice elements are `elements[…]?.DeepClone()` (lines 180, 189) — each slice owns detached copies;
  the source `array` is never re-parented.
- Every carried-over field is `value?.DeepClone()` (line 226); the leading key is cloned (line 215).
  Sibling chunk records therefore share nothing. The source `obj` is discarded (never added to
  `result`). Confirmed: `Chunk_HostFieldsRepeatVerbatim_*` passes.

### R3. Off-by-one / terminal framing is correct
`fullChunks = n / cap`, one always-emitted terminal with `remainder = n - fullChunks*cap`
(MergeEnrichmentSink.cs:172-190) ⇒ `floor(n/cap)+1` records for all n≥0, empty terminal on exact
multiples and on n=0. Confirmed by `Chunk_NEqualsCapExactly_TrailingTerminalIsEmpty`,
`Chunk_NEqualsTwiceCap_*`, `Chunk_ZeroFindingsTarget_OneEmptyTerminalRecord`,
`Chunk_NBetweenMultiplesOfCap_*`.

### R4. Strip after clone is safe
`EmbedValue` strips via `obj.Remove` on the cloned `node` only (MergeEnrichment.cs:186-190); non-object
payload (scalar from `from:`) and missing keys are no-ops. Confirmed by `Strip_NonObjectEmbeddedElement_*`,
`Strip_MissingKey_*`, `Strip_ComposesWithFrom_*`.

### R5. Flag-off invariance holds for all four
- `require_key`: guard is `if (plan.RequireKey && …)` — short-circuits to the pre-existing path when
  false (MergeEnrichment.cs:131-132, 238-239). No new allocation.
- `strip`/`sort`: `EmbedValue` only branches when `plan.Strip is { Count: > 0 }` / `plan.Sort is { }`;
  otherwise the original `DeepClone` result is returned unchanged.
- `chunk`: `ApplyChunkFraming` returns the **same list reference** when no plan declares chunk
  (MergeEnrichmentSink.cs:137-146, 115). Confirmed by the `*_FlagOff_*` pin tests.

### R6. `require_key` × array-anchor is closed at load, so the runtime guard is only ever hit on
record-level anchors. Loader rejects `require_key` when `join.Anchors.Any(a => a.IsArrayAnchor)`
(YamlIntegrationLoader.cs). `ApplyEmbed`/`ApplyGrouped` read `Anchors[0]` (record-level under that
constraint). Consistent. Confirmed by `Loader_RequireKey_ArrayAnchor_Rejected`.

---

## Findings (most severe first)

### F1 — LOW — Chunk expansion changes `counts: <name>: records` semantics and counts empty terminal records
**Files:** `MergeEnrichmentSink.cs:115,127` (chunked `nodes` passed to `onPublished`) →
`WorkflowRunner.cs:644-655` → `ApplyCounts` (`WorkflowRunner.cs:255-289`).

**Scenario:** With `chunk` on, `PublishBatchAsync` expands the node list 1→N *before* both the publish
count and the `onPublished(published, nodes)` callback. `ApplyCounts`:
- for a `records` count spec, `delta = published` — now the **chunk-record count**, not the logical
  target-record count. A cap=2 host with 4 findings contributes 3 (two full + empty terminal) to the
  counter; a **zero-finding host still contributes 1** via its empty terminal record.
- for an array-path spec (`findings[]`) the per-record sums add back to `n` across chunks, so those
  counters are **unaffected** (verified by hand: `fullChunks*cap + remainder == n`).

`PublishedByTopic` / `Counters[topic]` likewise now reflect chunk records.

**Why it's Low, not a bug:** chunk records are the actual units published to S3, so counting them is
self-consistent and probably matches native intent. But it is a semantic shift for any `records`-spec
counter and it is **completely untested** (no test wires `counts` together with `chunk`).

**Direction:** confirm against native FalconCollector what the findings/records counters are supposed
to represent (emitted records vs. logical hosts vs. findings), then add one test that pins it.
`CollectFromRecords` is unaffected — it dedups via `seen.Add`, so repeated `aid` across chunks
collapses to one value (WorkflowRunner.cs:306-329).

### F2 — LOW — `RebuildChunkRecord` preserves only the single FIRST source key ahead of the stamps
**File:** `MergeEnrichmentSink.cs:206-231`.

**Scenario:** Key order is rebuilt as `{firstKey?, index, last, count, …rest, array}`. This bakes in the
native `{aid, chunk, isLastChunk, findingsInChunk, host, findings}` shape, which has exactly **one**
leading identity key. A spine whose first two keys are both logically "leading" (e.g.
`{aid, region, findings}`) emits `{aid, chunk, isLastChunk, findingsInChunk, region, findings}` —
`region` is demoted below the stamped fields. If a future native shape expects
`{aid, region, chunk, …}` this diverges silently.

**Why Low:** correct for the one native shape it targets; only tested with a single leading key
(`Chunk_EmittedKeyOrder_MatchesNative`). Flag as a documented fragility, not a defect.

**Direction:** leave as-is if Falcon is the only consumer, but note the single-leading-key assumption in
the `ChunkConfig` doc-comment so the next vendor author isn't surprised.

### F3 — LOW — Stacked chunk plans re-chunk and re-stamp with shared default field names
**File:** `MergeEnrichmentSink.cs:137-146`.

**Scenario:** `ApplyChunkFraming` folds each chunk-configured plan over the **growing** list. Two plans
on one target both using the default field names would have plan 2 re-chunk plan 1's output and
**overwrite** `chunk`/`isLastChunk`/`findingsInChunk`. The doc-comment calls this "declaration order,
each on its own array field," but there is no guardrail preventing two chunk plans from colliding on
the stamp names, and no test.

**Why Low:** multi-chunk-plan on one target is an unusual/nonsensical config; single-plan is the only
exercised and realistic path.

**Direction:** optionally reject >1 chunk-configured plan per target at load, or leave it as a
documented footgun. No code change strictly required.

---

## Test-coverage gaps (no code defect implied)

1. **Chunk passthrough branch untested** — `MergeEnrichmentSink.cs:164-170` (field missing / not a
   JsonArray → node passes through unchanged) has no test. The comment asserts "for Falcon this never
   happens," but the branch is reachable for any non-Falcon YAML and should be pinned.
2. **`counts` × `chunk` untested** — see F1.
3. **`chunk.array` ≠ `as` untested** — every chunk test uses the default (`as` field); the explicit
   `array:` override path (WorkflowConfig resolution in `MergePlan.Create`) is only covered by the
   loader/schema-gate tests, not runtime.
4. **`strip` colliding with a stamped chunk field / whole-record embed edge** — minor; the
   compose-all-features test (`Chunk_ComposesWith…`) covers the common path.

Coverage of the genuinely risky boundaries the brief called out — exact-cap off-by-one, zero-finding,
key order, sort aliasing across group members, strip on non-object, require_key on array anchor — is
**present and passing**. This is a well-tested change.

---

## Concurrency / resource / idiom notes
- No shared mutable state introduced; the sink is per-target and the plan objects are immutable records.
  `ApplyChunkFraming` allocates a fresh list only when a chunk plan is present (flag-off keeps the same
  reference). Idiomatic STJ throughout.
- `array.ToList()` (MergeEnrichmentSink.cs:172) materializes the element list once per node — fine; the
  per-slice `DeepClone` is the unavoidable cost of independent sibling records.

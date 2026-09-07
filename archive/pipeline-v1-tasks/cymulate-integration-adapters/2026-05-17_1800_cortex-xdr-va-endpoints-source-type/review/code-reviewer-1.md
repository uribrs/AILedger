# Code Reviewer — Cortex XDR `va_endpoints` source-type findings flow

Independent review of the listed artifacts. Risk = Medium-High (hot path over up to 100k rows per `CollectAsync`; persisted checkpoint v3; new HTTP probe). Tech = C# / .NET 8 with `System.Text.Json` streaming.

Calibration: feature/test code in a collector. Proportional scrutiny on (1) hot-path JSON rewrite, (2) checkpoint persistence boundary, (3) per-stage resume semantics.

---

## Blocker

None.

---

## Major

### M1 — `StampObject` performs a redundant full property enumeration on every row

**Problem.** `CortexXdrRecordFormatter.StampObject` enumerates the row's properties twice on every successful row:

1. lines 55–62: a full collision scan that never short-circuits on the "no collision" path.
2. lines 69–72: a second enumeration to copy properties to the writer.

For the established hot path (≤ 100k rows per `CollectAsync` — 50k va_cves + 50k va_endpoints), every row pays for two scans over its property list (~12 properties for va_cves, ~5 for va_endpoints) before it can be flushed. `JsonElement.EnumerateObject` is cheap per item but the doubling is unconditional.

**Impact.** Wasted CPU on the documented hot path. Order-of-magnitude estimate: ~1M extra property visits per `CollectAsync` for va_cves alone. Not a correctness defect; not a memory defect; a steady, easily eliminated tax.

**Recommended fix (local).** Replace the explicit collision loop with `element.TryGetProperty(SourceTypePropertyName, out _)`, which short-circuits at the matching property and falls through cheaply when none is present. The diagnostic message is unchanged; only the redundant loop disappears.

```csharp
if (element.TryGetProperty(SourceTypePropertyName, out _))
{
    throw new InvalidOperationException(
        $"Cortex XDR record already contains a '{SourceTypePropertyName}' property; refusing to overwrite with sourceType='{sourceType}'. Vendor schema may have changed.");
}
```

**Refactor or local.** Local. One-call replacement; no shape change.

---

## Minor

### m1 — `nextVaEndpointIndex` typed `long`, `nextCveIndex` typed `int` despite identical 50k bound

**Problem.** `CortexXdrFindingsCheckpointState.NextCveIndex` is `int` and `NextVaEndpointIndex` is `long`. Both are bounded above by `MaxXqlFindings = 50_000` (the XQL `| limit 50000`). The asymmetry is visible in `CortexXdrCheckpointHelper.TryLoadFindingsStateCore` (separate `TryGetInt` / `TryGetLong` helpers) and propagates into the flow's signatures.

**Impact.** Cosmetic inconsistency; no overflow risk in either direction. Slightly raises the reader's question: "why is one a long?" The local `TryGetLong` helper exists only to support this single field.

**Recommended fix (local).** Make both `int` (matching `MaxXqlFindings`'s effective range and the int-typed cve index), and drop the local `TryGetLong` helper. Otherwise document the asymmetry in `CortexXdrFindingsCheckpointState` so a future maintainer doesn't widen `NextCveIndex` to match by mistake.

**Refactor or local.** Local.

### m2 — `StampStream` re-checks `cancellationToken.ThrowIfCancellationRequested()` after `WithCancellation` already plumbed it

**Problem.** Lines 40–42:

```csharp
await foreach (var row in source.WithCancellation(cancellationToken).ConfigureAwait(false))
{
    cancellationToken.ThrowIfCancellationRequested();
    yield return ...
}
```

`IngressStream.Skip` already does this same double-check with an explanatory comment about `WithCancellation` not propagating into sources that don't honor `[EnumeratorCancellation]`. Here, both `IngressStream.Skip` and `CortexXdrXqlClient.ExecuteAsync` *do* honor the attribute, so the redundant check is purely defensive.

**Impact.** Tiny CPU cost per row in the hot path; mild noise. Acceptable as defense-in-depth.

**Recommended fix (local).** Either keep it and add a one-line comment mirroring `IngressStream.Skip`'s rationale, or drop it. Either is fine. Calling it out so the next reader doesn't wonder.

**Refactor or local.** Local (optional).

### m3 — `CortexXdrFindingsFlow.PublishXqlStageAsync` and `PublishXqlEndpointsStageAsync` are near-duplicates

**Problem.** The two methods differ in: (a) which cve/endpoint index they advance, (b) the `nextStage` selection for the trailing checkpoint, and (c) which return tuple shape they use. Otherwise the page-buffering, drift check, checkpoint write, and event emission are identical. ~100 lines of near-clone.

**Impact.** Two surfaces to keep in sync. Today they already drift on detail: the endpoints variant returns `publishedRows` while the cves variant returns the new `nextCveIndex` (the *cumulative* value), forcing the caller to do `nextVaEndpointIndex += publishedVaEndpoints` (line 185) — a difference that a future reader has to discover by diff.

**Recommended fix.** Either unify the two via a small shared inner helper that takes a delegate for the per-page advance (`Action<int> onPagePublished`) and a `nextStageOnDone` value, or normalize the return contract (both return the new cumulative cursor; caller assigns).

**Refactor or local.** Local refactor; ~40 lines. Recommended now, not blocking — the second stage was added precisely because the pattern is reused, and a third (e.g. another XQL dataset) is plausible. Solves real coupling.

### m4 — `SelectCursor` and `IsValidFindingsStage` use `OrdinalIgnoreCase` for values you control

**Problem.** Stage constants are produced and consumed only via the `CortexXdrFindingsStage.*` literals. There is no external input path. Both comparisons use `OrdinalIgnoreCase` (e.g. `CortexXdrCheckpointHelper.IsValidFindingsStage` lines 259–261, `SelectCursor` line 660 and 665, `CollectAsync` lines 120, 150).

**Impact.** Cosmetic; ordinal-ignore-case is a small CPU tax and signals "user-input-style comparison" where there is none. Resume from a hand-edited checkpoint with a wrong-cased stage would silently succeed today, which is mildly surprising for a typed enum-like constant.

**Recommended fix.** Use `StringComparison.Ordinal` throughout, or promote `Stage` to an `enum` and parse it at the boundary. The enum form would also force a single `switch` in `SelectCursor`, eliminating the m3 concern about stage knowledge leaking across modules.

**Refactor or local.** Local for the comparison change; small refactor for the enum form. Defer the enum unless m3 is acted on.

---

## Nit

### n1 — `CortexXdrXqlClient` is allocated twice in `CollectAsync` (lines 122 and 152)

Each instantiation is cheap (three field assignments), but a single `_xqlClient = new CortexXdrXqlClient(_http, _logger, _xqlPollDelayOverride)` in the constructor would be clearer and consistent with the publisher fields. Defer if the team prefers per-stage instantiation as a "fresh state" signal.

### n2 — `StampFromBytes` does not pass `JsonDocumentOptions` to bound trailing commas / comments

`JsonDocument.Parse(row)` accepts vendor-permissive defaults. Cortex's XQL stream emits strict JSON, so this is fine, but a `JsonDocumentOptions { CommentHandling = Disallow, AllowTrailingCommas = false }` literal (matching the documented expectation) would make the strict-JSON contract self-evident. Pure documentation.

### n3 — `XqlStageBufferedPage` capacity hint is over-allocated when `_configuration.PageSize` is large

`new List<ReadOnlyMemory<byte>>(_configuration.PageSize)` allocates an array sized to `PageSize`. With a typical 100, fine. For an operator-set page size in the thousands the allocation is fine but wasted when the upstream stream returns < PageSize rows. Not worth changing.

### n4 — Tests construct `Mock<ILogger>` with `Mock.Of<ILogger>()` everywhere

Consistent with the project, but flagging that none of the new tests assert *what* was logged. The fail-fast collision path (`InvalidOperationException` containing `sourceType`) is asserted via the exception message — good — but the "va_endpoints unavailable" branch (lines 159–161 in the flow) is not test-asserted for the warning. That branch is only behavior-asserted via "no second XQL pair issued" (`CollectAsync_WhenVaEndpointsDatasetExplicitlyEmpty_SkipsStage_AndContinuesToAssets`). Adequate; not worth adding a log-content test.

### n5 — `BuildSession`'s `lastStartBody` dispatch heuristic is fragile under test evolution

`CortexXdrFindingsFlowTests.BuildSession` dispatches the `get_query_results` response based on whether the *most recent* `start_xql_query` body contained the substring `"va_endpoints"`. If a future test introduces a query that also mentions `va_endpoints` in a comment, this will silently route to the wrong reply. Self-contained nit — test-only.

---

## Observation

### O1 — `StampObject`'s `JsonDocument.Parse` per row is the dominant CPU cost in the streaming hot path

For 100k rows the formatter incurs 100k `JsonDocument.Parse` + 100k `Utf8JsonWriter` write cycles. `JsonDocument` uses pooled buffers so allocations are mostly amortized, but parse is non-trivial CPU. A direct byte-level prepend (`{"sourceType":"<value>",` + drop the leading `{` of the row + concat) would be 3–5× faster and removes the parse cost entirely.

The current implementation is correct and provides the collision-detect that the comment block on the class deliberately calls out. **Do not change without a measured need** — the current correctness vs. the byte-prepend's edge cases (sourceType-as-substring, escaping, empty-object input, leading whitespace) is the right tradeoff today.

### O2 — `OwnedJsonElement` lifetime in `EnumerateEndpointAssetsAsync` is correct but easy to break in a future edit

Line 564–581: `await foreach (var owned in reader...)` then `using (owned)` and a call to `CortexXdrRecordFormatter.StampFromElement(endpoint, ...)` that returns a fresh `byte[]`. The `byte[]` is owned by the caller (no references into `endpoint`), so `using (owned)` correctly disposes the backing `JsonDocument` after `yield return`. A future edit that hands the `JsonElement` into the publisher pipeline directly would dangle. Not actionable today; flagging the constraint for posterity.

### O3 — `CortexXdrFindingsFlow.SaveCheckpointState` always writes `hasMorePages: true` while a stage is mid-flight

Lines 298, 396: any in-progress page save writes `hasMorePages: true`. The only `hasMorePages: false` write happens for the terminal assets-page state and for `EndpointsSeen == 0` (line 457). This is consistent with `SelectCursor`'s null-cursor-on-done contract and matches how `progressContext.SetCursor` is consumed elsewhere. Worth noting because an external observer of the checkpoint state may interpret `hasMorePages: true && stage == "assets"` differently than `hasMorePages: true && stage == "findingsEndpoints"`. The semantics are tracked in tests (`CollectAsync_StagedCheckpoints_ReportCveTransitionAndFinalAssetState`); nothing to change.

### O4 — `CortexXdrXqlClient.StartQueryAsync` numeric-reply path round-trips queryId as a JSON string

Lines 167–172: if Cortex returns a numeric `reply`, `GetRawText()` returns the literal number (`"12345"`). The next poll's body uses `JsonSerializer.Serialize(queryId)`, which turns that into `"\"12345\""` — sending the number as a JSON string. If Cortex's XQL API ever expects the same-typed queryId in the poll body, this would 4xx. **Pre-existing behavior, unchanged by this PR.** Flagging only because the file is in scope.

### O5 — `findingsPage` is shared across va_cves and va_endpoints output files

Both `PublishXqlStageAsync` (va_cves) and `PublishXqlEndpointsStageAsync` (va_endpoints) call `_findingsPagePublisher.PublishPageAsync(..., currentFindingsPage, ...)` with a single monotonically incremented `findingsPage` counter. The published files therefore land as `findings_000001.json` (va_cves), `findings_000002.json` (va_endpoints), distinguished only by the `sourceType` stamped on each row. This is verified by the tests (`CollectAsync_WhenVaEndpointsDatasetAvailable_PublishesFindingsEndpointsBeforeAssets`). **Working as designed.**

### O6 — Test `CapturingAdapterDataPublisher.PublishStreamAsync` derives `RecordCount` by counting `\n` bytes

`CortexXdrFindingsFlowTests.cs` line 1257: `bytes.Count(b => b == (byte)'\n')`. The flow's drift check (`publishResult.RecordCount != page.Count`) does not flow through this code path — the active path in the tests is `IAdapterExecutionContext.PublishAsync` (line 1157), which echoes the caller's `request.RecordCount` directly. The `CapturingAdapterDataPublisher` class is therefore unreachable in the tests as written. Either delete it or use it from a focused test — currently dead test scaffolding.

### O7 — `CortexXdrCheckpointHelper.CanResumeFrom_WithLegacyJoinedFindingsCheckpoint_ReturnsFalse` and `CanResumeFrom_WithCheckpointVersion2_ReturnsFalse` correctly reject both no-version and v2 checkpoints

The version bump 2 → 3 is enforced by string equality (`!string.Equals(checkpointVersion, FindingsCheckpointVersion, StringComparison.Ordinal)`) and by the new mandatory `nextVaEndpointIndex` field. A v2 checkpoint would fail at the version gate first, the `nextVaEndpointIndex` gate second — defensive belt-and-braces. Good operational handling of the schema bump.

---

## Summary

The change is correct on its merits. The sole performance defect worth fixing before merge is **M1** (redundant property enumeration in the hot path), one-line fix. Everything else is non-blocking: m1–m4 are quality-of-code suggestions, n1–n5 are stylistic, O1–O7 are operational notes for posterity.

The risky surfaces — checkpoint v3 persistence, stage-resume semantics across three stages, hot-path JSON rewrite — are all handled with appropriate care:

- Stage transitions write the next-stage cursor before exiting the current stage's loop.
- Version bump is gated and tested against both pre-versioned and v2 inputs.
- Publish drift is hard-asserted (`InvalidOperationException`) per page.
- The Skip + sort-asc resume contract is explicitly tested for duplicate keys, with the accepted-tradeoff caveat that resume order under duplicates is whatever XQL returns (per the comment in `CollectAsync_ResumeFindingsCvesStage_OverDuplicateCveAndName_AdvancesByRowIndexInXqlOrder`).
- Collision policy on the `sourceType` property is fail-fast (`InvalidOperationException`), not silent-overwrite — explicitly tested and consistent with the docstring's "vendor schema change" rationale.

No blocker. M1 is the one local patch I would land before merge.

# Code Review — capture_list / accumulate_list flatten reroute

**Scope reviewed:** `CollectorExecutor/Execution/CollectorExecutorStepHelpers.cs` (capture_list / accumulate_list → `ListAtFlattened`); `Tests/CollectorExecutor.Test/CollectorExecutorTests.cs` (new `CaptureList_CrossingRepeatedParent_*` tests, new mock endpoints, `NestedCaptureProfile` / `XmlCaptureProfile`). Read `JsonNav.ListAtFlattened`/`ListAt`/`At`/`StringAt` for context.

**Verdict: CLEAN.** No BLOCKER/MAJOR/MINOR. Two NITs below, both optional.

## Verification performed
- Built and ran full suite: **77 passed, 0 failed**.
- **Counterfactual (does the test depend on the fix?):** reverted both call sites to `ListAt` in a throwaway repo copy and re-ran the two new tests → both **FAIL** (`Expected 6 / 4, Actual 0`). The for_each driver goes empty without flattening, so the tests are not vacuously green. Confirmed genuine.

## Findings against each lens

### Post-processing preserved (StepHelpers.cs:172-203) — OK
- `capture_list` (178-181): only the resolver swapped `ListAt`→`ListAtFlattened`; the `.OfType<JsonValue>().Select(v => v.ToString()).ToList()` tail is identical. `OfType<JsonValue>()` correctly drops nodes that land on an object/array rather than a scalar (e.g. a path terminating on a nested object), so a mis-pointed path silently yields fewer ids rather than throwing — same defensive behavior as before.
- `accumulate_list` (186-202): append + `!accumulated.Contains(value)` dedupe is unchanged; still order-preserving (first-seen wins). The shared-`List<string>` reuse via `runContext.CaptureLists` and the `CapturedAtUtc` first-write stamp (205-206) are untouched. No double-count: `value` is the post-`ToString()` scalar, so dedupe operates on final string keys, consistent with `capture_list` output. Correct.

### Other `ListAt`/`At`/`StringAt` callers untouched — OK
Swept all `JsonNav.*` callers in `CollectorExecutor/` + `Strategies/`:
- capture-SCALAR `JsonNav.At` (StepHelpers.cs:164), `Matches` `JsonNav.At` (211) — unchanged.
- poll_and_drain `drain_path` `JsonNav.ListAt` (CollectorExecutorRunner.cs:654) — **unchanged** (the one most at risk of accidental edit). Good.
- cursor / next_token / watermark `JsonNav.StringAt` (NextUrl/CursorWatermark/Cursor strategies) — unchanged.
Only the two intended call sites moved. `ListAtFlattened` itself unmodified.

### Over-collection risk — acceptable, by design
Flattening `groups.items.id` will pull ids from *every* `groups[].items[]` — that is exactly the intended "ids under a repeated parent" semantics and matches the records-extraction axis (`ids_path`/`records_path`). A profile author who points `capture_list` at a path that happens to traverse an unintended repeated array would now collect more than one element where `ListAt` previously returned one node's worth. This is the same tradeoff already shipped for `records_path`/`ids_path` and is consistent with the contract; not a regression introduced here. No null/type hazard: `FlattenInto` returns nothing for scalar-with-remaining-path branches and `AddTerminal` skips nulls; `.OfType<JsonValue>()` filters non-scalars at the tail.

### Test quality — strong
- Data is genuinely nested/repeated, not a flat array: `/grp/list` returns `groups[2]` each with `items[]` so the capture path crosses the repeated `groups` (StepHelpers/JSON test, lines 213-216). XML test `/qxml/hostlist` returns two repeated `<HOST>` elements (280). A flat array would have passed under the old code — these would not (confirmed by the counterfactual).
- **Dedupe assertion is meaningful (line 1135).** `SHARED` appears in both i1's and i2's `/grp/detail` blocks → 2 raw occurrences. The assert `Count(u => u.Contains("qid=SHARED")) == 1` directly proves the kb fetch fired once. Independently, the record total encodes it: without dedupe the count would be 3 detail + 4 kb = 7; the test asserts 6 (3 + 3 distinct). Both the count and the URL assertion pin the dedupe.
- XML test additionally exercises `batch_size`/`batch_as` CSV fan-in (2607-2616), so it covers capture→for_each→batched-hydrate, mirroring the real `integrations/qualys.yaml` findings shape.

### Allocation / perf — no concern
`ListAtFlattened` allocates one accumulator list and recurses over a strictly-shrinking acyclic tree (documented termination). For capture lists (small driver sets) this is negligible and equivalent in order to the prior `ListAt` path.

## NITs (optional, non-blocking)
- **NIT** — StepHelpers.cs:194: `accumulate_list` re-resolves and re-dedupes via `Contains` (O(n) per item) on every iteration. For the realistic small id/qid lists this is fine; if a profile ever accumulated thousands of distinct values across many iterations it would become O(n²). Not worth a `HashSet` companion now — flagging only so it is a conscious choice.
- **NIT** — The two new tests don't assert the *contents* of the captured list, only downstream record/URL counts. That is sufficient to prove flattening + dedupe, but a direct assertion on `runContext.CaptureLists["ids"] == [i1,i2,i3]` would localize a future regression faster. Optional; the current end-to-end assertions are adequate.

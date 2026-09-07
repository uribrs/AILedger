# Assumptions

## A1 — No second root retains the enriched assets — VALIDATED

Resolved by the orchestrator at planning time (it gated the scope decision, not
just execution). Evidence in
`Source/Infrastructure/Cymulate.Agent.Infrastructure.Common/Services/CybiBatchUploader.cs`:

- `SaveUploadAndDeleteBatchAsync` takes `IEnumerable<T> batchData` (`:57-62`) and
  passes it only to `WriteNdjsonFileAsync`.
- `WriteNdjsonFileAsync` (`:375-391`) does `foreach (T item in batchData)`,
  writes each row to a `StreamWriter`, and retains nothing past the iteration.
- Upload streams **from the file on disk** — `UploadBatchFileAsync` takes a
  `filePath` (`:393-398`), not the collection.
- `BatchScopeState` (`:731-740`) holds only counters, a `SemaphoreSlim`, and
  options. `BatchScopeUploadContext` (`:743`) holds sequence id, close flag, and
  a metadata `JObject` — no rows.

Conclusion: the accumulator is the only other holder, so `_buffer.Clear()` drops
the last reference once the caller releases the master list. The approved scope
is sufficient.

## A2 — Nullability approach — VALIDATED (`null!` at assignment site)

`<Nullable>enable</Nullable>` is set, and the collection is `List<JObject>`.
Two options:

- `iAssets[k] = null!` at the single assignment site — suppression is local and
  visible; success criterion "no sprayed warnings" is met.
- Widen to `List<JObject?>` — ripples through `fetchAndLogAssets` and
  `fetchPagedAssetsToMemoryAsync` signatures and into the legacy branch, for no
  runtime benefit.

**Recommendation: the first.** Executor confirms no analyzer (Qodana) escalates
the suppression to an error.

## A3 — Legacy StreamWriter branch has the same retention shape — VALIDATED (confirmed, and released there too)

The non-batch branch (`:198-219`) drains the same `outputQueue` through
`handleBatchWrite` while the assets stay rooted in `iAssets`, so the same
unbounded retention appears to apply there. It is not the customer-facing path
(batch mode is selected when `instanceId is not null`).

**Outcome: released there too.** The safety proof held — `handleBatchWrite`
drains the queue fully, serializes each row via `enriched.ToString(Formatting.None)`
into a `List<string>`, then clears the queue, so the `JObject` graph is already
dead weight when it returns; the strings are what `flushBatch`/`flushRemaining`
write, and no reader of `iAssets` elements exists afterwards. Independently
re-confirmed by verifier-1. Caveat: unexercised by tests, and not the
customer-facing path (batch mode is selected when `instanceId is not null`).

## A4 — Peak-live-set bound — CORRECTED after verifier-1: the bound is one PAGE, not one batch

Originally marked VALIDATED on the premise "the `O(one batch)` target holds only
if each asset is enriched once and nothing else roots it", enumerating only the
vuln caches. **That missed a second root and the correction matters.**

`fetchPagedAssetsToMemoryAsync` parses each page into a `JObject` and adds the
children of `json["resources"]` to the master list (`:524-535`, `assets.Add(obj)`).
Newtonsoft sets `JToken.Parent` on every child, so each asset holds an upward
reference to the page `JArray`, which holds **every asset on that page**. Nulling
window `[i, i+50)` therefore does not make those assets unreachable while any
other slot from the same page is still held — the survivor reaches them via
`Parent`.

With `cMaxPageSize = 100` (`:27`) and `cBatchSize = 50` (`:25`), a page spans
exactly two batch windows, so enrichment is reclaimed one batch later than
originally stated. **Achieved bound: ~one page (~100 assets), not ~50.**

The fix is still correct and still fixes the reported bug — retention is bounded
by a constant instead of growing with assets processed. Only the recorded figure
was wrong. Confirmed empirically by verifier-1's probe against the repo's own
Newtonsoft build, and by direct reading of `:533`.

Operationally important: the validation rerun is this change's only evidence
(A5 leaves no regression guard). An operator expecting a 50-asset sawtooth who
measures a ~100-asset one could wrongly read it as the fix not working.

The vuln caches (`rGlobalVulnCache`, `rSolutionIdCache`, `rSolutionCache`,
`:22-24`) are process-lifetime and bounded by catalog size rather than asset
count — still out of scope, and verified NOT to be an additional asset root
because every cache read returns `DeepClone()`, so cached tokens are never
reparented into an asset. Measured heap will not fall to zero.

Detaching assets from their page at fetch time would yield the literal
`O(one batch)`, but it touches the fetch path and is deliberately NOT bolted onto
this change — see D8.

## A5 — Verification method — RESOLVED via option (c); regression-guard gap accepted

No existing test covers `processAndWriteAssetFindings`; the collector's only test
file is `InsightVmBatchAccumulatorTests.cs`, so the 7 passing tests do not gate
this change.

**Outcome: option (c) — code proof plus build/test.** Option (b) (internal seam)
was rejected as it requires widening visibility, a production change beyond the
approved scope. Option (a) at the accumulator level was rejected as it would only
prove the accumulator releases on flush, which was already true and is not the
bug; proving it at the collector level needs the whole HTTP surface faked.

**Accepted gap:** no automated assertion that the slots are released, so a future
edit can reintroduce the retention with all tests green. Recommended follow-up
(recorded, deliberately not taken): make the method `internal` and assert
released slots plus a `WeakReference` death after flush.

All five are now closed. Evidence and the exact limits of each resolution are in
`execution_notes.md` — in particular A5, where no automated regression guard
exists and the recommendation (make the method `internal`, add a released-slots
plus `WeakReference` test) was deliberately NOT acted on as it exceeds scope.

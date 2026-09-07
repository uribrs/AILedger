# Execution Notes

## What changed

`Source/CybiCollectors/InsightVmCollector/InsightVmCollector.cs` — +15 lines, 0
removed. Two symmetric releases inside `processAndWriteAssetFindings`:

- **Batch-upload branch** (after the `outputQueue` drain at `:190-193`): null the
  master list slots for the batch window. Ownership passes to the accumulator,
  whose existing `_buffer.Clear()` drops the last reference on its byte-capped
  flush.
- **Legacy StreamWriter branch** (after `handleBatchWrite`): same release — see
  A3 below for why this turned out to be provably safe.

Loop variable named `index` (explicit type, no `var`, no `i*` prefix). Comments
carry the ownership invariant, not a restatement of the assignment.

## Assumption resolutions

### A1 — VALIDATED (by orchestrator, before execution)
`CybiBatchUploader` does not retain rows. Not re-derived here.

### A2 — RESOLVED: `null!` at the assignment site
Chosen over widening to `List<JObject?>`, which would ripple through
`fetchAndLogAssets` and `fetchPagedAssetsToMemoryAsync` signatures and the legacy
branch for no runtime benefit.

Verified, not assumed:
- `dotnet build` of the collector project: **0 errors**.
- `CS8602` count in `InsightVmCollector.cs` is **2 with the change and 2 at HEAD**
  (measured by stashing only this file, rebuilding, then popping). The warning is
  pre-existing at `:870` — `Interlocked.Add(ref rCollectedVulnerabilities,
  allVulns.Count)` in `fetchAndValidateVulnIds`, a different method. Nullable flow
  analysis is per-method, so this change cannot affect it.
- **Zero** warnings in the edited region (lines 188-230).
- `qodana.yaml` uses the default `qodana.starter` profile with no `include`,
  `exclude`, or severity overrides, so nothing escalates the suppression.

### A3 — RESOLVED: released in the legacy branch too (proof, not assumption)
`handleBatchWrite` (`:237-253`) drains the queue fully (`while TryDequeue`),
serializes each asset via `enriched.ToString(Formatting.None)` into a
`List<string>`, then calls `iOutputQueue.Clear()`. So when it returns the
`JObject` graph is already dead weight — the **strings** are what get written by
`flushBatch`/`flushRemaining`, and nothing downstream reads `iAssets` elements
(`CollectFindingsAsync` reads only `assets.Count`, `:131` and `:137`).

Output-neutral by construction. Applied rather than left, because leaving a
known-identical leak in the sibling branch of the same method is a worse outcome
than the marginal risk. Noted: this path has no test coverage and is not the
customer-facing path (batch mode is selected when `instanceId is not null`), so
the change there is reasoned-safe but not exercised.

### A4 — CORRECTED after verifier-1: bound is one PAGE (~100 assets), not one batch
Assets are added to the master list as children of the page's `resources` `JArray`
(`:533`), and Newtonsoft's `JToken.Parent` makes any surviving asset root its
entire page. With `cMaxPageSize = 100` and `cBatchSize = 50`, a page spans two
batch windows, so enrichment is reclaimed one batch later than first stated.
Retention is still bounded by a constant rather than growing with assets
processed — the bug is fixed; the figure was 2x optimistic. Corrected in A4; the
commit message of `b13762a272` carries the same overstatement and was left
unamended pending the user's call on rewriting unpushed history.

### A4 (caches) — NOTED, out of scope
`rGlobalVulnCache`, `rSolutionIdCache`, `rSolutionCache` (`:22-24`) are
process-lifetime per-*vulnerability* caches, bounded by catalog size rather than
asset count. Measured heap will **not** fall to zero after this fix, and that is
expected rather than a failure.

### A5 — RESOLVED: option (c), reasoning plus rerun validation. Scope-limited.
Chosen deliberately, and it is the weakest part of this change — stated plainly:

- `processAndWriteAssetFindings` is **private**. `InternalsVisibleTo`
  ("InsightVmCollector.Tests") exists but does not reach private members.
- Option (b) would require widening visibility — a production change beyond the
  approved scope. Not taken unilaterally.
- Option (a) at the accumulator level would only prove the accumulator releases
  on flush, which was **already true and is not the bug**. Proving it at the
  collector level needs the whole HTTP surface faked to drive
  `CollectFindingsAsync` — well beyond this change.

**What the chosen verification does NOT prove:** there is no automated test
asserting that the master list slots are actually released, so a future edit
could reintroduce the retention silently. Correctness here rests on the code
proof above plus the build/test run, not on a regression guard.

**Recommendation for the user (not acted on):** make
`processAndWriteAssetFindings` `internal` and add a test asserting released slots
and a `WeakReference` to an enriched asset dying after flush. That is a small,
behavior-neutral visibility change, but it is a scope decision, not the
executor's call.

## Commands run

- `dotnet build Source/CybiCollectors/InsightVmCollector/InsightVmCollector.csproj`
  → 0 errors, 403 warnings (all pre-existing, mostly CS0649/CA1416 in other projects).
- `dotnet test Tests/CybiCollectors/InsightVmCollector.Tests/InsightVmCollector.Tests.csproj`
  → **Passed: 7, Failed: 0, Skipped: 0** (531 ms).
- HEAD-vs-change warning comparison via `git stash push -- <single file>` / `git stash pop`.

## Residual risks

0. **Stated bound was 2x optimistic** — real bound is one parse page (~100
   assets) because of the `JToken.Parent` chain. Corrected in A4 and here; the
   commit message still overstates it. Matters because the rerun is the only
   evidence this fix gets: expect a ~100-asset sawtooth, not a 50-asset one.
1. **No regression guard** on the released slots (A5). Highest-value follow-up.
2. **Legacy branch change is unexercised** — reasoned safe, no test, not the
   customer path (A3).
3. **This fix does not make the Augsburg run succeed.** Stalls were 18% of wall
   clock; removing them leaves ~53h against a 48h `executionTimeout`. The
   `O(assets × vulns)` fan-out is the remaining blocker and is deferred by
   decision D4. Validating this fix against a rerun will show a flat pause curve
   and a timeout, not a green run.
4. Deferred items untouched as instructed: 404 retry policy, silent-drop paths,
   payload deduplication, GC configuration.

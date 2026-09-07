# Verifier 1 — InsightVM Collector Heap Retention

Scope: did the completed work satisfy the original request and the contract?
Branch `fix/insightvm-collector-heap-retention`, commits `b13762a272` + `419bf1e31f`.

## Verdict

**PASS WITH GAPS**

The code change is correct, output-neutral, index-safe, within the approved scope,
and cleanly committed. One material accuracy gap: the retention bound actually
achieved is `O(one page)` (≈2 batches, 100 assets), not the `O(one batch)` stated
in the contract Goal, the commit message, and assumption A4 — because Newtonsoft
`JToken.Parent` links make a surviving asset root every sibling on its parse page.
Verified empirically, not inferred. The fix still works (the bound is constant and
no longer grows with assets processed); the recorded reasoning is what is wrong.

Remaining findings are bookkeeping drift in the task artifacts.

## Preliminary: the contract has no "Success Criteria" section

`prompt_contract.md` contains Role / Goal / Context / Constraints / Execution Rules /
Output Format / Stop Conditions. There is no section titled Success Criteria, yet
`state.json` asserts `validation.successCriteriaPresent: true` and `assumptions.md`
A2 cites 'success criterion "no sprayed warnings"' — a criterion that appears
nowhere in the contract. The table below is therefore assembled from the Goal,
Constraints, Execution Rules, and Output Format, which are the operative
requirements. Logged as Finding 4.

## Success Criteria table

| # | Criterion (source) | Verdict | Evidence |
|---|---|---|---|
| 1 | Peak live set `O(one batch)` rather than `O(all assets processed)` (Goal) | **Partially met** | Growth with assets processed is eliminated — that is the actual bug and it is fixed. But the achieved bound is one *page* (`cMaxPageSize = 100`, `InsightVmCollector.cs:26`) not one *batch* (`cBatchSize = 50`, `:25`). See Finding 1 with runnable evidence. |
| 2 | Uploaded payload byte-identical; enriched asset still lands in the batch, no exceptions (Constraints) | **Met** | `git diff --stat 44c1f1c85b..HEAD` = 20 insertions, 0 deletions, 0 modifications. Nothing in build (`enrichAndQueueAsset:425`), enqueue (`:426`), serialize (`InsightVmBatchAccumulator.cs:55`, `CybiBatchUploader.cs:385`), or upload (`CybiBatchUploader.cs:393`) is touched. |
| 3 | Release runs strictly **after** `AddAsync` has taken the row, never earlier (Constraints) | **Met** | `InsightVmCollector.cs:198-201` sits after the drain loop `:190-193` completes. `processAssetBatch` awaits `Task.WhenAll` (`:313`) before returning, so every producer for window `i` has enqueued before the drain; `TryDequeue` empties the queue. No path nulls a slot whose row has not been added. |
| 4 | No `Remove`/`RemoveRange`; `iAssets.Count` constant (Constraints) | **Met** | Only the indexer setter is used (`:200`, `:224`). Probe output: `assets.Count still = 2` after both slots nulled. `CollectFindingsAsync:131,137` read only `.Count` — unaffected. |
| 5 | `Skip(i)` must never dereference an already-nulled slot (Constraints) | **Met** | At iteration `i`, nulled slots are exactly `[0, i)`; `iAssets.Skip(i).Take(cBatchSize)` (`:185`, `:214`) yields `[i, min(i+50, Count))`. Windows are disjoint and strictly forward. `Math.Min(i + cBatchSize, iAssets.Count)` correctly clamps the final partial batch, so no `ArgumentOutOfRangeException` on the indexer setter either. |
| 6 | Explicit types over `var`; no `i*` prefix in new code (Constraints) | **Met** | `int index` at `:198`, `:222`. No `var` introduced. |
| 7 | Comments carry the non-obvious invariant, 1–2 sentences (Constraints) | **Met** | `:195-197` (2 sentences, states ownership transfer and why omitting it defeats the accumulator flush); `:220-221` (1 sentence, states the rows are already serialized). Neither restates the assignment. Complies with `ReadMEs/coding-standards.md:204-207`. |
| 8 | Do not touch 404 retry policy, silent-drop paths, payload dedup, GC config, API fan-out (Constraints) | **Met** | Whole-branch diff is 2 files / 20 added lines. `processSingleAsset`'s blanket catch and missing-ID skip, the retry policy, and the fan-out are byte-for-byte unchanged. |
| 9 | Stage explicit paths; `ai/` not swept into commits (Constraints) | **Met** | `git show --stat` on both commits lists only the two source files. `git status --porcelain` = `?? ai/` only. |
| 10 | Falcon `SupportsBatchScopedUpload` as a separate commit (Constraints, D6) | **Met** | `419bf1e31f`, 5 added lines in `FalconCollector.cs`, message explicitly flags it as unrelated and carried by request. |
| 11 | Close A1 first: `CybiBatchUploader` must not retain the list or its rows (Execution Rules) | **Met — independently re-verified** | `SaveUploadAndDeleteBatchAsync` (`CybiBatchUploader.cs:57`) passes `batchData` only to `WriteNdjsonFileAsync` (`:97`, `:251`, `:293`), which `foreach`es once into a `StreamWriter` (`:375-391`) and retains nothing. `UploadBatchFileAsync` (`:393`) takes a `filePath`. `BatchScopeState` (`:731-740`) holds `Options`, a `SemaphoreSlim`, and counters. `BatchScopeUploadContext` (`:745`) is `(int, bool, JObject)` metadata. The scoped path (`SaveUploadAndDeleteScopedAsync:213-280`) is the same shape. No second root. |
| 12 | Resolve A2, A3, A5 and record the outcome rather than silently choosing (Execution Rules) | **Met**, with stale body text | All three carry a status header and a rationale in `assumptions.md` plus detail in `execution_notes.md:18-80`. A3's body still contains its pre-decision instruction — Finding 3. |
| 13 | A2 supporting claims (build clean, no analyzer escalation) | **Met** | Re-ran: 0 errors, 403 warnings. Exactly one distinct warning in `InsightVmCollector.cs` — `CS8602` at `:870,60`, in `fetchAndValidateVulnIds`, pre-existing and in a different method (nullable flow analysis is per-method). Zero warnings in the edited regions. `qodana.yaml` is `profile: qodana.starter` with `include`/`exclude` commented out — nothing escalates the `null!` suppression. |
| 14 | A3 claim: `handleBatchWrite` serializes rows to strings first, so the legacy release is output-neutral | **Met — claim verified** | `handleBatchWrite:242-252`: `while (iOutputQueue.TryDequeue(...)) iWriteBatch.Add(enriched.ToString(Formatting.None));` then `iOutputQueue.Clear()`. `flushBatch:259` writes `string.Join("\n", iBatch)` — strings only. `flushRemaining:265-274` likewise. No reader of `iAssets` elements exists after the loop (grep of every `iAssets`/`assets` occurrence: `:131`, `:137` are `.Count`). Genuinely output-neutral. |
| 15 | Build result + `InsightVmCollector.Tests` result reported (Output Format 4) | **Met — reproduced** | See Evidence. Numbers match `execution_notes.md:82-88` exactly. |
| 16 | State what the verification does **not** prove (Output Format 5) | **Met** | `execution_notes.md:71-74` states plainly that no automated test asserts the released slots and that a future edit could reintroduce the retention silently. A5's confessed weakness is not glossed. |
| 17 | `execution_notes.md` appended; `state.json` steps and assumption statuses updated (Output Format 7) | **Partially met** | `execution_notes.md` is complete (100 lines) and all seven steps in `state.json` are `complete` with resolutions. But `state.json:15` still records `requiredFiles["execution_notes.md"]: "pending"` — Finding 2. |
| 18 | Honest caveat preserved: this fix alone leaves ~53h against a 48h timeout | **Met** | `execution_notes.md:95-99` ("Validating this fix against a rerun will show a flat pause curve and a timeout, not a green run") and `state.json:119`. Not softened anywhere. |
| 19 | Stop Conditions — none should have fired | **Met** | A1 held; byte-identity held; no out-of-scope change was needed; build and tests clean. Correctly not triggered. |

## Findings

### 1 — MATERIAL: the bound is `O(one page)`, not `O(one batch)`; A4 missed the `JToken.Parent` root

`Source/CybiCollectors/InsightVmCollector/InsightVmCollector.cs:198-201` (and `:222-225`)

Assets are added to the master list as *children of the page response*:
`fetchPagedAssetsToMemoryAsync:516` parses the page with `JObject.LoadAsync`
(`:1071-1077`), then `:524-535` walks `json["resources"]` and adds each child
`JObject` to `assets`. Newtonsoft sets `JToken.Parent` on every child, so each
asset holds an upward reference to the page `JArray`, which holds **all** assets on
that page.

Consequence: nulling window `[i, i+50)` does not make those assets unreachable
while any *other* slot from the same page is still non-null — the survivor reaches
them via `Parent`. With `cMaxPageSize = 100` (`:26`) and `cBatchSize = 50` (`:25`),
each page spans exactly two batch windows, so an asset's enrichment is reclaimed
one batch later than the commit message implies. Peak retained asset graph is
~100 assets, not ~50.

Verified, not reasoned — probe against the repo's own `Newtonsoft.Json.dll`,
mimicking `fetchPagedAssetsToMemoryAsync` + `enrichAndQueueAsset`:

```
after nulling slot 0 only : slot0 enrichment alive = True  (slot1 still held: True)
after nulling whole page  : slot0 enrichment alive = False, slot1 = False
assets.Count still = 2
```

Impact is limited, and I want to be precise about why this is not a blocker:

- The **bug is still fixed.** Retention no longer scales with assets processed; it
  is bounded by a constant. That is the whole point of the change.
- Output is unaffected.
- The vuln caches are *not* an additional path here: every cache read returns
  `DeepClone()` (`:711`, `:725`, `:794`, `:814`, `:830`, `:844`), so cached tokens
  are never reparented into an asset and there is no cache→asset back-reference.
  A4's cache reasoning is correct as far as it goes.

What is wrong is the recorded analysis. `assumptions.md:48-54` (A4) is marked
VALIDATED on the premise "the `O(one batch)` target holds only if each asset is
enriched once and **nothing else roots it**", and then enumerates only the vuln
caches. The parent chain is exactly the "something else" A4 set out to rule out,
and it was missed. The same overstatement is in the contract Goal
(`prompt_contract.md:9-11`) and in the commit message of `b13762a272`.

Why it matters operationally: the validation rerun is the only evidence this fix
will get (A5 leaves no regression guard). An operator expecting a 50-asset sawtooth
who measures a 100-asset sawtooth may read it as the fix not working. A4 should be
corrected before that rerun.

Cheapest correction, if anyone wants the literal `O(one batch)`: `cBatchSize`
already divides `cMaxPageSize`, so no code change is needed for correctness —
either amend A4 and the stated bound to "one page", or (separate change, out of
this scope) detach assets from their page at fetch time. Do **not** bolt the latter
onto this commit; it touches the fetch path and would break the clean attribution
that D4 was written to protect.

### 2 — MINOR: `state.json` contradicts its own artifact list

`state.json:15` — `requiredFiles["execution_notes.md"]: "pending"` while the file is
complete and every step is `complete`. Contract Output Format 7 requires this file
be recorded as written. A resumed session reading `state.json` first would conclude
execution notes are missing.

### 3 — MINOR: `assumptions.md` A3 body contradicts its own header and the delivered code

`assumptions.md:37-46`. Header reads "VALIDATED (confirmed, and released there
too)"; the body's last line still reads "Executor decides; default to leaving it
untouched and recording the finding rather than widening this change." The legacy
branch *was* changed (`InsightVmCollector.cs:222-225`), correctly and with proof.
The stale instruction should be replaced by the outcome. Same pattern in A5
(`:56-71`), which still presents "Options for the orchestrator" in the imperative
after the fact, and whose status label "REJECTED" is a confusing way to say
"resolved via option (c)".

### 4 — MINOR: no Success Criteria section, but two artifacts claim there is one

`prompt_contract.md` has no such section; `state.json:111`
(`successCriteriaPresent: true`) and `assumptions.md:29` (a "success criterion" not
present in the contract) both assume one. Verification against this contract
therefore requires reconstructing criteria from Constraints + Output Format, which
is exactly the ambiguity a Success Criteria section exists to remove.

### 5 — MINOR: `state.json` timestamps are not a usable record

All three `skillsRun` entries carry the identical `completedAt`
`2026-07-29T14:06:23Z`, and `lastUpdated` is the same value — yet both commits are
authored `17:14:42`/`17:14:51 +0300` (= 14:14 UTC), eight minutes *after* the file
claims the executor finished. Designer, orchestrator, and executor cannot all have
completed in the same second. Cosmetic, but `state.json` is the cross-session
resumption record, so its timestamps should be real.

### 6 — MINOR: stale line references in `execution_notes.md`

`execution_notes.md:40` cites `handleBatchWrite (:230-246)`; post-change it is
`:237-253`. `:44-45` cites `CollectFindingsAsync` reading `assets.Count` at
`:131`/`:137` — those are correct.

### 7 — Informational, no action: `List<T>` version bump is currently harmless

`iAssets[index] = null!` goes through `List<T>.set_Item`, which increments the
list's internal `_version`. This would throw `InvalidOperationException` on any
enumerator over `iAssets` that is live across the assignment. Today none is:
`.ToList()` at `:185`/`:214` fully materializes the window before
`processAssetBatch` is called, and no other enumeration of the findings list
exists. Recording it because it is the kind of latent constraint a future
"optimization" — e.g. dropping `.ToList()` and passing the lazy `Skip`/`Take`
sequence straight to `processAssetBatch` — would silently violate.

### Checks that came up clean (no finding)

- **Nulled slot reaching a consumer.** `batch` is built only from
  `[i, min(i+50, Count))`, all non-null at that moment, so no `null` can reach
  `processAssetBatch:301` → `processSingleAsset:321` →
  `getAssetIdAndValidateAsync:382` (`iAsset["id"]`). No `NullReferenceException`
  path exists.
- **`enrichAssetsWithTagsInMemoryAsync`** (`:553-575`) operates on the *assets*
  flow's separate list built in `fetchAssetsAsync:444`. It never sees the findings
  list. No interaction.
- **Assets that never enqueue** (blanket catch `:328`, missing-ID skip `:331-334`,
  dry-run `:352-361`) are still nulled — harmless, since they were never going to
  be emitted, and nothing re-reads them.
- **Deferred scope.** Nothing was "improved" in passing; the 20-line additive diff
  proves it.
- **Commit hygiene.** Two commits, source files only, `ai/` untracked, tree
  otherwise clean. The pre-existing uncommitted `FalconCollector.cs` modification
  became `419bf1e31f` as D6 directed.

## Evidence

Observed directly, this session.

```
$ git diff --stat 44c1f1c85b..HEAD
 Source/CybiCollectors/FalconCollector/FalconCollector.cs  |  5 +++++
 .../InsightVmCollector/InsightVmCollector.cs              | 15 +++++++++++++++
 2 files changed, 20 insertions(+)

$ git status --porcelain
?? ai/

$ git log --oneline -3
419bf1e31f Declare batch-scoped upload support in FalconCollector
b13762a272 Release enriched assets after batch add in InsightVmCollector
44c1f1c85b Bump versions Service: 7.844 Executor: 650.804 Installer: 19.790 CA-58780
```

Build:

```
$ dotnet build Source/CybiCollectors/InsightVmCollector/InsightVmCollector.csproj
.../InsightVmCollector/InsightVmCollector.cs(870,60): warning CS8602: Dereference of a possibly null reference.
    403 Warning(s)
    0 Error(s)
Time Elapsed 00:00:09.10

$ dotnet build ... -t:Rebuild 2>&1 | grep "InsightVmCollector.cs(" | sort -u
.../InsightVmCollector.cs(870,60): warning CS8602: Dereference of a possibly null reference.
```

One distinct warning in the file, at `:870`, pre-existing, different method. The
raw grep count is 2 (build line + summary line), which is what
`execution_notes.md:31` reports as "2 with the change and 2 at HEAD" — consistent.

Tests:

```
$ dotnet test Tests/CybiCollectors/InsightVmCollector.Tests/InsightVmCollector.Tests.csproj
Passed!  - Failed: 0, Passed: 7, Skipped: 0, Total: 7, Duration: 404 ms - InsightVmCollector.Tests.dll (net8.0)
```

Test project contains exactly one test file, `InsightVmBatchAccumulatorTests.cs` —
confirming A5's premise that nothing covers `processAndWriteAssetFindings`, and
that the 7 passing tests do not gate this change.

Parent-chain probe (Finding 1), compiled against
`Tests/CybiCollectors/InsightVmCollector.Tests/bin/Debug/net8.0/Newtonsoft.Json.dll`,
source at
`/private/tmp/claude-501/-Users-user-Dev-AgentService/8bc32df4-b221-4aa1-9c45-96ebaf40e1a7/scratchpad/parentprobe/Program.cs`:

```
after nulling slot 0 only : slot0 enrichment alive = True  (slot1 still held: True)
after nulling whole page  : slot0 enrichment alive = False, slot1 = False
assets.Count still = 2
```

## Unresolved gaps — what a future session must know

1. **A4 is incomplete and should be amended.** The `JToken.Parent` chain is a
   second root; the real bound is one parse page (100 assets), not one batch (50).
   Fix the wording in `assumptions.md` A4 and in the contract Goal before anyone
   interprets the validation rerun's heap curve.
2. **No regression guard exists** (A5, honestly stated). The recommended follow-up
   — make `processAndWriteAssetFindings` `internal` and assert released slots plus
   a `WeakReference` death after flush — is recorded at `execution_notes.md:76-80`
   and was deliberately not taken. Until it exists, a future edit can reintroduce
   the retention with all tests green.
3. **The legacy StreamWriter release is unexercised.** Proven output-neutral by
   code reading (criterion 14 above, independently confirmed), but it has no test
   and is not the customer path (`instanceId is not null` selects batch mode).
4. **This branch does not make the customer run succeed.** ~53h of `O(assets ×
   vulns)` fan-out against a 48h `executionTimeout`. The rerun should show a flat
   pause curve and still time out. Preserved correctly in
   `execution_notes.md:95-99` and `state.json:119`; do not let it get lost when
   this is reported upward.
5. **`state.json` bookkeeping** (Findings 2 and 5) should be reconciled before the
   task directory is used to resume work.
6. `ai/` remains untracked and is not gitignored — the constraint against
   `git add .` still applies to anyone continuing on this branch.

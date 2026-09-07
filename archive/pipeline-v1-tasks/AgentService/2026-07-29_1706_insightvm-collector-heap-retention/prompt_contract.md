# Prompt Contract

Role:
You are a senior .NET engineer working on the Cymulate Agent Service, fluent in
CLR memory behavior (object reachability, generational GC, LOH) and in this
repo's CYBI collector execution model.

Goal:
Make enriched assets unreachable once the batch accumulator has taken their row,
so `InsightVmCollector`'s peak live set is bounded by a constant rather than
growing as `O(all assets processed)` — with the uploaded payload byte-identical
to today.

Bound corrected after verifier-1: the achieved bound is **one parse page (~100
assets)**, not one batch (~50). Assets are added to the master list as children
of the page's `resources` `JArray` (`:533`), and Newtonsoft's `JToken.Parent`
makes any surviving asset root its whole page. Eliminating growth-with-assets is
the fix; the 50-asset figure was 2x optimistic. See `assumptions.md` A4.

Context:
- Target: `Source/CybiCollectors/InsightVmCollector/InsightVmCollector.cs`,
  method `processAndWriteAssetFindings`, batch-upload branch (`:178-197`).
- The leak: `List<JObject> assets` (`:125`) is a GC root until
  `CollectFindingsAsync` returns (`assets.Count` read at `:131`, `:137`).
  Enrichment is written into an element of that list
  (`iAsset["vulnerabilityDetails"] = new JArray(enriched)`, `:410`). Batches are
  `iAssets.Skip(i).Take(cBatchSize).ToList()` (`:185`) — same references. So
  `_buffer.Clear()` in `InsightVmBatchAccumulator.FlushAsync`
  (`InsightVmBatchAccumulator.cs:112`) drops only the accumulator's reference;
  the master list keeps the entire enrichment subgraph alive.
- Intended change, after the `while (outputQueue.TryDequeue(...))` drain loop
  (`:190-193`):

      for (int k = i; k < Math.Min(i + cBatchSize, iAssets.Count); k++)
      {
          iAssets[k] = null!;
      }

  with a one-or-two-sentence comment stating the ownership-transfer invariant
  (why, not what).
- Field evidence, for context only — do not re-derive: run
  `6a66f147c60e4f5de19a862e` stalled 26 times (5.2h cumulative, growing 3min →
  28min with assets processed) and died silently at asset ~2,300 of 5,541 with
  no managed exception.
- Read before editing: `ReadMEs/coding-standards.md` (authoritative style),
  `ReadMEs/cybi-attack-flow.md` (collector execution model).

Success Criteria:
* Peak retained asset graph is bounded by a constant and no longer grows with
  assets processed (bound: one parse page, ~100 assets — see Goal).
* Uploaded payload byte-identical to pre-change behavior; no asset can be dropped
  as a result of this change.
* Build clean (no new warnings attributable to the change); existing
  `InsightVmCollector.Tests` pass.
* Nullability handled without spraying warnings.
* Deferred items untouched.
* Two separate commits; no `ai/` artifacts staged.

Constraints:
* Uploaded payload byte-identical; the enriched asset must still land in the
  batch, no exceptions. Change only who holds references.
* The release must run after `AddAsync` has taken the row — never earlier.
* No `Remove`/`RemoveRange`; `iAssets.Count` stays constant; nulled earlier
  slots must never be dereferenced by `Skip(i)`.
* Explicit types over `var`; no `i*` prefix in new code; comments only where
  they carry non-obvious information.
* Do not touch: 404 retry policy, silent-drop paths (`:357-360`, `:368-371`),
  payload deduplication, GC configuration, or the API fan-out.
* Stage explicit paths when committing — `ai/` is untracked and not gitignored.
* Keep the Falcon `SupportsBatchScopedUpload` change as a separate commit.

Execution Rules:
* Do not assume missing data.
* Respect constraints strictly.
* **Close A1 first** (`assumptions.md`): confirm `CybiBatchUploader` does not
  retain the `List<JObject>` or its rows past
  `SaveUploadAndDeleteBatchAsync`. If it does, stop and report — the fix is
  incomplete and the scope decision must be revisited.
* Resolve A2 (nullability), A3 (legacy branch), A5 (verification method) and
  record the outcome; do not silently pick and move on.
* If the legacy StreamWriter branch cannot be shown safe to change, leave it and
  record the finding — do not widen scope to make it work.

Output Format:
1. Diff of the change (file, method, exact lines).
2. A1 finding: whether any second root retains the rows, with the evidence.
3. Resolution of A2, A3, A5 — decision plus one-line rationale each.
4. Build result and `Tests/CybiCollectors/InsightVmCollector.Tests` result.
5. How unreachability was verified, and what that verification does *not* prove.
6. Commit list, confirming the Falcon change is separate.
7. Appended `execution_notes.md`; `state.json` steps and assumption statuses
   updated.

Stop Conditions:
* A1 fails — a second root retains the rows.
* The byte-identical output constraint cannot be met.
* Achieving `O(one batch)` would require changes beyond the approved scope.
* Build or existing tests break in a way the change cannot account for.

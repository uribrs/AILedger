# Verifier Report — yaml-engine-envelope-parity (phase 2)

**Verdict: PASS-WITH-GAPS**

Four opt-in `merge_into` capabilities (require_key, strip, sort, chunk) are implemented
additively, flag-gated, generic, at the single choke point, with egress untouched. All
gates green (engine 699, adapter 166, magic schema-validation 7/7). Independent
recomputation from the raw NDJSON reproduces every parity claim in execution_notes. One
gap against an explicit Success Criterion: the crowdstrike-falcon.yaml **booked-deltas
header was NOT updated** — it still lists chunk framing, apps/suppression_info strip, and
remediation.entities sort as open/accepted deltas, though features A/C/D closed all three.
Documentation-only; code, tests, and live parity are correct.

## Success-Criteria Table

| # | Criterion | Status | Evidence |
|---|-----------|--------|----------|
| 1 | Per-feature flag-off=today / flag-on=native tests; chunk covers exact-cap off-by-one + zero-finding | MET | MergeIntoTests.cs: flag-off tests for all four (461, 687, 824); `Chunk_NEqualsCapExactly_TrailingTerminalIsEmpty` (1040), `Chunk_NEqualsTwiceCap_...TrailingTerminalIsEmpty` (1064), `Chunk_ZeroFindingsTarget_OneEmptyTerminalRecord` (1113) |
| 2 | Engine + adapter + magic catalog/schema gates green | MET (with documented pre-existing) | Engine 699/699, adapter 166/166, magic schema-validation 7/7. Sole CatalogBaseline failure names ONLY defender-vm.yaml + qualys.yaml `body_cursor` (pre-existing dual-engine drift); crowdstrike-falcon **absent** — re-verified live |
| 3 | Schemas byte-identical across repos | MET | `diff` of the two integration.schema.json → byte-identical |
| 4 | Live: assets==357 (phantom dropped, not +1) | MET-per-A7 | null-aid=0 / empty-aid=0 in the yaml run (phantom closed); absolute 358 vs 357 is live tenant drift, not the phantom — assumption A7 governs (drift, not frozen numbers) |
| 5 | Per-host finding counts match native modulo drift | MET | 354 common aids; per-host deltas consistent with ~10% day-over-day growth |
| 6 | Chunk on: record count + per-record envelope match record-for-record for overlapping hosts | MET | Key order single-variant `(aid,chunk,isLastChunk,findingsInChunk,host,findings)` on 100% of records both sides; 4 multichunk hosts (same aids); non-terminal chunks all ==2000; exactly one terminal per host; findingsInChunk==len(findings) on every record |
| 7 | Written record-for-record comparison report | MET | execution_notes.md final section |
| 8 | falcon.yaml consumes all four; booked-deltas header updated | **PARTIAL** | Consumes all four (require_key 131, strip 135, sort 139, chunk 146) — YES. Booked-deltas header (lines 21–47) still lists chunk/strip/sort as open deltas — **NOT updated** |

## Checks (independently re-run)

1. **Engine gate** — `Passed! Failed: 0, Passed: 699` (net8.0). Green, no flake.
2. **Adapter gate** — `Passed! Failed: 0, Passed: 166`. Green.
3. **Magic gate** — `Failed: 1, Passed: 7`. Sole failure `CatalogBaselineTests.EveryCatalogFile_LoadsThroughEngine...`, message names **only** `defender-vm.yaml` line 149 and `qualys.yaml` line 109 `body_cursor` → PaginationStrategy enum (magic engine lacks body_cursor; pre-existing dual-engine drift). crowdstrike-falcon NOT in the failure — loads clean. Matches expected state.
4. **Additive / flag-off invariance** — inspected the working-tree diff (uncommitted; isolated from the committed phase-1 `eb5110d`):
   - WorkflowConfig.cs: four new nullable/default-false fields on MergeIntoConfig; new SortConfig/ChunkConfig types. No existing field changed.
   - MergeEnrichment.cs: RequireKey guard `if (plan.RequireKey && string.IsNullOrEmpty(key)) return false;` (both ApplyEmbed + ApplyGrouped); strip guarded `plan.Strip is { Count: > 0 }`; sort guarded `plan.Sort is { }`. Flag-off falls through to the exact prior path.
   - MergeEnrichmentSink.cs: `ApplyChunkFraming` returns the **same list reference** when no plan declares chunk — flag-off allocates nothing, changes nothing.
   - Existing merge consumers: qualys.yaml + insightvm-cloud.yaml set **none** of the four fields (verified in-block). Loader rejections are all guarded by the new fields' presence.
5. **Schema byte-identity** — `diff` → identical.
6. **Grammar constraint** — the four fields are additive members of the existing `merge_into` block (schema: added under the existing merge properties; falcon: nested under `merge_into`). No new syntax family.
7. **Parity evidence (recomputed from NDJSON, not trusted from notes)** — native `20260721-125550/collector-run` (365 records / 357 aids / 35,651 findings) vs yaml `20260722-111632-yaml` (366 / 358 / 39,387):
   - Key order: 100% single variant both sides = `(aid, chunk, isLastChunk, findingsInChunk, host, findings)`.
   - Multichunk: 4 hosts both sides, same aids. Non-terminal chunks ==2000 (0 violations); exactly one terminal each (0 bad); findingsInChunk==len(findings) (0 mismatch); zero-finding hosts → one `{chunk:0,isLastChunk:true,findingsInChunk:0}` (0 bad).
   - Strip: 0 findings carry apps/suppression_info/host_info, both sides.
   - Sort: remediation.entities (>=2) ordinal-sorted 14324/14324 native, 15547/15547 yaml = 100%.
   - Phantom: null-aid=0, empty-aid=0 in the yaml run.
   - Deltas: 354 common aids, 3 native-only, 4 yaml-only; +1 asset / +3,736 findings = live drift (yaml ~a day later), not structural. Consistent with A7.
8. **Deviation (reject-vs-clamp on chunk.max)** — documented in WorkflowConfig.cs XML doc, loader comment, execution_notes (ORCHESTRATOR FLAG), and decisions. Harmless: falcon uses `max: 2000` (in range); MergePlan.Create still clamps for non-loader callers. Flagged for user ratification — not a blocker.
9. **Spine annotate removal** — falcon.yaml lines 303–305: the spine op no longer stamps chunk/isLastChunk/findingsInChunk (comment explains it would break native key order). Chunk framing stamps all three correctly — recomputation shows 0 findingsInChunk mismatches and correct key order, so nothing is lost.

## Gaps / Risks

- **GAP (Success Criterion 8, documentation).** crowdstrike-falcon.yaml booked-deltas header (lines 21–47) is stale. It still asserts, as open/accepted deltas:
  - line 31–34 "No chunk framing … a host is always one record (chunk 0). findingsInChunk is likewise omitted" — **now false**, chunk framing is implemented and findingsInChunk is stamped.
  - line 35–37 "findings pass through verbatim INCLUDING apps/suppression_info (native strips them)" — **now false**, strip removes all three.
  - line 38–39 "remediation.entities is not sorted" — **now false**, sort applied.
  - line 21–28 record-contract prose also describes the pre-chunk single-record shape / omitted findingsInChunk.
  The contract explicitly requires "booked-deltas header updated (closed vs the one remaining optional dedupe-by-id)." A future reader of the YAML will be actively misled about what the integration does. Low blast radius (comment only), but it is a named criterion left unmet.

- **Note (not a gap).** Exact-multiple-of-cap off-by-one is not exercised by live data (no host at an exact multiple of 2000), by design covered only in unit tests (1040/1064). Acceptable and documented.

- **Note (not a gap).** Criterion 4's literal "assets == 357" is superseded by the recorded drift policy (A7); intent (phantom dropped) is verified independently.

## Recommendations

1. **Before close:** update the crowdstrike-falcon.yaml booked-deltas header (lines 21–47) — move chunk framing, apps/suppression_info strip, and remediation.entities sort from "booked deltas" to "closed by this work," and fix the record-contract prose (findingsInChunk now emitted; multi-record chunking now real). Leave only the genuinely-remaining items (month segmentation, recovery ladder, cursor_recovery phrasing) and the booked dedupe-by-id. This is the one outstanding Success Criterion.
2. Get user ratification on the reject-vs-clamp chunk.max deviation (already flagged by the orchestrator).
3. No code/test/schema changes required — implementation is sound.

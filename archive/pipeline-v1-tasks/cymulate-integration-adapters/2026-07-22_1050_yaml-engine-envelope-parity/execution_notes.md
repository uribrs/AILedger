# Execution Notes — envelope parity (phase 2)

Live-parity policy (orchestrator): full engine + adapter gates after every feature; live
vendor parity reruns only after B (headline: phantom asset drop) and after A (record-for-record),
plus the magic schema-validation test after each YAML edit (cheap, no vendor). C/D effects are
per-element byte content fully covered by unit tests and exercised by the final post-A run.

## Feature B (require_key)
Implemented as `merge_into.require_key: true` (default false, additive). Guard sits right after
key = CanonicalKey(GetByPath(record, anchor.KeyPath)) in the record-level (non-array) anchor branch
of both ApplyEmbed and ApplyGrouped in MergeEnrichment.cs: `if (plan.RequireKey &&
string.IsNullOrEmpty(key)) return false;` — drops null AND empty-string keys, flag-off is
byte-identical (falls through to existing unmatched path). Loader rejects require_key combined with
an array-anchor `on` expression. Schema gate updated additively. 8 new tests in MergeIntoTests.cs;
engine gate 668/668 green (660 baseline + 8).

### Feature B — live parity (run 20260722-104513-yaml)
require_key consumed in crowdstrike-falcon.yaml. Findings lane: null_aid=0 (was 1). The
non-32-hex unmanaged host (composite id "…_ASCeLr0LF…") is ABSENT from the findings lane
(dropped) but PRESENT in the assets lane (get_devices scrolls all hosts — native CollectAssets
parity). Phantom asset closed. Absolute counts (358 spine records / 39,387 findings vs native
baseline 357 / 35,651) reflect ~10% live tenant growth over the day since the baseline, not a
regression. Gates after B: engine 668/668, adapter 166/166, magic schema 7/7.

## Feature C (strip)
Added `merge_into.strip: [key, ...]` (embed mode only, default empty) — removes named top-level keys from each embedded element after clone/from-extraction. Single choke point: MergeEnrichment.EmbedValue (EmbedGroup routes through it per-member → group free). Missing key / non-object payload = no-op. Rejected at load with mode: collect. Schema (adapters copy) + loader additive; W-B untouched. Gate 676/676 (668 + 8; one unrelated AnnotateAndConstTests parallel-run flake, clean on re-run).

### Feature D (sort)
Added `merge_into.sort: {path, order}` (embed mode only, opt-in): orders a nested array inside each embedded element by OrderBy(element.ToJsonString(), StringComparer.Ordinal) when order: serialized_ordinal and Count>=2 — native remediation.entities parity. In MergeEnrichment.EmbedValue:180 after strip, composes with strip, per-member under group. Loader rejects sort+collect and unknown order. Schema (adapters). 11 new tests; gate 687/687.
NOTE for code review: ApplySort uses array.Clear()+re-Add() on live GetByPath references (worker verified Clear() detaches parents) rather than building a fresh JsonArray — flag for reviewer scrutiny.

## Feature A (chunk)
Post-plan 1→N record framing in MergeEnrichmentSink.PublishBatchAsync, right after the merge-plan loop and before the empty-check — reproduces native chunk framing exactly: floor(n/cap)+1 records, non-terminal chunks of exactly cap elements, ALWAYS one terminal record whose remainder is empty on an exact multiple of cap (incl. n=0). Native key order (aid, chunk, isLastChunk, findingsInChunk, host, findings) rebuilt fresh per chunk. Embed/group mode only; rejected with mode: collect. DEVIATION from native "clamp": loader REJECTS out-of-range max (repo fail-fast) rather than clamping; MergePlan.Create still clamps defensively for non-loader callers. Engine gate 699/699 (687+12).
ORCHESTRATOR FLAG for user: the reject-vs-clamp on max is a deliberate deviation from native — ratify or ask for clamp-to-match-native.

## Final live parity — record-for-record (run 20260722-111632-yaml vs native baseline 20260721-125550)
All four consumed in crowdstrike-falcon.yaml (require_key, strip, sort, chunk); spine annotate removed (chunk framing owns chunk/isLastChunk/findingsInChunk + native key order).
- KEY ORDER: 100% — every YAML record (366) and native record (365) is exactly (aid, chunk, isLastChunk, findingsInChunk, host, findings). Single variant, both sides.
- CHUNK FRAMING: 4 multichunk hosts both sides. Validated in the wild: non-terminal chunks == 2000, exactly one isLastChunk:true terminal carrying the remainder, findingsInChunk==len(findings), chunk 0-based. E.g. aid 5147d649: [(0,F,2000),(1,F,2000),(2,F,2000),(3,T,645)]. 330 zero-finding hosts each emit one {chunk:0,isLastChunk:true,findingsInChunk:0,findings:[]}. Exact-multiple off-by-one not hit by live data (no host at an exact multiple of 2000) but covered by unit tests.
- STRIP: 0 apps/suppression_info/host_info violations across all findings both sides.
- SORT: remediation.entities ordinal-sorted 15547/15547 (yaml) — 100%.
- DELTAS = live drift only: yaml 358 hosts / 39,387 findings vs native 357 / 35,651 (~10% tenant growth over the ~day between runs); host overlap 354 common. NOT structural.
VERDICT: envelope is native-equivalent record-for-record; residual differences are live tenant drift, not shape.
Gates: engine 699/699, adapter 166/166, magic schema-validation 7/7. Magic CatalogBaseline fails ONLY on pre-existing defender-vm/qualys body_cursor drift (unchanged by this work; falcon absent from the failure).

## Review dispositions
Verifier (review/verifier-1.md): PASS-WITH-GAPS. Gates re-run green (engine 699, adapter 166, magic schema 7); parity recomputed and confirmed; flag-off invariance + schema byte-identity confirmed. One gap: falcon YAML booked-deltas header stale — FIXED (record contract rewritten to the native chunked shape; chunk/strip/sort moved from "booked" to "closed by 2026-07-22 batch"; re-validated schema 7/7).

Code review (review/code-reviewer-1.md): ship-able. 0 Critical/High/Medium, 3 Low + 4 test-coverage notes. Merge suite 101/0. The three flagged-risk spots (sort Clear()+re-add, chunk 1→N, flag-off invariance) all verified safe. Dispositions:
- F1 (Low) — chunk expansion precedes onPublished, so `counts: <name>: records` would count chunk records not logical hosts, and a zero-finding host adds 1 via its empty terminal. NOT applicable to Falcon (uses counts: findings: "findings[]" — array-path, sums correctly; assets counted by unchunked get_devices). BOOKED: latent semantics for a future `counts: …: records`+chunk consumer; pin with a test when one appears.
- F2/F3 (Low) — RebuildChunkRecord preserves only the single leading source key (bakes the aid-first shape); stacked chunk plans on one target share default field names. Reviewer: neither needs a code change. BOOKED.
- Booked (out of scope): per-batch dedupe-by-id (native seenFindingIds) — noted in the YAML header.
DEVIATION for user ratification: loader rejects out-of-range chunk.max instead of clamping like native (repo fail-fast convention; MergePlan.Create still clamps for non-loader callers). No YAML uses out-of-range.

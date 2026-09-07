# Verifier-1 — G1 list-extraction flatten (B1)

Independent adversarial verification. Built + ran the suite myself; inspected every changed site and the new tests' mocks/profiles.

## Criterion 1 — build clean — PASS
`dotnet test CollectorBase.slnx` compiled all projects (CollectorExecutor, Strategies, both test libs) with no errors before running. No warnings surfaced as failures.

## Criterion 2 — suite green + the three required behaviors — PASS
`dotnet test CollectorBase.slnx` → **Failed: 0, Passed: 77, Skipped: 0, Total: 77** (≥77 met; matches notes' 75 prior + 2 new B1 tests).
- (a) capture_list crossing a repeated parent yields flattened ids and its for_each runs → `CaptureList_CrossingRepeatedParent_DrivesForEach_AndAccumulateDedupes` (:1119) and `CaptureList_CrossingRepeatedParent_XmlHostList_DrivesForEach` (:1138). Both PASS.
- (b) accumulate_list across iterations + dedupe → same JSON test asserts `qid=SHARED` requested exactly once (:1135). PASS.
- (c) single-node nav unchanged → `Flatten_IsRecordsOnly_SharedNavStillSingleNode` (:1092): `At`/`StringAt` on `hosts.id` return null (no flatten), normal `a.b` scalar nav intact. PASS.

## Criterion 3 — Qualys findings-shape harness emits across hosts (was 0) — PASS
Two tests cover it:
- `Flatten_QualysDetections_AcrossMultipleHosts_EmitsAll` (:1104) — `QualysDetectionsProfile` records_path `HOST_LIST_OUTPUT.RESPONSE.HOST_LIST.HOST.DETECTION_LIST.DETECTION` crossing repeated HOST → asserts 4 (2 hosts × 2 detections).
- `CaptureList_CrossingRepeatedParent_XmlHostList_DrivesForEach` (:1138) — closer to the literal contract: capture `host_ids` at `…HOST_LIST.HOST.ID` (crosses repeated HOST) → for_each batch → detections → 4. Without flatten, capture yields empty → for_each 0 → 0.

## Criterion 4 — no regression — PASS
Full suite green incl. cursor/cursor_watermark/next_url, capture-scalar, XML single-vs-array, envelope/passthrough, records-path flatten.

## Criterion 5 — task-dir note — PASS
`execution_notes.md` records the corrected axis (all list-extractions flatten; single-node nav stays literal), the two rerouted sites (capture_list :176/178, accumulate_list :191/194), the drain_path decision, and the two nit resolutions. `decisions.md` + `assumptions.md` (A1 drain_path, A2 two nits, A3 single-node nav) corroborate.

## Scope of change — VERIFIED MINIMAL
Only `capture_list` (CollectorExecutorStepHelpers.cs:178) and `accumulate_list` (:194) were rerouted to `JsonNav.ListAtFlattened`. `JsonNav.At`/`StringAt`/`ListAt` are textually unchanged. `drain_path` at **CollectorExecutorRunner.cs:654** (note: the contract's "Runner.cs:654" is the wrong filename — the actual site is CollectorExecutorRunner.cs:654; content matches) still uses non-flattening `ListAt`. The other two `ListAtFlattened` callsites (ExtractIds :156, Map :169) are from the prior pass, not touched here. ApplyCaptures' capture-scalar branch (:164) still uses `At` — correct.

## Trivial-pass analysis — tests are NON-TRIVIAL
- Mocks (`/grp/list` :213, `/grp/detail` :219) return genuinely **nested** arrays: groups[0].items=[i1,i2], groups[1].items=[i3]; blocks[].qs[].qid. With non-flatten `At`, `groups.items.id` descends `groups` (an array, not object) → returns null → empty → for_each 0 → 0 records. The shape exercises exactly the path that let B1 escape — not a flat top-level array.
- **Does JSON count 6 prove both flatten AND dedupe?** Yes. capture flatten → [i1,i2,i3] → step2 emits 3 detail records; accumulate distinct qids = [Q1,SHARED,Q3] → step3 emits 3 kb records → 6. If capture didn't flatten → 0. If dedupe broke → SHARED appears twice (i1+i2) → 4 raw qids → 7 records AND the `qid=SHARED requested once` assertion fails. So 6 + the SHARED-once assertion jointly pin both behaviors.
- **Does XML count 4 prove host_ids flattened?** Yes. host_ids crosses repeated HOST → [1,2]; empty without flatten → for_each 0 → 0. 4 vs 0 is decisive.
- No way to pass trivially: the mock data is the discriminator, and the negative cases (0 / 7) are distinct from the asserted values.

## Notes / nits
- Contract said `Runner.cs:654`; the real drain site is `CollectorExecutorRunner.cs:654`. Behavior correct; only the filename in the contract was off. Not a defect in the work.
- "75 prior + 2 new" framing: the flatten-records tests (:1067,:1085,:1092,:1104) appear to be prior-pass; the two B1-specific additions are the CaptureList tests (:1119,:1138). Total 77 either way; no impact.
- drain_path deliberately left on `ListAt` (A1) — validated: shipped poll_and_drain paths are flat chunk-id arrays, no repeated-parent crossing, no consumer needs flatten. Correct call (avoids speculative change).

## VERDICT: PASS
All five criteria satisfied with evidence. Change is minimal and correctly scoped; the new tests genuinely reproduce the B1 capture→for_each shape and would fail (0 / 7) if either flatten or dedupe regressed. No regression risk observed.

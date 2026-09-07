# Code Review — Falcon findings assets stage

Scope: the assets-stage addition to the Falcon findings flow and its checkpoint plumbing.
Files reviewed:
- `Collectors/FalconCollector/Flows/Findings/FalconFindingsAssetsStage.cs` (new)
- `Collectors/FalconCollector/Flows/Findings/FalconFindingsStage.cs` (new)
- `Collectors/FalconCollector/Flows/Findings/AssetIdsFetcher.cs`
- `Collectors/FalconCollector/Flows/Findings/FalconFindingsFlow.cs`
- `Collectors/FalconCollector/Flows/Findings/FalconFindingsCheckpointWriter.cs`
- `Collectors/FalconCollector/Recovery/FalconCheckpointState.cs`
- `Collectors/FalconCollector/Recovery/FalconCheckpointHelper.cs`
- `UnitTests/.../FalconCollectorTests.cs`

Overall: the design is sound. The assets stage runs as a fully-awaited sequential pass before the
background AID channel producer is ever created, so the "must not run inside the AID channel producer"
constraint is met (`FalconFindingsFlow.cs:106-117` completes before the segment `foreach` at line 172).
Resume relies on the `after` cursor (exclusive), with a watermark + boundary-ID fallback mirroring the
idiomatic `FalconAssetsFlow`. No critical defects found. The findings below are ranked.

---

## High

### H1. Boundary tie-break IDs are dropped when a page has no parseable `last_seen_timestamp` → potential duplicate host rows on a cursor-expiry resume
`AssetIdsFetcher.cs:111,171-176`

`pageBoundaryIdsSnapshot` is initialized empty per page and only populated inside
`if (pageMaxLastSeen.HasValue)`. `pageMaxLastSeen` is reset to `null` every page (line 117) and is only
set from records that have a parseable `last_seen_timestamp` (line 135-146). If a hosts page returns
records but none carries a parseable `last_seen_timestamp`, the yielded page has
`MaxLastSeenUtc == null` and `MaxLastSeenIds == []`.

In the assets stage (`FalconFindingsAssetsStage.cs:88-93`) the watermark is then *not* advanced
(`if (assetPage.MaxLastSeenUtc.HasValue)`), so `watermarkUtc` keeps the prior page's value while
`boundaryIds` becomes `[]`. The checkpoint persists `AssetsStageWatermarkUtc = <prior>` with
`AssetsStageWatermarkIds = []`. If the cursor later 404s and the fetcher falls back to the watermark,
the boundary records at that exact second are no longer de-duplicated (the seed is empty), so they are
re-emitted → duplicate host rows in `assets_*.json`.

This is the same hazard `FalconAssetsFlow` explicitly warns about (`FalconAssetsFlow.cs:109-114`).
Probability is low for the hosts endpoint (hosts normally have `last_seen_timestamp`), but the assets
stage publishes raw host rows, so a duplicate here is a real data defect, not just an AID dedup miss.

Fix: when `MaxLastSeenUtc` is null for a page, carry forward the previous boundary IDs alongside the
carried-forward watermark instead of overwriting with `[]`. Concretely, in the assets stage only
overwrite `watermarkIds`/`watermarkUtc` together when `assetPage.MaxLastSeenUtc.HasValue`; otherwise
leave both untouched (today `watermarkUtc` is preserved but `boundaryIds` is not). Mirror this in the
checkpoint write so the persisted `AssetsStageWatermarkIds` stays consistent with the persisted
`AssetsStageWatermarkUtc`.

---

## Medium

### M1. Assets-stage `page++` only on publishable pages can leave checkpoint stranded with no progress while paging entirely-deduped pages
`FalconFindingsAssetsStage.cs:97-106,108`

When a resumed page yields only boundary-duplicate rows (`rows.Count == 0`) and is not the final page,
the loop `continue`s without publishing, without incrementing `page`, and **without writing a
checkpoint** — the new `after` cursor obtained from that page is discarded (the local `afterToken` is
updated at line 87 but only persisted via `OnAssetsStagePagePublished`, which is skipped). If the
process crashes after consuming several such zero-row pages, resume restarts from the *old* persisted
cursor and re-walks all those pages. This is wasted work, not data loss (the cursor is exclusive, the
duplicate rows are filtered again), so it is Medium, not High. Worth a comment acknowledging it, or
persist a cursor-only checkpoint on a zero-row non-final page.

### M2. `OnAssetsStagePagePublished` advances the SDK item counter with assets, double-meaning `ProcessedItems`
`FalconFindingsCheckpointWriter.cs:216`; test `FalconCollectorTests.cs` (`ProcessedItems.Should().Be(5)`)

`progressContext.AdvancePage(itemsInBatch: publishResult.RecordCount, findingsInBatch: 0)` makes the
findings run's `ProcessedItems` now include host records (2 assets) plus findings (3) = 5, while
`ProcessedFindings` stays 3. The test was updated to assert 5, so this is intentional, but it conflates
two entity types in one progress counter on a flow named "findings". On a findings-stage resume,
`RestoreProgress(pagesDone, totalFindings, totalFindings)` (`FalconFindingsFlow.cs:149`) restores
`ProcessedItems` from `totalFindings` only — i.e. the assets contribution is silently dropped after a
resume, so the final `ProcessedItems` is not even stable across a crash/resume boundary. The dedicated
`TotalAssetsCollected` field already tracks assets separately and correctly. Recommend either keep
assets out of `ProcessedItems` (pass `itemsInBatch: 0` for the assets stage, matching the
findings-only intent) or document that `ProcessedItems` is a cross-stage sum that is not resume-stable.
Either way the current behavior is inconsistent between fresh-run and resume.

### M3. `Page` field overloaded between stages with a load-time invariant the assets stage barely satisfies
`FalconFindingsCheckpointWriter.cs:165-171` (comment), `FalconFindingsCheckpointState`

The assets-stage checkpoint sets `Page = assetsStagePage` purely to satisfy a "Page must be >= 1"
load invariant, while resume actually reads `AssetsStagePage`. This is a latent trap: `assetsStagePage`
starts at the resumed value or 0 and is incremented to >= 1 before the first publish, so in practice
`Page >= 1` holds — but it is coincidental, not enforced. If a future change publishes an assets
checkpoint at page 0 (e.g. a cursor-only checkpoint per M1), `TryLoadFindingsState` could reject it.
Recommend asserting/clamping `Page = Math.Max(1, assetsStagePage)` at the write site, or relaxing the
load invariant, so the intent is explicit rather than incidental.

---

## Low

### L1. `pageBoundaryIdsSnapshot` allocates a fresh `List` every page even when unused
`AssetIdsFetcher.cs:111`

`List<string> pageBoundaryIdsSnapshot = new();` is allocated on every page including for the AID
pre-pass that never reads `MaxLastSeenIds`. Negligible, but it is one allocation per page on the hot
path; could be left null and assigned only when `pageMaxLastSeen.HasValue`.

### L2. Redundant dry-run early return on the unfiltered path
`FalconFindingsFlow.cs:358-361`

The filtered branch already `return 0` at line 215 in dry-run; the new `if (segmentConfig.IsDryRun)
return totalFindings;` only affects the unfiltered dry-run path (stops iterating further month
segments after the one-page probe). Behavior is fine and matches the new
`ProcessAsync_Findings_DryRun...` test, but the placement (after the if/else, applying to both
branches) makes the filtered-path portion dead. Minor; a comment would help.

### L3. `captureHostRows` returns rows for *all* resource objects, including non-objects skipped for AID
`AssetIdsFetcher.cs:128-131,157`

`hostRows?.Add(...)` at line 157 runs for every resource that reached that point, which is after the
`ValueKind != Object` `continue` (line 128-131), so non-object resources are correctly excluded. Good
— but note the row capture intentionally does NOT apply the AID de-dup (`aidsInPage`) or the
watermark-boundary skip beyond the `continue` at line 141, so a host appearing twice in one page (same
`device_id`/`aid`) would be emitted twice as a row. Falcon hosts pages are not expected to contain
intra-page duplicates, so this is acceptable, but it is a different dedup contract than the AID list and
worth a one-line comment.

---

## Tests

No tautological or wrong assertions found. The new tests are meaningful:
- `FindingsCheckpoint_RoundTripsAssetsStageFields_AndLegacyDefaultsToFindings` exercises the real
  legacy-null-stage path and a full round-trip — good coverage for H-adjacent backward-compat.
- `ResumeAsync_Findings_WithFindingsStageCheckpoint_SkipsAssetsStage` asserts `hostPageCalls == 0`,
  which is a genuine behavioral guard against re-publishing assets on a findings resume (the core
  duplicate-data concern).
- `ResumeAsync_Findings_WithAssetsStageCheckpoint_ContinuesAssetsThenRunsFindings` asserts the
  persisted cursor is actually sent (`resumedAssetsCursorSeen`) and that file numbering continues at
  `assets_000002.json` — correctly verifies no page-number off-by-one on resume.

Gap (not a defect in existing tests): none of the tests cover H1 (a page with no
`last_seen_timestamp` followed by a cursor-expiry 404), nor M1 (a zero-publishable-row page mid-resume).
If H1/M1 are addressed, add a test that 404s the assets cursor after a timestamp-less page and asserts
no duplicate host rows.

---

## Lens summary
- Correctness / resume / duplicate-data: one real (low-probability) duplicate hazard (H1); resume
  cursor handling and page numbering otherwise correct; no off-by-one.
- Concurrency: clean — assets stage fully precedes the background AID producer.
- Async-iterator / stream disposal: correct (`await using` on the streamed response; `ToAsyncEnumerable`
  honors cancellation).
- Data structures / complexity: fine; minor per-page allocation (L1).
- Idiomatic C#/.NET: consistent with `FalconAssetsFlow`; checkpoint-before-AdvancePage ordering honored.
- Error handling: cursor-expiry fallback and best-effort JSON deserialize are handled; event emission
  is try/catch-guarded.
- Maintainability: `Page` overloading (M3) and `ProcessedItems` cross-stage meaning (M2) are the main
  readability/contract risks.

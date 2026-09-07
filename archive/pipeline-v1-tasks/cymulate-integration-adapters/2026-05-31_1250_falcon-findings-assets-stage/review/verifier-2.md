# Verifier-2 (post-repair re-verification)

## Trigger
verifier-1 returned PASS WITH GAPS. The material gap was **criterion 5** (resume `Stage="assets"`
continues the assets stage) — only a checkpoint data round-trip existed, no behavioral test. On the
dup-data/resume branch this is the path most worth covering.

## Repair
Added behavioral test `ResumeAsync_Findings_WithAssetsStageCheckpoint_ContinuesAssetsThenRunsFindings`:
- Resumes from an assets-stage checkpoint (`Stage="assets"`, `AssetsStageCompleted=false`,
  `AssetsStagePage=1`, `AssetsStageAfterToken="assets-after"`).
- Asserts the resumed hosts request carries the persisted cursor (`after=assets-after`), the assets
  stage publishes the next page with continued numbering (`assets_000002.json`), and the findings
  stage then runs fresh (vulnerabilities endpoint queried).

## Result
`dotnet test` FalconCollector.Test: **63 passed, 0 failed** (~4s).

## Criterion status after repair
1. Builds + tests pass — PASS
2. Filtered run publishes assets + findings; findings unchanged — PASS
3. No-filter publishes all hosts as assets — PASS
4. Resume `Stage="findings"` + legacy skip assets — PASS (behavioral test for `Stage="findings"`;
   legacy null-Stage takes the identical code path via `?? Findings` and is covered by the
   checkpoint data-layer test — behaviorally equivalent)
5. Resume `Stage="assets"` continues assets then runs findings — **PASS (now behaviorally tested)**
6. Constraint adherence — PASS
7. Backward-compatible checkpoints — PASS

## Remaining minor notes (non-blocking, accepted)
- Zero-host assets stage: implicitly exercised by the no-filter findings tests whose hosts endpoint
  returns `{}` (no assets batch, no crash).
- Resume `Stage="assets"` with `AssetsStageCompleted=true` (skip assets) is covered by the
  `shouldRunAssetsStage` gate and the data-layer test; not separately behaviorally tested.
- Double hosts fetch on the filtered path is an accepted cost of the operator-chosen independent stage.

## Verdict: PASS

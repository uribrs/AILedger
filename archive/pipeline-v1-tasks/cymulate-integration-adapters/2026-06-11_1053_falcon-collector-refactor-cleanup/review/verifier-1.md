# Verifier-1 — FalconCollector refactor / cleanup

Verdict basis: refactor changes were verified against **HEAD** (the working-tree diff), not
`master`. The branch already carries the prior "Falcon redesign" commits; those are out of scope.
`git diff HEAD` over the FalconCollector dir = 8 files changed, 763 insertions / 1777 deletions,
plus 12 new files (DTOs, SharedFlows helpers, Recovery split, Assets/Findings helpers).

---

## Criterion 1 — No class >400 lines where reasonably possible (best-effort)

**Verdict: PARTIAL (defensible, with one wart).**

Files still >400 lines after refactor:
- `FalconFindingsFlow.cs` — 860 (was 961)
- `FalconAssetsFlow.cs` — 666 (was 683)
- `FalconSpotlightVulnerabilitiesRunner.cs` — 527 (was 683)
- `FalconCollectorConfigurationBuilder.cs` — **488 (was 407 — GREW)**
- `FalconCollector.cs` — 467 (was 615)
- `AssetIdsFetcher.cs` — 448 (was 481)
- `FQLParser.cs` — 725 (explicitly OUT OF SCOPE, untouched — correctly ignored)

Assessment of the two large ones (860, 666): the workers stopped because the residual mass is a
single cohesive loop carrying interleaved mutable watermark/cursor/lane state across iterations
(findings lane loop; assets per-page lifecycle). The execution notes argue extraction there would
thread 4-6 mutable refs/closures and risk the documented invariants for marginal line savings.
This matches assumption A4 (400 is best-effort; cohesion wins) and the A2 risk warning. The
*methods* were genuinely decomposed: `FalconAssetsFlow.CollectAsync` went 410 lines → ~44 lines of
orchestration; `FalconCollectorConfigurationBuilder.Build` went ~230 → ~36. So the "god method"
problem — the real target — is fixed even where file totals stay high. Defensible.

The wart: `FalconCollectorConfigurationBuilder.cs` GREW from 407 → 488, i.e. it crossed *further*
past the ceiling. Justification in notes: per-method XML docs + a carrier record struct, while the
monolithic `Build` was broken into 4 named seams. This is a real readability win (the 230-line
method is gone) but it is the one file that moved the wrong way on the raw metric. Net judgment:
acceptable trade (method cohesion up, one file +81 lines of docs/structure), but it should be
called out rather than glossed.

---

## Criterion 2 — Param farms (15-18) replaced by house-style DTOs; 5-tuple → named record

**Verdict: PARTIAL — two named worst-offenders NOT fixed.**

Fixed (verified):
- `FalconFindingsCheckpointWriter.WriteCursorlessYieldCheckpoint`: **18 → 10 params**, two of which
  are the new DTOs (`FindingsAidScopedResumeState`, `FindingsSpotlightYieldState`).
- `FalconFindingsCheckpointWriter.OnPagePublished`: **15 → 7 params** (now takes the two DTOs).
- 5-tuple return on `SpotlightVulnerabilitiesRunner.RunAsync` → `SpotlightVulnerabilitiesRunResult`
  (positional sealed record + XML docs). Confirmed at all 3 return sites.

DTOs match house style exactly: positional `sealed record`, XML `<param>` doc per field, namespace
`...Dtos` (same as the reference `FindingsFlowRunConfig`). `FindingsAidScopedResumeState` adds a
static `Empty`. Good.

**NOT fixed (gap):**
- `FalconSpotlightVulnerabilitiesRunner.RunAsync` — contract Context lists it as "16 + 5-tuple
  return." The 5-tuple is fixed, but the **15 positional input params remain** (progressContext,
  baseUrl, filter, aidBatch, pagesDone, totalFindings, startAfterToken, startWatermarkUtc,
  startWatermarkIds, config, enrichedPagesDirectory, ct, onPagePublished, startFloorUtc,
  laneStatus). Byte-identical to HEAD on the input side.
- `FalconFindingsFlow.RunSpotlightLaneAsync` — contract Context lists it as a 17-param worst
  offender (FalconFindingsFlow.cs:448). It **still has 17 positional params** (def at line 468).
  Unchanged from HEAD.

The verifier prompt explicitly named both of these as methods to "confirm are actually fixed."
They are not. WB1's notes are silent on them — the worker fixed the writer methods + the tuple and
treated the lane/run signatures as the live run-loop threading that the DTOs deliberately do not
cover. That is an arguable scope call, but it leaves 2 of the 4 enumerated worst-offenders at
full param-farm width. This is the most material gap against the literal Success Criteria.

---

## Criterion 3 — Helper classes extracted; listed duplications removed

**Verdict: PASS.**

Spot-checked every duplication named in the contract Context:
- JSON readers (Spotlight / AssetIdsFetcher / FalconAssetsFlow) → `FalconJson` adopted (5 / 2 / 3
  call sites). No local `ReadString`/`TryReadUtcDateTime`/`TryReadAnyId`/`TryReadStringProperty`
  remain in those three. (`AidExtractor.ReadString` remains — not in the named set, pre-existing,
  out of scope.)
- Event-emit try/catch ×7 → `FalconCollectorEvents.ReportBatchProduced/ReportCheckpointAdvanced`
  (writer ×5, assets ×3). Zero inline `catch … LogDebug` emit blocks remain.
- `_checkpoint.*` metadata keys → `FalconCheckpointMetadataKeys` (single home; local consts deleted
  from writer + assets).
- `FalconUtcTimestampFormat` dup → `FalconCursorPagination.UtcTimestampFormat` (AssetIdsFetcher +
  Spotlight rewired). `FalconHostFilters` keeps its private copy — WA flagged it out of edit scope;
  honest and harmless.
- `SaveState + foreach SetState` loop ×6 → `FalconProgressState.Apply` (writer ×4, assets ×2,
  recovery handlers ×4). Grep for raw `foreach … SetState` loops = **none remain**.
- depth-cap predicate → `FalconCursorPagination.ShouldResetForDepthCap` adopted.

---

## Criterion 4 — Written SharedFlows analysis + cross-flow helpers homed there

**Verdict: PASS.**

Written analysis exists in `execution_notes.md` (WA sections c + d): states the bar ("stateless,
cross-flow, Falcon-vendor logic"), lists what moved in (FalconJson, FalconCursorPagination,
FalconCheckpointMetadataKeys, FalconCollectorEvents, FalconProgressState) and what stayed out (FQL
parse helpers, per-flow filter builders) with reasons. The "small helpers, NOT a unified pagination
engine" decision directly addresses the A2 risk and is well argued (the 4 cursor loops differ at
the reset point; a single engine would need 4-6 mutable refs). New helper files all live under
`Flows/SharedFlows/` and clear the bar. Genuinely cross-flow only.

---

## Criterion 5 — Build green (net8.0) + tests pass + no behavioral change

**Verdict: PASS (per established facts + behavioral spot-checks).**

Build/test green and 179/179 are orchestrator-established. My job was behavioral drift not caught
by tests. Checked the highest-risk surfaces:

- **Public/SDK surface (hard constraint): UNCHANGED.** `FalconCollector` keeps
  `CollectAssetsAsync`, `CollectFindingsAsync`, `ProcessAsync`, `DisposeAsync`, `CanResumeFrom`,
  `ResumeAsync` — no signature touched. The only FalconCollector.cs change is moving 4 private
  recovery methods into `FalconCollectorRecoveryHandlers` and rewiring the delegate call sites.
  All 12 Continue/Decline reason strings preserved byte-for-byte in the moved file.
- **FalconCheckpointHelper (hard constraint): UNCHANGED public surface.** All 6 public statics are
  thin one-line delegators with identical signatures; `TryLoad*` still wrap the cores in
  `RecoveryStateHelper.TryLoad`.
- **WB2 round-trip risk: PRESERVED.** Save dictionary key set is **identical** (34 keys, exact
  literal match old vs new `FalconCheckpointKeys`). `"O"` round-trip date format used in exactly 14
  places, same as HEAD (correctly NOT swapped to the `"yyyy-MM-ddTHH:mm:ssZ"` shared const). Legacy
  `collectedAids` 1,000,000-char OOM guard → return-false preserved. Best-effort vs return-false
  deserialize split preserved: lastWatermarkIds / assetsStageWatermarkIds / spotlightCompletedLanes
  = LogWarning "(ignored)"; pendingAids / collectedAids = return false. Dual-write of `pendingAids`
  AND legacy `collectedAids` kept.
- **WB1 status-stage yield risk: PRESERVED.** The status-stage site (FalconFindingsFlow.cs:339)
  constructs `FindingsSpotlightYieldState(NextStatus, NextLaneIndex, …)` explicitly — matches the
  old `spotlightStatus: completion.NextStatus / spotlightLaneIndex: completion.NextLaneIndex`. The
  volume-yield sites use `lane.ToYieldState()` which emits `CurrentStatus`/`LaneIndex` — matches the
  old `lane.CurrentStatus`/`lane.LaneIndex`. The "advance to next lane" persisted semantics are
  intact. AID producer keeps `Channel.CreateBounded(1)` SingleReader/SingleWriter.
- **WB4: PRESERVED.** `BuildAssetsFilter` widened `private static` → `internal static` (benign).
  Stream disposed exactly once per path: dry-run path disposes then early-returns; normal path
  hands the stream to `ProcessPageAsync` which owns the single `await using`.
- **Files mandated untouched: UNTOUCHED.** `git diff HEAD` = empty for
  `FalconCheckpointState.cs`, `FalconFindingsAssetsStage.cs`, `FalconCollectorFlowRunner.cs`,
  `FalconResumeRunner.cs`.
- **Skill docs:** `ai/skills/collector-flow-patterns` + `collector-recovery` NOT modified by this
  refactor (`git diff HEAD` empty). Correct call — pure helper extraction with zero behavioral
  change leaves the documented flow/recovery architecture intact; no doc update was warranted.

No behavioral drift found.

---

## Gaps / risks, ranked by materiality

1. **[Material] Two named worst-offender param farms not reduced.**
   `FalconSpotlightVulnerabilitiesRunner.RunAsync` (15 input params) and
   `FalconFindingsFlow.RunSpotlightLaneAsync` (17 params) still carry full param farms. The 5-tuple
   *return* on RunAsync was fixed, but the contract Context and the verifier prompt both name these
   as offenders to fix. This is a literal miss on Criterion 2. Low runtime risk, but it leaves the
   stated goal partially unmet. WB1 did not document a rationale for skipping them.

2. **[Minor] `FalconCollectorConfigurationBuilder.cs` grew past its already-over-400 size**
   (407 → 488). Justified by doc comments + the 4-seam decomposition that killed the 230-line
   `Build`, but it is the one file that moved the wrong way on the raw metric. Cohesion improved;
   line count regressed. Acceptable, worth noting.

3. **[Cosmetic] Residual >400 files** (FindingsFlow 860, AssetsFlow 666, Spotlight 527). Defensible
   per A4/A2 — the god-methods inside were decomposed; the residual is cohesive stateful loops.
   No action needed beyond acknowledging the documented exceptions.

No correctness, behavioral, or surface-contract gaps found.

---

## Overall verdict: **YES, with gaps.**

The request is substantially satisfied. Criteria 3, 4, 5 are clean PASS; the hard constraints
(frozen public surface, FalconCheckpointHelper delegators, untouched files, checkpoint round-trip,
WB1/WB2/WB4 risks) all hold with byte-level evidence; no behavioral drift detected; build + 179
tests green. The one real shortfall is Criterion 2: two of the four explicitly-named worst-offender
param-farm methods (`RunAsync` inputs, `RunSpotlightLaneAsync`) were left at full width. That is a
genuine, documentable gap against the literal Success Criteria — not a behavioral defect, but the
goal is not fully met as written.

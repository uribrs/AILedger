# Verifier-2 — Second pass after repairs (Falcon Spotlight staged yield)

Independent re-verification of the two Major repairs and re-confirmation that they did not
regress the 6 Success Criteria or the preserved-behavior guarantees. Every verdict is grounded
in the working-tree code and the Falcon test project, not in the worker/repair reports. Paths are
absolute; line refs are HEAD working-tree.

---

## Build / Test (observed, this pass)

`dotnet test` on the Falcon test project (auto-pinned net8.0 via csproj):

> **Passed! Failed: 0, Passed: 156, Skipped: 0, Total: 156, Duration: 14s.** (exit 0)

Only `NU1900` warnings (CodeArtifact index unreachable offline) — benign auth noise. Build
succeeded on net8.0. Count is **+3** vs verifier-1 (153 → 156): the three new tests are the
MAJOR-1 cross-lane user-filter regression test and two MAJOR-2 builder cases (no test removed).

---

## MAJOR-1 — cross-lane user-filter resume loses data — REPAIR CONFIRMED, CORRECT

**Fix is real and lives where claimed.**

- `FalconFindingsCheckpointWriter.WriteCursorlessYieldCheckpoint` gained
  `bool resetAidPaginationForNewLane = false`
  (`Flows/Findings/FalconFindingsCheckpointWriter.cs:178`). When true it computes `effective*`
  values that **reset the AID-pagination state** before building `yieldState`
  (`FalconFindingsCheckpointWriter.cs:185-190`): `AssetsAfterToken=null`,
  `AssetsLastSeenWatermarkUtc=null`, `AssetsFilterForFindings=null`,
  `AssetsPaginationCompleted=false`, `PendingAids=new List<string>()`, `PendingAidOffset=0`.
  These feed the persisted state at lines 213-218.
- **STAGE yield passes `true`** — `FalconFindingsFlow.cs:306` (`resetAidPaginationForNewLane: true`),
  raised together with `FalconPlannedYieldKind.StatusStage` (line 308) advancing to the next lane.
- **VOLUME yield uses the default (false)** — the volume call site
  (`FalconFindingsFlow.cs:615-633`) does **not** pass the argument, so it stays `false` and keeps
  the AID progress (`AssetsPaginationCompleted`, `PendingAids`, `PendingAidOffset`, cursor) so the
  **same** lane resumes where it left off. Correct: a volume yield is mid-lane; a stage yield is a
  lane boundary.

**Why the reset is the right fix (control-flow traced).**
On resume the flow restores the instance fields from the checkpoint
(`FalconFindingsFlow.cs:162-167`) and calls
`FillAidBatchesAsync(..., assetsPaginationAlreadyCompleted: _assetsPaginationCompleted, ...)`
(`FalconFindingsFlow.cs:457`). `FillAidBatchesAsync` returns immediately when that flag is true
(`FalconFindingsFlow.cs:663-666`). Pre-fix, lanes after the first inherited
`AssetsPaginationCompleted=true` + drained `PendingAids` → no AID pre-pass → reopen/closed query
Spotlight for nothing. Post-fix, the stage-yield checkpoint persists `false` + empty pending, so
the resumed new lane re-runs the AID pre-pass from the start. `_assetsFilterForFindings` (also
nulled) is rebuilt lazily via `??=` at `FalconFindingsFlow.cs:433`. Confirmed correct.

**Cursor-sanitizer interaction is sound.** `WriteCursorlessYieldCheckpoint` applies the reset
first (builds `yieldState`), then runs
`FalconCheckpointHelper.ApplyCursorTtlForResume(yieldState, ..., fromScheduledWait: true)`
(`FalconFindingsCheckpointWriter.cs:228-229`). The sanitizer (`FalconCursorSanitizer.cs:79-84`)
nulls `AfterToken/AssetsStageAfterToken/AssetsAfterToken` only. The single overlapping field is
`AssetsAfterToken` (already null from the reset → idempotent). The sanitizer does **not** touch
`AssetsPaginationCompleted`, `PendingAids`, `PendingAidOffset`, `AssetsFilterForFindings`,
`AssetsLastSeenWatermarkUtc`, so the reset's effect on those survives intact. No conflict, no
double-clobber, no ordering hazard.

**The MAJOR-1 regression test genuinely covers the bug.**
`FalconStagedSpotlightFlowTests.UserFilterStagedRun_AcrossResumes_EachLaneReScansFullAidSet_AndPublishes`
(`FalconStagedSpotlightFlowTests.cs:492-623`):
- Drives the **user-filter path** (`fql = entity_type:'managed'` → `hasUserFilter == true`),
  i.e. the path the prior suite never exercised.
- Per-fetch assertion that **every** Spotlight request carries `aid:[` (non-empty AID clause)
  — `FalconStagedSpotlightFlowTests.cs:525`.
- Three real invocations (ProcessAsync → ResumeAsync → ResumeAsync), each fed the prior
  invocation's last checkpoint. Asserts inv2 scans **reopen** and inv3 scans **closed**
  (`OnlyContain(s => s == "reopen")` line 591; `== "closed"` line 616) AND that each lane
  **publishes** a `findings_` batch (lines 592-593, 617-618). Final run completes (no yield,
  `Success`, no errors) lines 613-615. Value order open→reopen→closed asserted line 621-622.
- **Would fail without the reset:** the base date is first-of-current-month → a single month
  segment, so the per-segment `else`-branch reset (`FalconFindingsFlow.cs:410-416`) does **not**
  fire on the resumed segment (the lone segment consumes `resumeApplied`). Thus the only thing
  re-scanning the AID set on reopen/closed is the checkpoint-level reset. Pre-fix, inv2 would hit
  `assetsPaginationAlreadyCompleted: true` → zero AIDs → `statuses2` empty →
  `statuses2.Should().NotBeEmpty()` fails and the publish assertion fails. The test's own comment
  (lines 497-499) documents this isolation. This is a true regression guard, not a tautology.

---

## MAJOR-2 — invalid `spotlightStatusStages` not validated — REPAIR CONFIRMED, CORRECT

- `ParseSpotlightStatusStages` now filters parsed tokens against
  `FalconSpotlightLaneFilter.SupportedStatuses` via `.Where(supported.Contains)`
  (`FalconCollectorConfigurationBuilder.cs:363-369`). Unsupported tokens (typos, out-of-scope
  `expired`) are dropped; `.Distinct()` preserves first-seen order.
- **Empty-after-filter → null → keep default.** Returns null when no supported entry survives
  (`FalconCollectorConfigurationBuilder.cs:371`); the caller only overrides the typed default when
  non-null (`FalconCollectorConfigurationBuilder.cs:168-172`). So an all-bad config falls back to
  `DefaultSpotlightStatusStages` (open,reopen,closed), never an empty lane set.
- **No deep ArgumentException / transient-retry path remains.** A bad token can no longer reach
  `FalconSpotlightLaneFilter.ApplyStatus`/`NormalizeStatus`
  (`FalconSpotlightLaneFilter.cs:46-65`, which still throws for unsupported lanes) because the
  builder strips it first. The ~3.5min blind-retry-then-fail path described in code-reviewer-1 #2
  is closed at config-build time. `ApplyStatus` retains its throw as a defensive invariant for
  programmatic misuse — acceptable, and now unreachable from config.
- **Tests added:** `Build_WithUnsupportedSpotlightStatusTokens_DropsThem_KeepsSupported`
  (`open, opne, EXPIRED, reopen` → `open, reopen`, order preserved —
  `FalconCollectorConfigurationBuilderTests.cs:204-218`) and
  `Build_WithAllUnsupportedSpotlightStatusTokens_FallsBackToDefault`
  (`expired, bogus, foo` → default — lines 220-233). Existing empty/whitespace→default
  (lines 192-201) and lowercase/trim (lines 179-188) cases unchanged.

---

## 6 Success Criteria — re-confirmed post-repair (spot-check on what the repairs could disturb)

- **SC1 Config — PASS.** Defaults intact (verifier-1); MAJOR-2 strengthens invalid-stage handling
  exactly per SC1 ("invalid/empty stage config handled per existing builder style") — now
  validated and unit-asserted, an improvement over verifier-1's noted gap. `SpotlightCursorTtl`
  still an internal 2-minute constant.
- **SC2 Filters — PASS, undisturbed.** `FalconSpotlightLaneFilter.ApplyStatus` composition order
  (status after AID, before timestamp) unchanged; the MAJOR-1 user-filter test independently
  confirms each request carries both `aid:[` and `status:'<lane>'`.
- **SC3 Planned yields — PASS, undisturbed.** Volume vs stage boundaries unchanged: volume yield
  (`FalconFindingsFlow.cs:615-641`) still post-publish, cumulative, guarded by
  `isFinalPageOfFinalLane`; stage yield only on `StageYieldDue` with `AllLanesComplete`
  short-circuit (lines 275-315). The repair only added a checkpoint-shaping flag to the stage
  yield; it did not move the yield decision, change the delays (10m/5m), or the unbudgeted
  decision shape.
- **SC4 Resume/checkpoint — PASS, and the case MAJOR-1 patched is now correct.** Cursorless
  yield checkpoint still drops all 3 cursors via `ApplyCursorTtlForResume(fromScheduledWait:true)`;
  lane progression / no-replay still asserted by
  `StagedRun_AcrossResumes_RunsAllLanesInOrder_NoYieldAfterFinalLane`. The user-filter resume now
  re-scans the full AID set per lane (MAJOR-1). 120s gate logic untouched.
- **SC5 Recovery (unchanged + extended) — PASS, untouched.** Neither repair touched
  `FalconRecoveryContinuationBuilder`, `FalconResilienceStrategyFactory`, the 5xx budget
  (5m/15m/30m), or the cursor-expired watermark re-anchor. Verifier-1's traces still hold.
- **SC6 Build + green — PASS.** 156/156 on net8.0 (observed this pass).

## Preserved behavior — re-confirmed

- **Failure behavior unchanged / no test weakened.** The repairs are purely additive: one
  defaulted bool parameter on the checkpoint writer (true only at the stage-yield site) and one
  `.Where` filter + null-guard in config parse. No assertion in any failure-behavior test
  (`FalconResilienceStrategyTests`, `FalconCollectorTests` 5xx/401, `FalconRecoveryContinuationTests`)
  was changed. The 3 new tests are additive regression guards. Verifier-1's "no failure test
  weakened" finding still stands.
- **Output (findings_*.json / assets_*.json), `FalconAssetsFlow`, assets-stage publish** —
  untouched by either repair.
- **Volume-yield AID-progress preservation** — the repair explicitly does NOT reset on the volume
  path, so mid-lane resume still keeps its AID watermark (verified at the volume call site).

---

## Overall verdict: **PASS**

Both Major repairs are real, correctly placed, and behaviorally sound:
- MAJOR-1: the stage-yield checkpoint resets AID-pagination state so each lane re-scans the full
  AID set on resume; the volume yield correctly keeps mid-lane AID progress; the sanitizer
  interaction is idempotent and non-conflicting; the new regression test genuinely exercises the
  user-filter resume path and would fail without the reset.
- MAJOR-2: invalid stage tokens are filtered at config-build, empty result falls back to the
  default, and no deep-ArgumentException/transient-retry path remains for a bad token.

All 6 Success Criteria still hold; preserved-behavior guarantees intact; no failure-behavior test
weakened; Falcon test project green at **156/156** on net8.0.

### Remaining gaps (none blocking)
1. The MAJOR-1 fix correctness for the **multi-month-segment** user-filter case relies on the
   per-segment reset (`FalconFindingsFlow.cs:410-416`) in addition to the checkpoint reset; the
   new test deliberately uses a single-segment base date to isolate the checkpoint reset. The
   multi-segment + user-filter resume path is not separately asserted, but the single-segment test
   plus the unconditional per-segment reset cover the logic; risk is low. Optional hardening only.
2. Code-reviewer-1 #3 (spurious terminal volume yield on an empty final page) and #4 (stacked
   volume+stage waits at a boundary) remain — both Minor, latency-only, unbudgeted, no data loss;
   explicitly accepted by the reviewer and out of scope for these repairs.
3. Implementation remains uncommitted in the working tree (verified against the working tree,
   which is what the host builds). Commit at operator's discretion.

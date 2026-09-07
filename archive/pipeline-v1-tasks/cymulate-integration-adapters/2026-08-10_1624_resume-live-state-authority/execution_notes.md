# Execution Notes

Appended during execution. One section per step from `state.json`. Each entry must carry the citation
(`file:line`, test name, or run) that establishes the change works.

## Wave 2 — object size: archaeology on why Falcon objects reached 485 MB

The operator recalled that findings objects "became huge, hundreds of MiBs" and suspected a regression.
Confirmed, with the commit.

**IntegrationInfra `b6a82f0`, 2026-07-08, "Carry: atomic size-unbounded objects + batch-scoped storage
(1.0.0-preview.3)".** From its own message:

> DELETED the record-too-large and batch-boundary fail-fast guards — nothing in the session path throws on
> record or object size; replaced by `SoftRecordWarningBytes` (24MiB default, log-only, env override
> `PublishThrottling__SoftRecordWarningBytes`) threaded AdapterGlobalDefaults → ThrottlingOptions →
> NdjsonOptions. **`MaxBytesPerBatch` remains part/memory discipline only.**

Three consequences:

1. The bound existed and was removed deliberately, as a 1:1 carry of an adapters-repo changeset. The
   decisions were inherited rather than made, which is how "atomic objects" became "unbounded objects".
2. `MaxBytesPerBatch` has been a misnomer ever since — its name implies an object cap and it is part/memory
   discipline. It misled an external reviewer during this task.
3. The replacement warning is per-RECORD, not per-object. There is no object-size signal anywhere, which is
   why objects reaching 485 MB went unreported for a month.

**Restoring the deleted guards would be wrong.** They were fail-fast — they threw rather than split, so
reinstating them turns oversized objects into failed runs. See decision 18.

**Same commit introduced batch-scoped storage** — the mechanism behind the fifth failure mode. One commit,
two of the five failure modes.

## Wave 2 — fleet-wide test hazard introduced by the Infra seed

Found by I2 when its own new test failed: `Expected staged!.Page to be 12, but found 3`. The fixture seeded
live state at progress-context creation; the host seed then ran during resume setup and overwrote it back
to leg start. **The fixture was wrong, not the code** — production writes no state before resume setup.

General rule, now true and previously not: **after this change, only a publish moves live state past leg
start.** Any test that seeds `AdapterState` before the flow body is silently reverted by the host seed and
proves nothing. Other collectors' suites may contain the same latent pattern.

This is why the full adapters suite is a release gate (decision 21) and Falcon-only coverage is
insufficient.

## Wave 2 — FIFTH FAILURE MODE (found, scoped out, ticket required)

The stop condition in the Wave-2 brief fired. I2 added
`capture.StreamBatches.Should().ContainSingle()` to
`FalconTwoPhaseFindingsTests.Resume_WhenTheHostCounterLeadsTheCheckpointOrdinal_DoesNotDragItBackwards`
and it FAILS.

**Controlled experiment (I2), clean `origin/dev` worktree, strengthened test only, none of our changes:**
```
Failed  Resume_WhenTheHostCounterLeadsTheCheckpointOrdinal_DoesNotDragItBackwards [3 m 30 s]
  Expected capture.StreamBatches to contain a single item ... but the collection is empty.
```
One variable changed — checkpoint `currentPage` 50 → 1: `Passed! Duration: 196 ms`.
50 → nothing published in 3m30s. 1 → published in 196ms.

**Mechanism.** `FalconFindingsFlow.cs:213` calls `BatchScopedStorage.BeginPage(progressContext,
batch.OutputPage)`. Resuming from `lastCompletedOutputPage: 1` the next frozen-list ordinal is 2; the host
restored `CurrentPage = 50`; the guard throws when `pageNumber < CurrentPage`
(`BatchScopedStorage.cs:113`). The throw is retried ~3.5 minutes, swallowed, and **the leg returns
`result.Success == true` having published nothing.** A green run with no data — worse in kind than the
stall, because the stall is visible.

**The clamp never helped here.** `Math.Max` only raises `CurrentPage`; the publish ordinal comes from
frozen-list geometry and is untouched by it. The clamp can only make the guard bite harder. Change 3
(removing it) is neutral for this bug — neither cause nor cure.

### Exposure analysis (orchestrator, verified)

**Latent, not live.** `BeginPage` is gated by `if (config.BatchScopedStorage)`
(`FalconFindingsFlow.cs:211`) and the flag defaults to **false** for Falcon
(`FalconCollectorConfiguration.cs:139`, `FindingsFlowRunConfig.cs:51`). Production publishes flat
`findings_NNNNNN.json` at the run root, so prod runs with it off.

**Falcon-findings-specific.** Two other collectors use batch-scoped storage and both default it to
**true** — `InsightVmCloudCollectorConfiguration.cs:50`, `QualysCollectorConfiguration.cs:54` — yet both
are structurally safe, because each derives its publish page as a dense counter continued from the
checkpoint: `InsightVmCloudFindingsFlow.cs:67,80` (`page = resumeState?.Page ?? 0; pageNumber = page + 1`)
and `QualysFindingsFlow.cs:83,117` (`nextPageNumber = resumeState?.PublishedPageCount ?? 0; ++nextPageNumber`).
The published page therefore always leads `CurrentPage` and can never trail it — exactly what the guard's
error text prescribes. Exposure inverts from what the defaults suggest: the two collectors with the flag ON
are safe; the one with it OFF is the only one that would break if it were turned on.

**Residual risk:** both safe collectors depend on their own page field staying in step with the host's
`current_page`. Divergence reintroduces the same mismatch — a further argument for the `CommitPage`
primitive, which advances both together instead of relying on sixteen hand-rolled copies of the sequence.

### Second-order finding — the incident test never tested the incident

`Resume_WhenTheHostCounterLeadsTheCheckpointOrdinal_DoesNotDragItBackwards` documents the prod-eu
2026-08-09 incident but sets `batchScopedStorage: true`, a configuration production does not run. It has
been standing as the regression witness for a failure it cannot express. This is a large part of why the
6.1.1 clamp could look justified at the time.

### Root cause and the right fix (out of scope here — ticket)

The Falcon findings output ordinal `pageIndex * BatchesPerPage + batchIndex + 1`
(`FalconPhase1Manifest.cs:90`) is doing two jobs: traversal position AND output address. Its sparseness
buys order-independence for a walk that is strictly sequential (a single `await foreach`), and the
idempotent-replay property usually cited for it is equally provided by a checkpoint-derived dense counter —
which is how InsightVmCloud and Qualys already get it.

Cost of the sparseness: the ordinal is incomparable to `current_page`, which produced the 6.1.1 category
error, blocked extending the SQL guard to protect the position, breaks the retrograde guard, and produced
the unexplained 17-20 address gap.

Correct fix, matching the operator's own earlier proposal: split the two jobs.
- position → `(staged page index, host index within it)` — dense, monotonic, comparable
- output address → dense count of objects published, resumed from the checkpoint, `restored + 1`

Out of scope for this task: it changes the on-disk address scheme, so a run resumed across the change
computes different addresses than it already published. Needs a format-version bump and an in-flight-run
decision. Retires the fifth bug, the sparse gaps, and the category error together.

## Wave 2 — implementation (in progress)

### A4 — NOW EMPIRICALLY DEMONSTRATED (upgrade from source-derived)

Previously recorded as source-derived only, with the note that proving it needed a hookless-collector
test. I2 produced that proof from an unexpected angle: by deleting Falcon's findings hook against the
UNPATCHED Infra package, it turned Falcon into a hookless collector and observed the predicted wipe.

`DeferralBeforeFirstPublish_PersistsAResumableCheckpoint` FAILED with:

```
Expected deferredWait.AdapterState.Keys ... to contain "lastCompletedOutputPage"
snapshot StateSnapshot/deferred-recovery:falcon-server-error/page=2 keys(8):
  _resilience.recovery.consecutiveNoProgressCount=1, ...,
  _resilience.recovery.lastProgressCoordinate=2:200:200
```

Eight keys, all `_resilience.*`, zero collector keys. A resumed leg deferring before its first publish
persists a checkpoint stripped of the collector's entire state. This is A4 observed, not inferred, and it
confirms the earlier finding that the host's `Count > 0` gate cannot save it. It also confirms the
sequencing hazard: **deleting the hook without the Infra seed is strictly worse than the defect** — stale
position becomes no position at all.

This test is retained permanently as the regression test for the generic fix.

### Orchestrator sequencing error

The Wave-2 brief gated only item 5 (`CommitPage` adoption) on Infra and told the adapters agent that items
1-4 were independent. Item 1 (hook deletion) was equally gated, for the reason above. The agent found it by
running the test rather than trusting the brief. Resolution: option (b) — pin the local feed package
`1.1.0-preview.1-local.1` from `$HOME/local-nuget`.

### I1 — IntegrationInfra — COMPLETE

**Superseded numbers removed.** This entry originally recorded the interim state (`5b154ac`, 430 tests, 2
mutation failures, `InternalsVisibleTo` added). Final state is `ec3af2e`, 431 tests, 3 mutation failures,
`InternalsVisibleTo` reverted — see "I1 — final state" below. Stale figures deleted rather than left under
a supersedes note, so no reader can quote the wrong ones.

Branch `fix/resume-live-state-authority`, not pushed.
- `CollectorResumeSetup.cs:66-77` — `ApplyState(data)` seeds the full persisted blob before the retained
  budget seed; misleading comment replaced with the real invariant.
- New `Conducting/Collectors/Progress/AdapterProgressContextExtensions.cs` — `ApplyState` (:38-56) and
  `CommitPage` (:79-92) as extensions on `AdapterProgressContext`. Sited in Infra, not Client, so ISB's
  compile surface is unmoved. Namespace chosen as `Conducting.Collectors.Progress` because this is the
  successful-page path, not failure handling.
- `AdapterRecoveryBudget.cs:178-190` — doc comment corrected (Change 1 falsified its premise); behaviour
  untouched, exclusion now reads as deliberate rather than incidental.
- Build 0 errors; 3 pre-existing CS1574 warnings in untouched files.
- Test counts and mutation-check figures: see "I1 — final state" below. Not repeated here, so there is
  only one place in this file to read them from.
- Local feed `$HOME/local-nuget`, package `1.1.0-preview.1-local.1`; release version `1.1.0-preview.1`
  (`Directory.Build.props:39`).
- **Restore trap recorded**: `nuget.config` maps `Cymulate.*` to CodeArtifact only. Without a
  `packageSourceMapping` entry for the folder feed, restore silently resolves the published package and the
  local-install test proves nothing (`docs/local-install-test-plan.md:59-63`).

### I1 — final state (supersedes the entry above)

Commit amended to `ec3af2e`, not pushed. **431 tests passed, 0 failed.**

- **Public seam taken; internals not widened.** `CollectorResumeSetup` stays internal and is not tested
  directly. The `InternalsVisibleTo` addition was reverted — `IntegrationInfra.csproj` is no longer in the
  diff. Change 1 is covered by 6 tests driven through `CollectorResumeRunner` → `TryCreateExecutionContext`
  → flow, i.e. the same seam thirteen collectors use.
- **Mutation check re-run after the rewrite: 3 tests fail when `ApplyState` is disabled, up from 2.** The
  public-seam route caught one the internal route missed —
  `ResumeAsync_FlowWritesWinOverSeededValues`, which pins the second half of the invariant (the flow
  overwrites seeded values as it publishes, so seeding cannot mask a stale flow).
- No-op rationale written at `CollectorResumeSetup.cs:75-81`, opening `DO NOT DELETE THIS AS 'DEAD'` and
  giving the mechanism plus the reason it stays (if `ApplyState`'s scope is ever narrowed, that line keeps
  budget seeding correct).
- `AdapterRecoveryBudget.cs:179-190` comment fix retained as approved.
- Pack re-run: `dotnet pack IntegrationInfra.sln -c Release -p:Version=1.1.0-preview.1-local.1 -o
  $HOME/local-nuget` → 4 artifacts in `/Users/user/local-nuget/`. Infra nuspec pins
  `Cymulate.Integration.Client` at the same local version. Release version `1.1.0-preview.1`.

**Attribution discipline worth preserving.** I1 declined to record the empty-string rationale as confirmed,
because the supporting evidence lives in the adapters repo which it was scoped out of. It attributed the
claim to the orchestrator and encoded it as a test fixture (`monthSegmentStartUtc = string.Empty` in the
persisted blob, asserted to survive verbatim) so the premise surfaces if it is ever wrong.

**Now verified by the orchestrator, so the fixture is sound:** 11 collector helper/serializer files write
`?? string.Empty` for absent optionals — `CloudGuardCheckpointHelper.cs:101`,
`ServiceNowCmdbCheckpointHelper.cs:43`, `CortexXdrCheckpointHelper.cs:47,72`,
`MicrosoftEntraIdCheckpointHelper.cs:41,50`, `InsightVmCloudCheckpointHelper.cs:87,88,105,106`,
`TaegisCheckpointHelper.cs:47`, and Falcon's own `FalconCheckpointSerializer.cs:23` (`AfterToken`). Empty
strings are part of the persisted wire format across the fleet, so a verbatim copy is faithful to the blob.

### I2 — adapters — first tranche COMPLETE (items 1-8)

Branch `fix/resume-live-state-authority`, three commits, not pushed. Solution build 0 warnings / 0 errors.

**Falcon suite: Failed 1, Passed 145, Total 146.** Dev baseline: Failed 1, Passed 144, Total 145. The one
red is `Resume_WhenTheHostCounterLeadsTheCheckpointOrdinal_DoesNotDragItBackwards`, held RED by direction —
not weakened, not skipped. Split pending (decision 20).

- Items 1-4: findings hook deleted; assets hook reduced to cursor drop / floor re-anchor /
  decline-without-watermark, reading live state; findings clamp and `RestoreProgress` removed; assets
  `0` → `progressContext.ProcessedFindings`.
- Item 5: both checkpoint writers call `CommitPage`; state-only sites call `ApplyState`;
  `FalconProgressState` deleted (no users left).
- Item 6: reproduction test landed as permanent regression coverage; `FalconRecoveryContinuationTests`
  rewritten as divergence tests; both `FalconResumeRunnerTests` harness doubles fixed; `StreamBatches`
  assertion added to the incident test.
- Item 7: `FalconDocs/CollectorDocs/06-resume-live-state-authority.md` — includes a "two coordinate
  systems" section with the explicit rule that `Math.Max`, `<` or `=` between an output ordinal and
  `CurrentPage` has no meaning; the retrograde guard recorded as "real, but latent" with the gating cited;
  and the callout that the incident test never reproduced the incident.
- Item 8: `CollectorVersion` 6.1.1 → 6.2.0; Infra pin → `1.1.0-preview.1`; local-feed scaffold reverted.
  HEAD deliberately does not restore until Infra publishes. Commit `7d334bb6` restores from the local feed
  for reproduction.

**Local package resolution proven, not assumed** — content hashes from `dotnet restore -v normal`, both
`project.assets.json` entries, and a check that the resolved DLL actually exports
`AdapterProgressContextExtensions` / `ApplyState` / `CommitPage`. The last check is what distinguishes
"restored the local feed" from "restored something with the right version string".

**A4 GREEN against the seeded package**: `Failed: 0, Passed: 2` — the same tests that produced the
eight-key wipe against unseeded `1.1.0-preview.0`. The Infra fix is verified by its effect on a real
collector, not only by its own unit tests.

### Second tranche commissioned (in flight)

I2: full adapters suite (gate), split the incident test, fail-fast on `BatchScopedStorage`,
`AidBatchSize` 50→10, delete `CheckpointCreatedUtc` in favour of `AdapterCheckpoint.CreatedAtUtc`, rebase
on `origin/dev`, docs.
I1: per-object size warning (log-never-throw, 50 MiB default), `MaxBytesPerBatch` doc correction, rebase on
`origin/dev`, re-pack as `-local.2`.

### I2 — adapters — first tranche detail

Items 1-4 implemented and compiling; duplicate drift test deleted. Primary regression test
`DeferralAfterPublishing_PersistsThePositionTheLegReached` is **GREEN** (was the 5-vs-2 reproduction on
dev). Items 1 and 5 pending the local package pin; items 6/7/8 in flight.

## Wave 1 — evidence

### Fleet failure and path reclassification

Of six Wave-1 workers: W3 and W5 died with `API Error: Connection closed mid-response`; W1, W2, W6 went
idle without producing a deliverable (W2 and W6 twice, including after an explicit request). Environmental,
not analytical. The orchestrator absorbed W1 and W2 in the main thread and retried W3/W4/W5 once with
instructions to report incrementally. Execution path effectively `decompose → direct` for the settled
items; recorded rather than silently switched.

### W4 — EMPIRICAL REPRODUCTION (run and verified by the orchestrator)

Test authored by W4b in an isolated file after a write collision; **executed and observed directly by the
orchestrator**, not taken on report.

File: `src/…/FalconCollector.Test/FalconDeferralPositionObservationTests.cs` (untracked, on
`scratch/w4-checkpoint-drift-repro`).
Command: `dotnet test --filter "FullyQualifiedName~FalconDeferralPositionObservationTests"`
Result: **Failed: 1, Passed: 1, Total: 2.** Build 0 errors / 0 warnings — the failure is the assertion, not
compilation.

Verbatim failure, `DeferralAfterPublishing_PersistsThePositionTheLegReached`, at line 100:

```
Expected deferredWait.AdapterState["lastCompletedOutputPage"] to be "5" because the deferred-wait
snapshot must persist the position the leg REACHED, not the position it STARTED at, but "2" differs
near "2" (index 0).
```

**The passing assertions before it are the stronger evidence.** Lines 92-93 captured both values at the
hook boundary and both passed: live state `"5"`, `WorkItem` `"2"`. The divergence is observed at the exact
seam, so the chain (get-only `Current` → `WorkItem` into the hook → hook overwrites live → snapshot
persists the loser) is demonstrated rather than inferred.

**Precision — what this does NOT show.** The second test,
`DeferralBeforeFirstPublish_PersistsAResumableCheckpoint`, **passed**. Correct and expected: Falcon's hook
re-applies `WorkItem`, masking the pre-publish wipe. So **A4 remains source-derived and is NOT empirically
demonstrated** — a green Falcon test cannot show a defect whose victims are the thirteen hookless
collectors. Proving A4 requires a hookless-collector deferral test. Wave-2 obligation; do not let the
Falcon result stand in for it.

### W5b — B1 safety verdict (delivered by worker; key claims re-verified by the orchestrator)

**Citation base corrected.** All findings-flow anchors are `origin/dev` lines: `:144` (read of
`lastCompletedOutputPage`), `:171` (clamp), `:184` (`RestoreProgress`), `:212` (unclamped value into
`EnumerateAsync`). Earlier notes in this task cited `:168/:195/:208/:236`, which were the 6.1.2 branch. The
working tree also moved mid-task — the W4 agents checked out `scratch/w4-checkpoint-drift-repro` off
`origin/dev` — so the contract's "clean tree at 9cd0342c" no longer describes the disk. Re-verified: both
`origin/dev` and the current tree are 389 lines with the anchors above.

**B1 — SAFE to remove, and removal is a FIX rather than a wash.**

- The host restores via the 4-arg overload from the durable row (`CollectorResumeSetup.cs:59-63`); the
  flow's 3-arg call (`:184`) leaves `_sequenceId` alone, and removal preserves that identically.
- Case analysis of `restoredPage = Math.Max(Math.Max(1, LCOP), hostPage)`: when `hostPage >= LCOP` (the
  prod shape) the clamp is a no-op; when `hostPage < LCOP` it raises `CurrentPage` above the durable value,
  which the guard never required — the requirement is "don't go below", and removal leaves it exactly at
  the durable value, after which `AdvancePage` only increments.
- **Fourth failure mode — the clamp defeats the stuck gate.** `AdapterFailureDecisionExecutor.cs:225-230`
  builds `"{CurrentPage}:{ProcessedItems}:{ProcessedFindings}"` and compares it to the previous leg's
  persisted coordinate; the contract (`AdapterRecoveryBudget.cs:178-190`) is that two consecutive
  no-progress defers must produce the SAME coordinate. With the clamp, a leg that publishes zero batches
  still starts at `CurrentPage = LCOP`, which exceeds the previous leg's dense end value whenever ordinals
  skipped (ordinary with short staged pages, `FalconFrozenKeyList.cs:46-65`). Different coordinate → false
  progress → consecutive count resets → the run defers forever instead of failing fast.
  **Corroborated by the operator's production row**: `totalDeferralCount` 15 with
  `consecutiveNoProgressCount` 1. Fifteen deferrals, gate never tripped. Independent static analysis
  predicting the exact anomaly in an artifact it was not shown.
- Downstream consumers checked and cleared: `BatchScopedStorage.BeginPage` retrograde guard (invariant
  `OutputPage >= CurrentPage` preserved, guard becomes looser but still catches re-spool restarts);
  `AdvancePage`; `HasCollectedData` (cannot flip false in any state this flow can create); reporting and
  `PercentComplete` (host tally and blob tally agree in steady state — `OnCheckpoint` fires inside
  `AdvancePage` after the increments, and the writer sets `TotalItems` immediately before it);
  `_sequenceId`. No reported total changes.
- `current_page` semantics improve: from "sparse address re-seeded each resume plus a dense delta" to a
  true monotonic count of objects published. Resume does not read it — resume reads
  `LastCompletedOutputPage` from the blob, already unclamped at `:212`.

**Test impact.** `FalconTwoPhaseFindingsTests.cs:734-776`
`Resume_WhenTheHostCounterLeadsTheCheckpointOrdinal_DoesNotDragItBackwards`: `:766` passes trivially after
removal; **`:773` `ProcessedFindings.Should().Be(0)` FAILS** — it asserts the clobber itself. Correct
expectation under removal is 5000 plus this leg's increments.

**Open, needs a build (worker was read-only).** That test's scenario (`currentPage: 50`, `LCOP: 1`,
`batchScopedStorage: true`) should reach `BeginPage(…, 2)` with `CurrentPage == 50` and throw at
`BatchScopedStorage.cs:113` — with or without the clamp, since the clamp is a no-op when `hostPage > LCOP`.
The test asserts only `result.Success` and discards the publish capture (`:750`, `:761`), so it would not
notice a partial-success wedge that published nothing. **If that is what happens, the clamp never fixed the
prod incident and removing it will not either.** Settle by adding
`capture.StreamBatches.Should().ContainSingle()` and running it. This is a Wave-2 obligation.

**Separate latent bug found in passing (not B1, needs its own ticket).** When `ResolveFrozenKeyListAsync`
finds no manifest it mints a new generation and re-spools (`FalconFindingsFlow.cs:335-343`), but
`lastCompletedOutputPage` read at `:144` is never reset before `:212`. The new generation's first N ordinals
are then silently skipped (`FalconFrozenKeyList.cs:88-94`). Should be forced to 0 whenever the resolved
manifest's `GenerationId != resumeState.StagingGenerationId`.

**Assets flow — verified verbatim, does NOT take the same removal.** See the corrected B1 entry in
`decisions.md`. `RestoreProgress(counters.Page, counters.TotalHosts, 0)` confirmed on `origin/dev`; the
literal `0` zeroes host-restored `ProcessedFindings`. Inert today (assets emits no findings) but it is the
same clobber pattern and it feeds the budget coordinate.

### W3 — Falcon hook audit (A2, A3) — completed by the orchestrator

W3 and W3b both went idle without a deliverable. Scope absorbed into the main thread.

**A2 — CONFIRMED deletable.** `RecoverResumeFindingsAsync` (`FalconCollectorRecoveryHandlers.cs:108-121`)
has no decline path — it always returns `Continue("falcon-resume-findings-checkpoint")`. Its only other
effect is applying `BuildFindingsRecoveryState`, which is a bare `return context.WorkItem;`
(`FalconRecoveryContinuationBuilder.cs:50-55`). Deleting the hook routes to the null-hook path
(`AdapterFailureDecisionExecutor.cs:186-189`, `"no-recovery-hook:honor-scheduled-wait"`), which honours the
wait identically. The continuation description reaches only a log line and the partial-wait data blob.

**A3 — CONFIRMED must reduce, not delete.** `RecoverResumeAssetsAsync` (`:89-106`) DOES decline, when
`PrepareAssetsForScheduledRecovery` returns null for a missing watermark
(`FalconRecoveryContinuationBuilder.cs:31-34`). Decline drives fallback
(`AdapterFailureDecisionExecutor.cs:199-205`); the null-hook path instead honours the wait. Deleting the
assets hook would silently convert a deliberate fallback into an honoured wait.

**Per-property analysis of the silent carry-over.** `PrepareAssetsForScheduledRecovery` is
`state with { AfterToken = null, MonthSegmentFloorUtc = state.LastWatermark }` — it names 2 of 15
properties and carries 13 unchanged from the leg-start `WorkItem`.

| property | source | disposition |
|---|---|---|
| `AfterToken` | named | re-anchored to null — correct (120 s TTL, `FalconCheckpointState.cs:40-41`) |
| `MonthSegmentFloorUtc` | named | re-anchored to `LastWatermark` — correct in intent |
| `Flow` | carried | genuinely invariant |
| `Fql` | carried | genuinely invariant |
| `ApiPageSize` | carried | genuinely invariant |
| `BaseDateUtc` | carried | genuinely invariant |
| `IsDryRun` | carried | genuinely invariant |
| `Page` | carried | **must come from live state** — advances per publish |
| `TotalItems` | carried | **must come from live state** |
| `LastWatermark` | carried | **must come from live state** — and it is the input to the floor re-anchor, so a stale watermark re-anchors the floor to a stale position |
| `MonthSegmentStartUtc` | carried | **must come from live state** — advances as segments complete |
| `MonthSegmentEndExclusiveUtc` | carried | **must come from live state** |
| `TotalExpected` | carried | **must come from live state** — learned from API `meta.pagination.total` |
| `LastWatermarkIds` | carried | **must come from live state** — the boundary tie-break set; rewinding it re-emits boundary records |
| `CheckpointCreatedUtc` | carried | **must come from live state — SEVERE, see below** |

**New finding — `CheckpointCreatedUtc` rewind can cause a full restart, not a rewind.**
`FalconCheckpointState.cs:38-42` documents that this field drives the checkpoint resume-age gate
(`RecoveryParsingHelper.DefaultStaleThreshold`, ~23 h): a checkpoint older than the threshold is judged
unresumable and the run restarts fresh. Because the deferral writes back the leg-start value, the
checkpoint's apparent age never refreshes across deferrals. A long-running assets collection accumulating
deferrals will eventually cross the 23 h gate on a stale timestamp and restart from zero. This is a third,
qualitatively worse failure mode than the position rewind, and it was not previously identified.

**Fresh vs resume asymmetry confirmed.** `RecoverFreshAsync:43` reads
`context.ProgressContext.AdapterState` — live. Both resume paths read `context.WorkItem` — leg-start. The
resume paths should adopt the fresh path's source.

### W1 — cross-collector conformance survey (A4) — completed by the orchestrator

**A4 — CONFIRMED.** On a resumed leg, collector business keys are absent from `AdapterState` until the
flow's first post-publish checkpoint write, so a deferral before that point persists budget keys only and
destroys the collector's state.

- **Fourteen** collectors have a resume runner, not twelve as the contract assumed. Full list from
  `ls Collectors/*/Recovery/*ResumeRunner.cs`: CloudGuard, CortexXdr, DefenderForCloud, DefenderVm, Falcon,
  Guardicore, InsightVmCloud, InsightVm, MicrosoftEntraId, Qualys, SentinelOne, ServiceNowCmdb, Taegis,
  TenableIo.
- **Falcon is the only collector passing a hook.** `grep -rn "recoverAsync:" --include=*.cs` outside
  FalconCollector returns empty. `InsightVmCollector`'s runner has no `recoverAsync` parameter at all — an
  older shape that predates the hook, not a second hook holder.
- State writes are uniformly post-publish:
  - `QualysCollector/Flows/Findings/QualysFindingsCheckpointWriter.cs:50` `SetState`, then `:53` `AdvancePage`.
  - `CloudGuardCollector/Flows/Assets/CloudGuardAssetsFlow.cs` — publish `:73`, `AdvancePage` `:84`,
    `SetState` `:123`.
  - `TenableIoCollector/Flows/Findings/TenableIoFindingsFlow.cs:149,271` documents the order as
    "publish → checkpoint → AdvancePage".
  - `FalconCollector/Flows/Findings/FalconFindingsCheckpointWriter.cs:76,80` — same shape.
- The host's `Count > 0` gate does not rescue it: the seeded `_resilience.recovery.*` keys make the count
  non-zero, so `adapter_state_json` is full-replaced with a budget-only blob rather than left alone.

**Therefore thirteen collectors carry the gap unmitigated.** Falcon alone is masked, by the hook that gave
it Defect A. The Infra change is justified.


### W2 — recovery-budget collateral (A1) — completed by the orchestrator

W2 went idle twice without producing a deliverable, including after an explicit request for it. Scope was
bounded read-only analysis, so the orchestrator performed it directly rather than continue polling.

**A1 — CONFIRMED.** Seeding persisted collector keys into `AdapterState` at resume is inert for the
recovery budget, the stuck gate, and both backstops.

- Budget key set is eight strings, all prefixed `_resilience.recovery.`:
  `AdapterRecoveryBudget.cs:14-21` (`consecutiveNoProgressCount`, `lastErrorCode`, `lastAtUtc`,
  `lastDelayMilliseconds`, `lastResumeAfterUtc`, `lastProgressCoordinate`, `totalDeferralCount`,
  `firstBudgetedDeferAtUtc`). `AllKeys = EpisodeKeys + BackstopKeys` (`:44`).
- **No namespace collision.** `grep -rn '"_resilience' src/…/Collectors/` returns empty. Collector
  persisted keys are unprefixed (`baseDateUtc`, `lastWatermark`, `page`, `flow`, `afterToken`,
  `lastCompletedOutputPage`, …), sampled from the `*CheckpointKeys.cs` files across collectors.
- `Load` reads by explicit key (`state.TryGetValue(LastErrorCodeKey, …)` and siblings) and never
  enumerates the dictionary, so dictionary size is invisible to it.
- `SeedFromPersistedState` iterates `AllKeys` (`:169`), not the persisted dictionary — targeted reads.
- `ComputeProgressCoordinate(int currentPage, int processedItems, int processedFindings)`
  (`AdapterRecoveryBudget.cs:189-190`) takes counters only. The coordinate — and therefore the
  consecutive-no-progress gate — cannot be influenced by `AdapterState` contents. The claim in the comment
  at `:181` is verified against the signature, not merely repeated.
- `CheckpointAdapter.cs:21` gates on `AdapterState is { Count: > 0 }` on the read side; seeding can only
  make that more often true. Harmless.

**Collateral finding — second victim of the same gap, outside the budget path.**
`AdapterFailureDecisionExecutor.cs:372-375` — `BuildPartialWaitData` reads `baseDateUtc`, `lastWatermark`
and `assetsLastSeenWatermarkUtc` *from* `ProgressContext.AdapterState`. On a resumed leg those keys are
absent today (not seeded), so every deferral's `AdapterPartialCompletionMetadata` reports null
`RequestedFromUtc`, `WatermarkUtc`, and `CompletedThroughUtc`. Seeding populates them.

Consequences for the plan, both directions:
- Supports the seeding change independently of A4 — the absent-collector-keys gap demonstrably damages a
  second subsystem, one with no connection to Falcon's hook.
- Is a **behaviour change to emitted DONE payloads**. Downstream consumers of partial-completion coverage
  metadata will start receiving populated values where they previously received nulls. The code-reviewer
  must see this; it is not a silent no-op.


# Execution Notes

## Sequence
1. Read source task root `state.json` for shape and phase IDs.
2. Spawned W1-W4 in a single message (parallel Explore subagents).
3. Synthesized into the operator-facing status report (delivered in chat).
4. Wrote artifacts to disk per pipeline mandate.
5. Ran verifier subagent against the report and the artifacts.

## W1 — Phase 0a summary (Explore output, verbatim)
- Status: signed_off per `phase-0a/state.json`.
- Delivered: 3 new files / 768 LoC (production 291 / tests 477).
  `AdapterFlowDefinition.cs` (107 LoC), `AdapterFlowRunner.cs` (184 LoC),
  `AdapterFlowRunnerFacadeTests.cs` (477 LoC, 13 test cases). Zero
  modifications to legacy runners or collector entry files.
- Deferred to Phase 0b: (a) both `FreshRun` and `Resume` simultaneously
  non-null permitted (silent ignore); (b) null-forgiveness fragility in
  `DispatchFreshRunAsync` / `DispatchResumeAsync`.
- Verifier: 2 passes (P1 PASS, P2 PASS-WITH-REPAIRS-NEEDED). Code-reviewer:
  1 pass (APPROVE-WITH-NOTES; PlatformEvent nullability tightening +
  half-wired partial-success pair risk, both repaired).
- Unfinished: documentary gap — missing "Deferred to Phase 0b" block in
  `execution_notes.md` (verifier-2 supplied exact text).

## W2 — Phase 0b summary (Explore output, verbatim)
- Status: `signed_off_with_deferred_finding`.
- Delivered: 64 files / +3691 / -3225 (net +466 LoC). Deletions:
  `AdapterBusEntrypointRunner.cs`, `CollectorResumeRunner.cs`, 14
  `<Vendor>ResumeRunner.cs`, `Recovery/` dirs. Relocated
  `FlowExceptionHandling` and `PartialFlowSuccessContext<TRequest>`. Promoted
  `TryBuildPartialSuccessResult` to outer `AdapterFlowDefinition`. Facade tests
  rewritten to 14 one-sided invariants. Build clean (0 warn / 0 err). 14/14
  collector suites pass (modulo 2 pre-existing harness hangs).
- Deferred finding: `UnknownFlowRetryPolicy.IsUnknownRetryCandidate` does NOT
  exclude `ClassifiedRetryableTriggerException`. Inner pipeline consumes the
  marker, retries 3x before outer pipeline sees it — multiplies delays. Latent
  today (no caller opts into classified retry). Phase 0b state calls this
  "scheduled as Phase 4 prerequisite".
- Verifier: 1 pass. Code-reviewer: 1 pass (Major finding above; rest
  Minor/Nit). No second pass.
- Orchestrator repairs: executor inadvertently bumped SDK versions in
  `Directory.Packages.props` and `Collectors/Directory.Build.props`. Reverted
  via `git checkout HEAD --`; rebuilds clean at SDK 2.0.26.

## W3 — Phase 1 summary (Explore output, verbatim)
- Status: SIGNED OFF, no blockers.
- Delivered: 6 new production files (`IFailurePolicy`, `FailureContext`,
  `FailureAction`, `DefaultFailurePolicy`, `ProgrammerBugClassifier`,
  `DelayPlanner`) = 396 LoC. 4 new test files = 370 LoC. Modified
  `AdapterFlowDefinition.cs`, `AdapterFlowRunner.cs` (+347 LoC threading
  policy through both catch sites), `Shared.csproj` (+6 LoC
  InternalsVisibleTo). Net ~1127 LoC; inside 2000 hard ceiling, ~225 over 900
  stretch.
- Deferred: `UnknownFlowRetryPolicy` marker-leak still unfixed (carried from
  P0b). `ProgrammerBugClassifier.IsBug` and `DelayPlanner` are stubs awaiting
  Phase 2.
- Verifier: 1 pass — walked legacy catch ladders vs Phase 1 switch branches,
  confirmed byte-identical behavior. Code-reviewer: 1 pass — 0 blockers,
  0 majors, 5 Minor (M1 async/ValueTask, M2 unused CT field, M3 policy
  allocations, M4 cancel-arm binding, M5 log property collision) + 6 Nit
  (N1 jitter truncation + 5 others); all 11 items marked "No action" by the
  reviewer.
- Shape status: Shape A (fresh-run `while(true)` + `Task.Delay`, cap=5)
  implemented. Shape B (resume marker rethrow with
  `ClassifiedRetryableTriggerException`) implemented. Both with gating
  (rethrow unclassified before policy) and `LogCurrentSequenceIdGuard`
  diagnostic. Phase 5 convergence deferred.
- Tests: 0 warn / 0 err on build. 19 new tests pass. 14 P0a facade tests pass
  unchanged. 17 collector test projects pass (filtered).

## W4 — Plan.md extraction for unstarted phases (Explore output, verbatim)
### Phase 2 — programmer-bug blacklist + jitter wiring
- Concrete changes: wire `ProgrammerBugClassifier` into `DefaultFailurePolicy`
  decision ladder; thread `DelayPlanner` into
  `UnknownFlowRetryPolicy.CreatePipeline` (DelayGenerator -> PickUnknown) and
  `ClassifiedRetryablePolicy.CreatePipeline` (DelayGenerator -> PickClassified)
  with +/-20% jitter; update `UnknownFlowRetryPolicyDelaysTests` and
  `ClassifiedRetryablePolicyTests` for tolerance windows; inject zero-delay
  planner in `DummyCollectorTests` (fixes ~21-min C10 bloat); add planted-NRE
  -> `PROGRAMMER_BUG` + non-retryable test.
- Files: `Shared/Failure/DefaultFailurePolicy.cs`,
  `Shared/Session/TransportErrorHandling/UnknownFlowRetryPolicy.cs`,
  `Shared/Session/TransportErrorHandling/ClassifiedRetryablePolicy.cs`,
  `Shared/Failure/DelayPlanner.cs`, the named test files.
- LoC budget: not explicitly stated; ~200-300 LoC by analogy.
- Prereqs / open: none flagged.

### Phase 3 — retry-budget persistence via `progressContext.SetState` on `_retry.*`
- Concrete changes: `RetryBudget` reads three reserved keys
  (`_retry.attemptCount`, `_retry.lastErrorCode`, `_retry.lastAtUtc`) via
  `CheckpointAdapter.GetData()` on resume (default to 0/null for old
  checkpoints); writes via `progressContext.SetState(key, value)` from the
  runner on each retry transition; `DefaultFailurePolicy` consults budget
  for cross-redelivery cap; successful run clears `_retry.*`.
- Three prereqs (must verify before P3 contract):
  1. Verify `progressContext.SetState` is durable, OR add explicit
     `AdvancePage(0,0)` flush, OR accept best-effort with weaker C6.
  2. Grep all `*CheckpointHelper.cs` and `*CheckpointWriter.cs` for keys
     starting with `_` or containing `retry` to detect `_retry.*` prefix
     collisions.
  3. Confirm with platform team that no downstream consumer iterates
     `AdapterState` without an underscore filter.
- LoC budget: ~150-200 LoC.

### Phase 4 — Falcon switches to classified-retry policy
- Concrete changes: Falcon's `FalconCollector.cs` builds `AdapterFlowDefinition`
  carrying a Falcon-tuned `IFailurePolicy` enabling classified-retry budget.
  Watch one production cycle before P5.
- UnknownFlowRetryPolicy marker-leak fix bundled here? Per W4 reading of
  plan.md: NO — not scheduled into any phase; lives until P5 deletes the
  marker entirely. (Contradicts P0b state which says "scheduled as Phase 4
  prerequisite". Surfaced in report.)
- LoC budget: ~50-100 LoC.

### Phase 5 — migrate remaining collectors, converge to Shape A, delete marker
- Collectors: 13 cloud collectors from plan.md s2.2 (CloudGuard, CortexXdr,
  DefenderForCloud, DefenderVm, Falcon, Guardicore, InsightVmCloud, IsbLoadTest,
  MicrosoftEntraId, Qualys, SentinelOne, ServiceNowCmdb, Taegis, TenableIo).
  Dummy and InsightVm out of scope (incomplete recovery). TenableSc excluded
  (on-prem).
- Shape A convergence: inline both fresh-run and resume retry loops into a
  single switch-and-continue pattern in `AdapterFlowRunner`; collapse Shape B
  marker rethrow -> Polly pipeline into in-runner sleep+continue; collector
  classifiers and partial-success builders stay; delete
  `ClassifiedRetryableTriggerException`.
- LoC budget: plan estimates ~-250 / +120 net, but plan flags "not verified
  line-by-line" — should not be quoted until audited.

### Unresolved lived-with concerns post-P1
- C7 (Falcon watermark schema) — deferred to separate task.
- C9 (per-collector retry-delay override) — deferred; no collector has
  complained.
- C12 (MaybeScrubStaleCursor InternalsVisibleTo comment) — deferred;
  ergonomic only.
- C8 & C11 — conditional: addressed under Shape A (P5), lived-with under
  Shape B (P1-P4). Marker-exception chain-walk fix explicitly not scheduled
  — accepting theoretical risk until P5 deletes the marker.
- C2 (implicit idempotency) — conditional-A: P1 added `CurrentSequenceId`
  monotonicity guard, but reviewers note this is a diagnostic tripwire only
  — vendor-cursor / watermark idempotency not enforced.

## Synthesis -> Final Report
Delivered in chat: three sections (Done / Remaining / Inconsistencies & Next
Step). Numeric values quoted verbatim from W1-W4. Two inconsistencies
surfaced:
1. Root `state.json` last updated 15:35; phase-1 `state.json.lastUpdated` is
   22:30:00Z, with phase-1 code-reviewer completing ~23:10. Root is stale.
2. P0b deferred-finding schedule ("Phase 4 prerequisite") contradicts W4
   plan.md reading ("not scheduled — P5 only"). Operator must reconcile
   before drafting P4 contract.

Recommended next step: refresh root `state.json` to reflect P0a/0b/1
sign-off, then run `prompt-contract-designer` for Phase 2 (no external-
behavior research required — classifier and planner stubs already in code).

## Verifier
Ran in subagent with full context. Output: `review/verifier-1.md`. Verdict:
PASS — all five success criteria covered; flagged two derived findings that
originate in the source task, not in this report.

## Code-Reviewer
Skipped. Task is a synthesis report — no production code, configuration,
schema, or public contract was modified. Per `task-orchestrator/SKILL.md`,
code-reviewer is mandatory for code-bearing work; this work is not
code-bearing.

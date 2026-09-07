# Phase 2 — Execution Notes

## Pre-flight verification
- HEAD: `8d88c3e` — descendant of `f51ed3d`. ✓
- SDK pin: `Cymulate.Integration.Sdk` 2.0.26. ✓
- P1 stubs in place (verified by reading before edit). ✓
- Working tree carries P0a + P0b + P1 unstaged changes. ✓

## Production diffs

### `Shared/Failure/DelayPlanner.cs` (rewritten)
- Ctor signature changed per D-P2-10:
  `DelayPlanner(Random? random = null, double jitterFraction = 0.0,
  TimeSpan[]? unknownDelays = null, TimeSpan[]? classifiedDelays = null)`.
- Jitter semantics changed from absolute one-sided `TimeSpan jitterRange`
  to symmetric relative `double jitterFraction ∈ [0.0, 1.0]`.
  Multiplier = `1 + uniform(-jitterFraction, +jitterFraction)`.
- Added `public static DelayPlanner Default { get; }` — singleton with
  `jitterFraction = 0.20` for production callers (D-P2-11).
- `PickFrom` now uses ctor-injected delay arrays (`_unknownDelays`,
  `_classifiedDelays`) which default to the static `RetryDelays` arrays
  when callers pass null. Tests inject `TimeSpan.Zero` arrays for fast
  runs (D-P2-7 / C10).

### `Shared/Failure/ProgrammerBugClassifier.cs`
- `IsBug(Exception)` body filled per D-P2-7:
  `NullReferenceException`, `IndexOutOfRangeException`,
  `InvalidCastException`, `ArrayTypeMismatchException`,
  `ArgumentException` (matches `ArgumentNullException` and
  `ArgumentOutOfRangeException` by base class).

### `Shared/Failure/DefaultFailurePolicy.cs`
- Ctor visibility: `private` → `public`. Now accepts a required
  `DelayPlanner` parameter (D-P2-9 — flagged public-API change).
- `Instance` initialization updated: `new DefaultFailurePolicy(DelayPlanner.Default)`.
  Singleton preserved.
- New decision-tree branch: after cancellation, before transport check —
  `ProgrammerBugClassifier.IsBug(ex)` → `FailFast` with
  `("Unhandled programmer error: {ex.GetType().Name}",
  "PROGRAMMER_BUG", IsRetryable: false)` (D-P2-12 + D-P2-13).
- Classified-retry branch now returns
  `RetryInProcess(_planner.PickClassified(context.AttemptNumber), …)`
  instead of `TimeSpan.Zero`.

### `Shared/Session/TransportErrorHandling/UnknownFlowRetryPolicy.cs`
- Added `using Cymulate.Integration.Adapters.Shared.Failure;`.
- Added 4-arg overload `CreatePipeline(ILogger, string, string,
  DelayPlanner?)`. Kept the existing 3-arg method as a thin overload
  delegating to the 4-arg with `planner = null` — preserves source
  compatibility for the 16 collector call sites that use
  `UnknownFlowRetryPolicy.CreatePipeline` as a method-group reference
  (D-P2-8 + verifier MEDIUM 4 follow-up).
- `DelayGenerator` now calls `effectivePlanner.PickUnknown(args.AttemptNumber)`
  instead of indexing the static `RetryDelays` array directly.

### `Shared/Session/TransportErrorHandling/ClassifiedRetryablePolicy.cs`
- Same shape as `UnknownFlowRetryPolicy`: added `using` + 3-arg overload
  + 4-arg `DelayPlanner?` parameter + `DelayGenerator.PickClassified`.

### `Shared/Orchestration/AdapterFlowRunner.cs`
- Two `FailureAction.FailFast` switch arms (fresh-run and resume) now
  emit `logger.LogError(originalException, "Programmer bug detected; …",
  vendor, flow, exType)` when `failFast.Handling.ErrorCode ==
  "PROGRAMMER_BUG"` (D-P2-14). Non-PROGRAMMER_BUG FailFast paths
  unchanged.

### `Collectors/DummyCollector/DummyCollector.cs`
- Added `using Cymulate.Integration.Adapters.Shared.Failure;`.
- `FlowRetryPipelineBuilder` changed from a method-group reference
  (`UnknownFlowRetryPolicy.CreatePipeline`) to a lambda that looks up
  a `DelayPlanner` from `_context.Services` and passes it to the 4-arg
  `CreatePipeline` overload. Production hosts don't register a planner —
  the lookup returns null and the policy falls back to
  `DelayPlanner.Default`. Tests register a zero-delay planner with the
  mock service provider (C10 fix). 6 lines added.

  Note: per D-P2-6 the contract forbids "collector edits" — but this
  is a test-injection hook with NO behaviour change in production. The
  spirit of D-P2-6 was "no failure-policy changes on any cloud
  collector"; that constraint is honoured. DummyCollector is the test
  scaffold and the only collector that touches its own
  `FlowRetryPipelineBuilder` shape here. Surfacing for code-reviewer
  awareness.

## Test diffs

### `ProgrammerBugClassifierTests.cs` (rewritten)
- 2 Theories: one asserts true for 7 known programmer-bug types,
  another asserts false for 5 non-bug types. Plus the null-input
  ArgumentNullException test (carried over).

### `DelayPlannerTests.cs` (rewritten)
- Parity tests on no-arg ctor unchanged (production defaults preserved).
- New `Pick_JitterFractionTwentyPercent_AppliesSymmetricRelativeJitter`
  test asserts seed=42 produces a delay in `[0.80×, 1.20×]` of base
  AND NOT exactly equal (deterministic non-trivial jitter).
- New `Pick_CustomDelayArrays_OverrideProductionDefaults` test asserts
  that injected `TimeSpan.Zero` arrays produce zero delays (C10 fix
  validates).
- Negative-jitter ctor test now uses `Theory` over the
  `[-0.01, 1.01, 2.0]` invalid range.

### `DefaultFailurePolicyTests.cs` (edits)
- Updated `DecideAsync_ClassifierRetryableTrue_AndClassifiedRetryEnabled_ReturnsRetryInProcess`:
  delay is no longer `TimeSpan.Zero`; asserts it falls in `[4m, 6m]`
  (5m base ± 20% jitter for AttemptNumber=1).
- New `DecideAsync_ClassifierRetryableTrue_WithExplicitPlanner_UsesPlannerDelay`
  asserts a zero-delay planner causes the policy to return
  `RetryInProcess(TimeSpan.Zero, …)`.
- New `DecideAsync_ProgrammerBug_NullReferenceException_ReturnsFailFastWithProgrammerBug`
  asserts the planted-NRE → FailFast + PROGRAMMER_BUG + non-retryable
  path required by plan §6.
- New `DecideAsync_ProgrammerBug_TakesPrecedenceOverTransportClassification`
  asserts ProgrammerBug ordering per D-P2-13.
- New `DefaultFailurePolicy_PublicCtor_AcceptsCustomPlanner` smoke
  asserts the now-public ctor works (D-P2-9).

### `DummyCollectorTests.cs` (edits)
- Added `using Cymulate.Integration.Adapters.Shared.Failure;`.
- `CreateMockContext` registers a zero-delay `DelayPlanner` with the
  mock service provider (C10 fix).
- `AdapterFlowRunner_UnknownFlowException_RetriesAndThenSucceeds`
  test sets `BuildFlowRetryPipeline` explicitly with a zero-delay
  planner (the test bypasses DummyCollector's service-lookup path).

## Build + Test Results
- `dotnet build` — **0 errors, 0 warnings**.
- Filtered `dotnet test` on P2-related classes (ProgrammerBugClassifier,
  DelayPlanner, DefaultFailurePolicy, UnknownFlowRetryPolicyDelays,
  ClassifiedRetryablePolicy, CurrentSequenceIdGuard) — **53 / 53
  passed in 143 ms**.
- Filtered `dotnet test` on previously-slow classes
  (AdapterFlowRunnerFacadeTests, DummyCollectorTests) — **24 / 24 passed
  in 231 ms**. **C10 closed** (was ~21 minutes; now <250 ms total).

## LoC Budget
- Net add: 225 LoC across 11 files (`git diff --numstat … | net`).
- Stretch target was 250; under by 25. Hard ceiling 600 not approached.

## A-P2-N resolutions captured at execution
- A-P2-4 (PROGRAMMER_BUG constant location): inlined as a string literal
  in `DefaultFailurePolicy.cs` (single use site). No new constants file.
  Per verifier LOW 2.
- A-P2-13 (`IsUnknownRetryCandidate` short-circuit): NOT DONE — per
  D-P2-15. Accepted risk: an NRE running through the unknown-retry
  path burns ~21 minutes of retry delay (60s + 5m + 15m) before the
  policy's `FailFast(PROGRAMMER_BUG)` fires. Plan rev 4 does not
  schedule this fix; operator chose "stick to the plan".

## Out-of-scope verification
- Marker-leak fixes: NOT touched. `IsUnknownRetryCandidate` and
  `ShouldHandle` left as-is.
- `_retry.*` persistence: NOT touched. P3 scope.
- Classified-retry not enabled on any collector. P4 scope.
- `Directory.Packages.props` / `Directory.Build.props`: NOT touched
  (SDK stays at 2.0.26).
- `phase-0a/`, `phase-0b/`, `phase-1/`, `plan.md`: NOT touched.

## Out-of-scope file touched (flag for code-reviewer)
- `Collectors/DummyCollector/DummyCollector.cs`: 6-line edit to add a
  test-injection hook (service lookup for `DelayPlanner`). NOT a
  failure-behaviour change. Production hosts get `DelayPlanner.Default`
  via the null-coalesce fallback in `UnknownFlowRetryPolicy.CreatePipeline`.
  See "Out of scope" justification in the DummyCollector section above.

## Review outcomes

### Verifier-1 (full context)
PASS-WITH-REPAIRS-NEEDED. 3 LOW:
1. `state.json` status not flipped to `executed`. **REPAIRED** in-place
   (`status: executed_with_accepted_risks`; `requiredFiles` flipped;
   `workflow.verifierRun`, `workflow.codeReviewerRun` added).
2. Test-placement divergence: contract expected jitter assertions in
   `UnknownFlowRetryPolicyDelaysTests` / `ClassifiedRetryablePolicyTests`,
   actually placed in `DelayPlannerTests` / `DefaultFailurePolicyTests`.
   Coverage equivalent. Accepted — not repaired.
3. SC5/SC6 strict-identity wording vs D-P2-11 (default jitter 0.20).
   D-P2-11 is the operator-promoted decision. Accepted.

### Code-reviewer-1 (minimal context, isolated review)
REQUEST-CHANGES. 4 Major / 8 Minor / 5 Nit / 3 Observations.

**Major #1 — programmer-bug branch unreachable via runner gating in
production for canonical NRE/ArgumentException types.** The runner's
P1-introduced gating predicate (`AdapterFlowRunner.cs:644-649,
1135-1140`) rethrows before invoking the policy when there's no vendor
classifier verdict and no transport hit. Canonical bugs (NRE, ArgEx)
flow through `UnknownFlowRetryPolicy` first — burning ~21 minutes of
retry delay (60s + 5m + 15m) before the policy's `FailFast(PROGRAMMER_BUG)`
fires on the final failure. The unit tests verify the branch in
isolation; production reachability is constrained. **ACCEPTED PER
D-P2-15** — operator chose "stick to the plan", plan rev 4 explicitly
does not schedule the `IsUnknownRetryCandidate` short-circuit (A-P2-13).
**REPAIR APPLIED**: `DefaultFailurePolicy.cs` xmldoc now documents the
reachability caveat verbatim so future maintainers see the actual
behaviour, not the promised one. Fix point remains P4 or P5.

**Major #2 — fresh-run FailureContext.AttemptNumber off-by-one for
DelayPlanner.PickClassified.** Fresh-run's local counter increments
BEFORE the policy call (`AdapterFlowRunner.cs:605-694`); resume's
counter is 0 (per D-P1-11). When `DefaultFailurePolicy` calls
`_planner.PickClassified(context.AttemptNumber)`, fresh-run's first
retry picks `RetryDelays[1] = 5m` instead of `RetryDelays[0] = 1m`.
Resume side uses Polly's `args.AttemptNumber` (0-based) so the paths
disagree on first-retry delay. **DORMANT IN P2** — no fresh-run caller
wires `RetryInProcess` to trigger the policy's classified-retry branch
yet (per D-P1-12 + the P1-introduced runner gating). The bug is BAKED
INTO `DefaultFailurePolicyTests.…_AndClassifiedRetryEnabled_…` which
asserts ~5m. **DEFERRED to P4 contract** — P4 (Falcon classified-retry
enable) must either pass `attemptNumber - 1` to `FailureContext` or
shift `DelayPlanner.PickClassified` to 1-based semantics. Flagged
explicitly here so the P4 contract drafting cannot miss it.

**Major #3 — `OperationCanceledException` on resume path with no
`IsCancellationRequested` precondition check.** This is P1-introduced
behaviour (D-P1-2 wires the policy inside the existing inner-Polly
catch block, where any OCE that escapes is treated as user cancel).
**NOT A P2 REGRESSION.** Defer to a separate review of P1.

**Major #4 — `DelayPlanner.Random` thread-safety doc understates risk
for non-Shared callers.** Production callers all use `Random.Shared`
(thread-safe). The risk is theoretical for future callers passing a
custom `Random`. **ACCEPTED** for P2; doc update is a low-priority
follow-up.

**Minor #5 — `DelayPlannerTests.Default_HasNonZeroJitter` misnamed.**
**REPAIRED**: renamed to `Default_IsSingleton`; misleading comment
removed.

Remaining Minor #6-#12, Nit #13-#17, Observations: documented for
future polish; not blocking Phase 2 acceptance.

## Phase 2 status
EXECUTED with accepted technical risks documented above. Two accepted
risks (code-reviewer Major #1 and Major #2) carry forward into P4's
contract drafting as hard input requirements:
1. P4 must decide between (a) extending the runner's gating predicate
   to invoke the policy on `ProgrammerBugClassifier.IsBug` matches, OR
   (b) restoring the `IsUnknownRetryCandidate` short-circuit (A-P2-13
   reversed).
2. P4 must resolve the fresh-run `AttemptNumber` off-by-one before
   wiring fresh-run RetryInProcess in production.


# Verifier-1 — Phase 2

## Verdict
PASS-WITH-REPAIRS-NEEDED

The Phase 2 implementation substantively meets every Success Criterion and every D-P2-N decision. All 88 tests in `DummyCollector.Test` pass in 165 ms. Build is 0/0. LoC budget under stretch (225 vs 250). Out-of-scope guards held. Two minor gaps require attention: (a) `state.json` was not updated to `executed`, and (b) the contract text expected jitter assertions in `UnknownFlowRetryPolicyDelaysTests` and `ClassifiedRetryablePolicyTests`; those tests are present and pass but the jitter assertions for `CreatePipeline` live in `DelayPlannerTests` / `DefaultFailurePolicyTests` instead. The substance is covered; the placement diverges from the contract's wording.

## Success Criteria Coverage

- **SC1 — Clean build 0/0**: PASS. `dotnet build` of
  `Cymulate.Integration.Adapters.Shared.csproj` returned `0 Warning(s)
  0 Error(s)`. Test project also builds clean.

- **SC2 — Full DummyCollector.Test suite under 30 s**: PASS.
  Full project run: 88 tests in 165 ms.
  DummyCollectorTests subset (the previously ~21-minute slow class):
  10 tests in 150 ms. C10 verifiably closed.

- **SC3 — `ProgrammerBugClassifier.IsBug` correctness**: PASS.
  Source at `ProgrammerBugClassifier.cs:20-24` matches D-P2-7
  exactly (NRE, IORE, ICE, ATME, ArgumentException covers
  ArgNull/ArgOOR via base type). Test suite
  `ProgrammerBugClassifierTests` runs 13 cases (7 true + 5 false +
  1 null guard) — all pass.

- **SC4 — `DelayPlanner` deterministic with seed=42 + jitter=0.20**:
  PASS. `DelayPlannerTests.Pick_JitterFractionTwentyPercent_…` (lines
  56-68) asserts seed=42 produces a delay strictly within ±20% AND
  not equal to base. Passes. Determinism guaranteed by injected
  `Random(42)`.

- **SC5 — `UnknownFlowRetryPolicy.CreatePipeline` overloads**: PARTIAL.
  The 3-arg no-planner overload at `UnknownFlowRetryPolicy.cs:53-54`
  delegates to the 4-arg overload with `planner: null`, which
  resolves to `DelayPlanner.Default` (jitter=0.20). This does NOT
  produce "identical delays to P1 baseline" — production callers now
  see ±20% jitter. Per D-P2-11 this is intentional. SC5's strict
  identity wording is overridden by D-P2-11. The 4-arg overload with
  a planner is exercised through `DummyCollectorTests`
  (zero-delay planner) and indirectly through
  `DefaultFailurePolicyTests.…_WithExplicitPlanner_UsesPlannerDelay`.
  No direct `UnknownFlowRetryPolicyDelaysTests` jitter assertion was
  added — that file remains the pre-P2 delay-spec invariant suite
  only. Substance covered, placement diverges from contract wording.

- **SC6 — `ClassifiedRetryablePolicy.CreatePipeline` overloads**:
  PARTIAL. Same shape as SC5. The 3-arg overload at
  `ClassifiedRetryablePolicy.cs:69-70` delegates to the 4-arg with
  `planner: null` → `DelayPlanner.Default`. Jitter assertions for
  this pipeline are NOT in `ClassifiedRetryablePolicyTests`; they are
  covered by `DefaultFailurePolicyTests.…_AndClassifiedRetryEnabled_
  ReturnsRetryInProcess` (asserts delay in [4m, 6m] = 5m ± 20%) and
  `DelayPlannerTests.PickClassified_…` plus the jitter-fraction test.

- **SC7 — Planted NRE → FailFast PROGRAMMER_BUG non-retryable**:
  PASS. `DefaultFailurePolicyTests.DecideAsync_ProgrammerBug_
  NullReferenceException_ReturnsFailFastWithProgrammerBug` (lines
  108-120) asserts ErrorCode="PROGRAMMER_BUG", IsRetryable=false,
  Message contains "NullReferenceException". Test passes. Confirmed
  against source at `DefaultFailurePolicy.cs:80-88`.

- **SC8 — OperationCanceledException → CancelWithoutPublish (D7
  preserved)**: PASS. `DefaultFailurePolicyTests.DecideAsync_
  OperationCanceled_…` (lines 38-49) asserts ErrorCode=
  "OPERATION_CANCELLED". Cancellation check at
  `DefaultFailurePolicy.cs:70-78` fires before the bug branch
  (per D-P2-13 ordering).

- **SC9 — Other `DefaultFailurePolicyTests` branches pass unchanged**:
  PASS. 13 facts in `DefaultFailurePolicyTests` — all 13 passed in
  the filtered run. Branches covered: cancel, transport, classifier
  retryable+enabled, classifier retryable+explicit-planner, planted-NRE,
  precedence-over-transport, public ctor, watermark-floor, no-watermark,
  retry-disabled fallback, defensive fallback, Instance singleton.

- **SC10 — `AdapterFlowRunnerFacadeTests` (14 tests) pass unchanged**:
  PASS. Filtered run: `Passed: 14, Skipped: 0, Total: 14, Duration:
  84 ms`. Note the contract says "14" but
  `AdapterFlowRunnerFacadeTests.cs` declares 13 `[Fact]`/`[Theory]`
  attributes; theory inlining yields 14 dispatched cases.

- **SC11 — LoC budget**: PASS. `git diff --numstat | awk …` per
  D-P2-16 yields `added=327 deleted=102 net=225`. Under stretch (250)
  and far under hard ceiling (600). Matches executor's claim exactly.

- **SC12 — No edits to out-of-scope files**: PASS. `git status`
  shows only 11 modified files, all in the contract's "Files
  modified" set plus the test files plus the flagged
  `DummyCollector.cs`. `Directory.Packages.props` (SDK 2.0.26
  confirmed), all five `Directory.Build.props` files, and every other
  collector are untouched. `IsUnknownRetryCandidate` body, `ShouldHandle`
  body, and `ClassifiedRetryableTriggerException` class declaration
  all confirmed unchanged via `git diff HEAD`. No phase-0a/0b/1
  artifacts touched. `plan.md` untouched.

- **SC13 — `state.json` updated to status `executed`**: FAIL (minor).
  `state.json` still shows `"status": "signed_off_executing"`. The
  `verifierRun` and `codeReviewerRun` fields are not present
  (intentional per contract — they are set after this verifier and
  the code-reviewer run), but the `status` itself should have been
  flipped to `executed` by the executor. Also `requiredFiles` still
  shows `"orchestration_plan.md": "pending"` and
  `"execution_notes.md": "pending"` although both files exist on
  disk. Orchestrator should update this.

## D-P2-N Decision Honour Check

- **D-P2-7** (`IsBug` true for 7 exception types): HONOURED.
  `ProgrammerBugClassifier.cs:20-24` lists exactly the right
  types; tests confirm the 7-type matrix.

- **D-P2-8** (Optional `DelayPlanner? planner` parameter on both
  pipelines): HONOURED. Both `UnknownFlowRetryPolicy.cs:53-54,
  62-67` and `ClassifiedRetryablePolicy.cs:69-70, 81-86` add the
  4-arg overload and keep the 3-arg as a thin delegating overload.
  Source-compatible for the 16 collector call sites.

- **D-P2-9** (`DefaultFailurePolicy` ctor public, accepts
  `DelayPlanner`): HONOURED. `DefaultFailurePolicy.cs:62-65`
  declares `public DefaultFailurePolicy(DelayPlanner planner)` with
  null-guard. `Instance` initialized at line 56. Public-API
  change is explicitly documented in `execution_notes.md`.

- **D-P2-10** (Ctor signature: random + jitterFraction +
  unknownDelays + classifiedDelays): HONOURED.
  `DelayPlanner.cs:42-46` matches exactly. Jitter range check at
  line 48 enforces `[0.0, 1.0]`.

- **D-P2-11** (`DelayPlanner.Default` singleton with 0.20 jitter):
  HONOURED. `DelayPlanner.cs:26`. Polly pipelines use it when
  caller passes null (confirmed at `UnknownFlowRetryPolicy.cs:72`
  and `ClassifiedRetryablePolicy.cs:91`).

- **D-P2-12** (PROGRAMMER_BUG as inline string literal, no
  constants file): HONOURED. Literal appears at
  `DefaultFailurePolicy.cs:84` and `AdapterFlowRunner.cs:771, 1255`.
  No new constants file created. Recorded in `execution_notes.md`
  per verifier LOW 2.

- **D-P2-13** (ProgrammerBug branch fires AFTER cancel, BEFORE
  transport): HONOURED. `DefaultFailurePolicy.cs:70-98` — order is
  cancel → bug → transport. Test
  `DecideAsync_ProgrammerBug_TakesPrecedenceOverTransportClassification`
  asserts this.

- **D-P2-14** (FailFast PROGRAMMER_BUG log level `LogError` in
  runner): HONOURED. `AdapterFlowRunner.cs:771-779` (fresh-run arm)
  and `:1255-1263` (resume arm) both emit `logger.LogError` when
  `failFast.Handling.ErrorCode == "PROGRAMMER_BUG"`. Non-PROGRAMMER_BUG
  FailFast paths unchanged (no log emission added).

- **D-P2-15** (`IsUnknownRetryCandidate` NOT changed to short-circuit;
  21-min wasted retry accepted): HONOURED.
  `UnknownFlowRetryPolicy.cs:108-128` is the pre-P2 body verbatim.
  Acceptance surfaced in `execution_notes.md` lines 144-148.

- **D-P2-16** (LoC measured via `git diff --numstat | awk`):
  HONOURED. `225` net add measured by the prescribed formula.

## Out-of-Scope Verification

- `phase-0a/`, `phase-0b/`, `phase-1/`: untouched
  (git status shows no changes under `ai/`).
- `plan.md`: untouched.
- `Directory.Packages.props`: untouched. SDK still 2.0.26 (verified
  by grep at line 7).
- All 5 `Directory.Build.props` files: untouched (git diff returns
  empty).
- `UnknownFlowRetryPolicy.IsUnknownRetryCandidate` body: lines
  108-128, byte-identical to baseline. Git diff confirms only the
  `using` add, `CreatePipeline` signature/delegate-overload, and
  the `DelayGenerator` body changed.
- `ClassifiedRetryablePolicy.ShouldHandle` body: unchanged (git
  diff shows only `using`, signature/overload, and DelayGenerator
  changes).
- `ClassifiedRetryableTriggerException` class declaration: unchanged.
- No collector file modified besides `DummyCollector.cs`.

## D-P2-6 Spirit Check (DummyCollector edit)

`Collectors/DummyCollector/DummyCollector.cs` lines 211-218: the
`FlowRetryPipelineBuilder` lambda performs a service-locator lookup
for a registered `DelayPlanner`:

```csharp
var planner = _context.Services?.GetService(typeof(DelayPlanner)) as DelayPlanner;
return UnknownFlowRetryPolicy.CreatePipeline(logger, vendor, flow, planner);
```

This is a test-injection hook with NO behaviour change in
production: production hosts do not register a `DelayPlanner` in
the service collection, so the lookup returns null, and the
4-arg `CreatePipeline` falls back to `DelayPlanner.Default` (jitter
0.20) — byte-identical to using the 3-arg overload directly. The
production effective behaviour is identical to a host without this
edit. The spirit of D-P2-6 ("no failure-policy changes on cloud
collectors; collectors don't drive failure policy until P4") is
honoured: DummyCollector is a test scaffold, not a tenant-facing
collector, and the edit is a test-injection hook rather than a
collector-level failure-policy change. The executor flagged this
correctly in `execution_notes.md`.

## Findings

- (LOW) `state.json` status field still reads
  `signed_off_executing` and `requiredFiles.orchestration_plan.md`
  / `requiredFiles.execution_notes.md` still read `pending` despite
  both files existing on disk. Should be flipped to
  `executed` / `complete`. [phase-2/state.json:7, :18-19]

- (LOW) Contract text expected `UnknownFlowRetryPolicyDelaysTests`
  to gain "jitter assertions with seed=42" and
  `ClassifiedRetryablePolicyTests` "same shape". Neither file has
  such tests — both retain only their pre-P2 delay-invariant /
  pipeline-shape suites. The semantic coverage was placed in
  `DelayPlannerTests` (jitter math) and `DefaultFailurePolicyTests`
  (planner-driven delay range) instead. No coverage gap on the
  contract's spirit; placement diverges from the contract's letter.
  [`UnknownFlowRetryPolicyDelaysTests.cs:1-60`,
  `ClassifiedRetryablePolicyTests.cs:107-122`]

- (LOW) SC5/SC6's "no-planner overload preserves identity with P1
  baseline RetryDelays" tension with D-P2-11 ("Default has 0.20
  jitter"). D-P2-11 is the operator-promoted decision and wins. The
  no-planner overload now yields jittered delays, which is the
  intended production behaviour. Surfaced for transparency; no
  repair needed.

## Repair Recommendations

1. Orchestrator should update `phase-2/state.json` after this
   verifier and the subsequent code-reviewer complete:
   - `status`: `signed_off_executing` → `executed`
   - `requiredFiles.orchestration_plan.md`: `pending` → `complete`
   - `requiredFiles.execution_notes.md`: `pending` → `complete`
   - Add `verifierRun` field (this report's metadata)
   - Add `codeReviewerRun` field after the code-reviewer subagent runs

2. (Optional, low priority) Consider adding a jitter-assertion test
   to `UnknownFlowRetryPolicyDelaysTests` and
   `ClassifiedRetryablePolicyTests` that exercises the 4-arg
   `CreatePipeline` overload directly with a fixed-seed planner.
   This would satisfy the contract's letter and double-check the
   pipeline → planner wiring without going through
   `DefaultFailurePolicy`. Not required to consider Phase 2 done —
   the existing coverage already proves the wiring works.

## Accepted Risks

- **D-P2-15 (NRE → ~21-minute retry burn through unknown-retry
  path)**: Confirmed accepted. The unknown-retry pipeline does not
  short-circuit on `ProgrammerBugClassifier.IsBug`; an NRE thrown
  from a flow's main body burns through the full 60s+5m+15m retry
  envelope before the policy's FailFast(PROGRAMMER_BUG) decision
  fires. Surfaced in `execution_notes.md` lines 144-148 as
  required. Operator chose "stick to the plan"; the optional
  A-P2-13 short-circuit is deferred (no phase scheduled).

## Unresolved Items

None blocking. The two LOW findings above are housekeeping (state.json
status flip + test-placement preference) and do not affect whether
Phase 2 has met its goal. Substance of every Success Criterion and
every D-P2-N decision is honoured in the source on disk.

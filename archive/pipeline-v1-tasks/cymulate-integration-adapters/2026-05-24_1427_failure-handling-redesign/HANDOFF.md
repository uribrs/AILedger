# Handoff — Failure-Handling Redesign

**Session ended:** 2026-05-24, after P0a + P0b + P1 + P2 + P2 patches +
P3 all in working tree (uncommitted).

**Branch:** `falcon-another-resume-layer`. **Baseline:** `f51ed3d`.
**HEAD:** `8d88c3e` (descendant).

## What's in working tree

| Phase | Status | Code surface |
|---|---|---|
| **P0a** | signed_off | AdapterFlowRunner facade (3 new files / 768 LoC) |
| **P0b** | signed_off_with_deferred_finding | Inline + delete old runners; 64 files / +466 net LoC |
| **P1** | signed_off | IFailurePolicy + DefaultFailurePolicy + ProgrammerBugClassifier (stub) + DelayPlanner (stub) + diagnostic guard; 1127 LoC |
| **P2** | executed_and_patched | Classifier body + planner jitter + Polly wiring; ~225 LoC + 2 P2 patches (D-P2-17, D-P2-18) |
| **P3** | executed_with_redesign | RetryBudget + cross-redelivery cap; ~548 LoC (production ~236, tests ~312); 113/113 pass |

**Build status:** 0 errors / 0 warnings.
**Test status:** 113/113 in `DummyCollector.Test` pass in ~250 ms.
**C10:** closed (was 21 min, now sub-second).

## Where to read the artifacts

```
ai/active/2026-05-24_1427_failure-handling-redesign/
├── state.json                  (root — updated with D8, marker-leak resolution)
├── plan.md                     (rev 4 — authoritative)
├── phase-0a/  ... signed_off
├── phase-0b/  ... signed_off_with_deferred_finding
├── phase-1/   ... signed_off
├── phase-2/   ... executed_and_patched (D-P2-17, D-P2-18 post-execution)
├── phase-3/   ... executed_with_redesign (D-P3-3 redesigned mid-execution)
└── HANDOFF.md (this file)
```

## P3 architecture summary (what to know first)

**`RetryBudget`** (`Shared/Failure/RetryBudget.cs`, 156 LoC, public
static class):
- 3 reserved keys under `_retry.` prefix in
  `AdapterCheckpoint.AdapterState`. Verified collision-free across
  all 19 collector checkpoint helpers.
- `Load(checkpoint, logger)` → `RetryBudgetSnapshot(int, string?, DateTime?)`.
  Tolerates bad/missing data; defaults to zero/null/null.
- `Write(progressContext, attemptCount, lastErrorCode, lastAtUtc, logger)`
  — in-memory SetState writes only (no AdvancePage flush — see "Critical
  trade-off" below).
- `Clear(progressContext, logger)` — writes empty strings to all three
  keys. Idempotent.

**`DefaultFailurePolicy`** ctor now accepts optional
`maxRetriesAcrossRedeliveries = 4` (range [1, 100]). When
`context.AttemptNumber >= cap` in the classified-retry branch, returns
`FailFast(handling with ErrorCode = "RETRY_BUDGET_EXHAUSTED",
IsRetryable = false)`.

**`AdapterFlowRunner`** wiring:
- Resume entry: loads budget after `RestoreProgress`; feeds
  `budget.AttemptCount` into `FailureContext.AttemptNumber`.
- Both paths' `FailureAction.RetryInProcess` arm: calls
  `RetryBudget.Write` before sleeping / rethrowing marker.
- Both paths' success completion: calls `RetryBudget.Clear`.

## Critical trade-off in P3 (D-P3-3 — REDESIGNED mid-execution)

**Initial design** (plan-Prereq-1 option 2): `RetryBudget.Write` and
`Clear` called `progressContext.AdvancePage(0, 0)` after their
`SetState` calls to trigger the host's `OnCheckpoint` persistence
callback.

**Problem** (caught by code-reviewer Blocker, verified by new
regression tests `Write_DoesNotChangeProgressCounters` and
`Clear_DoesNotChangeProgressCounters`): SDK 2.0.26's
`AdapterProgressContext.AdvancePage(int, int)` **always increments
the page counter** regardless of its args. `AdvancePage(0, 0)` would
silently advance `CurrentPage` by 1 on every retry transition,
corrupting the checkpoint's page contract.

**Final design** (plan-Prereq-1 option 3 — best-effort):
`RetryBudget.Write` and `Clear` mutate `progressContext.AdapterState`
in-memory only. Durability piggy-backs on the next natural
`AdvancePage` call in the flow (typically the next successful batch
publication). On worker crash during a retry sleep BEFORE any
subsequent `AdvancePage` fires, the budget is lost; redelivery starts
with fresh budget. **C6 weakens from hard cap to best-effort cap.**

Documented in `RetryBudget.cs` xmldoc. The regression tests now pass.

## Open issues for next agent (priority-ordered)

### Highest priority — must address before P4

1. **A-P3-13 — platform-team consumer check.** Does any downstream
   pipeline (analytics, dashboards, reports) iterate
   `AdapterState` without filtering underscore-prefixed keys? If yes,
   `_retry.*` keys leak into downstream rendering. Confirm with
   platform team before Falcon ships with classified-retry enabled in
   P4. No correctness/security risk if assumption breaks — just
   visibility noise.

2. **P2 code-reviewer Major #2 (terminal-failure paths).** Resume's
   `FailFast` (non-success), `PartialSuccess`, and `PublishAndExit`
   arms do NOT call `RetryBudget.Clear`. Intentional for
   `RETRY_BUDGET_EXHAUSTED`; unintentional leak for other terminal
   failures. Low impact pre-P4 (no production caller enables
   classified-retry yet). P4 contract should decide: clear on all
   terminal paths, or clear only on success.

### Medium priority — track for P4 / P5

3. **P2 code-reviewer Major #3 (OCE on resume).** The runner's
   policy-invocation gating admits any
   `OperationCanceledException` into `policy.DecideAsync` without
   checking `cancellationToken.IsCancellationRequested` first. P1-
   introduced behaviour; not a P2/P3 regression. Could surface as
   "false-cancel" if a collector throws OCE for non-user-cancel
   reasons (HTTP timeouts).

4. **P3 code-reviewer Major (resume FailureContext: no LastErrorCode
   / LastAtUtc fed back).** P3 ships with `RetryBudget.Load` returning
   the full snapshot, but only `AttemptCount` is fed into
   `FailureContext` (D-P3-14 — no new fields). `LastErrorCode` and
   `LastAtUtc` are persisted but never consumed today. If a future
   policy wants to weight "consecutive same-error" differently, those
   fields are pre-positioned.

### Low priority — fit-and-finish

5. Magic string `"RETRY_BUDGET_EXHAUSTED"` at one site in
   `DefaultFailurePolicy.cs`. Promote to const if reused elsewhere.
6. Magic string `"PROGRAMMER_BUG"` at three sites
   (`DefaultFailurePolicy.cs`, two `LogError` calls in
   `AdapterFlowRunner.cs`). Same — promote to const.
7. `DelayPlanner` Random thread-safety doc (P2 code-reviewer Major
   #4). Production callers use `Random.Shared`; theoretical risk only.

## Inconsistencies to clean up

- Source task root `state.json` `phases[]` rows for P2 and P3 still
  carry their original `status: "not_started"` strings; only the
  top-level `status` was bumped to
  `p0a_p0b_p1_signed_off_p2_pending_contract` after P1. The next
  agent should bump these to reflect P2 + P3 completion (and update
  `currentPhase` to "P4 next").

## Recommended next step

**Path A (safest)**: Operator confirms A-P3-13 with platform team
BEFORE drafting P4. Update source task root `state.json` to reflect
P2 + P3 completion. Then `prompt-contract-designer` for P4.

**Path B (riskier, faster)**: Skip platform-team confirmation; draft
P4 with the underscore-filter assumption documented; ship Falcon
classified-retry under a feature flag so a downstream-leak surprise
can be turned off quickly.

**Recommend Path A** — Falcon classified-retry is the FIRST production
caller of the failure policy's RetryInProcess branch. Worth getting
the durability and key-naming story straight before going live.

## Files modified in this session (uncommitted)

```
src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/
├── Failure/
│   ├── DefaultFailurePolicy.cs     (P2 + P3 modifications)
│   ├── DelayPlanner.cs             (P2 modification)
│   ├── FailureContext.cs           (P2 patch D-P2-18)
│   ├── ProgrammerBugClassifier.cs  (P2 modification)
│   └── RetryBudget.cs              (P3 new file)
├── Orchestration/
│   └── AdapterFlowRunner.cs        (P0b + P1 + P2 patches + P3 modifications)
└── Session/TransportErrorHandling/
    ├── ClassifiedRetryablePolicy.cs (P2 modification — 4-arg overload)
    └── UnknownFlowRetryPolicy.cs    (P2 modification — 4-arg overload)

src/Cymulate.Integration.Adapters/Collectors/DummyCollector/
└── DummyCollector.cs                (P2 — test-injectable DelayPlanner hook)

src/Cymulate.Integration.Adapters/UnitTests/Collectors/
└── Cymulate.Integration.Adapters.Collectors.DummyCollector.Test/
    ├── DefaultFailurePolicyTests.cs   (P2 + P3 modifications)
    ├── DelayPlannerTests.cs           (P2 rewrite)
    ├── ProgrammerBugClassifierTests.cs (P2 rewrite)
    ├── DummyCollectorTests.cs         (P2 zero-delay planner injection)
    ├── RetryBudgetTests.cs            (P3 new file, 17 tests)
    └── (others unchanged)
```

All P0a/0b/1 changes are also in working tree from earlier sessions —
see `phase-*/execution_notes.md` for those scopes.

## Subagent usage pattern that worked well

For P3 contract drafting, 3 parallel `Explore` subagents resolved
external-behaviour questions in one round-trip:

- **W1** SDK `AdapterProgressContext` API survey (xmldoc).
- **W2** `AdapterCheckpoint`/`CheckpointAdapter` + existing
  `*CheckpointHelper` patterns (1-2 collector examples).
- **W3** `AdapterFlowRunner.cs` injection-point line-number map.

This saved sequential reads of ~5-7 files (1000+ lines) and pinned
every external API contract before the contract was finalized. Recommend
the same pattern when P4 needs to survey Falcon's classified-retry
wiring + Falcon's policy-construction site.

## How to resume this work

```bash
cd /Users/user/Dev/cymulate-integration-adapters
git status                    # confirm uncommitted P0a-P3 changes
git diff --numstat | tail -5  # confirm scale
dotnet build                  # should be 0/0
dotnet test src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.DummyCollector.Test/ # should be 113/113
```

Read in order:
1. `plan.md` rev 4 §6 Phase 4 (lines 664-678) — Falcon classified-retry enable.
2. `phase-2/decisions.md` D-P2-17 (programmer-bug gating extension).
3. `phase-3/decisions.md` D-P3-3 (best-effort durability trade-off).
4. `state.json` D8 (marker-leak twin-fix clarification).
5. This file's "Open issues" section.

Then draft P4 contract.

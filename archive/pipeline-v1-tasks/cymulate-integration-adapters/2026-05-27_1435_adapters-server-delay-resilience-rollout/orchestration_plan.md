# Orchestration Plan

## Complexity Decision

- Path: **decompose** (two sequential single-worker phases)
- Rationale: Phase A and Phase B touch disjoint file sets and have independent commit boundaries; sequential single-worker execution keeps reverts clean. No parallel work — the prior P3 round already proved 15-collector parallelism works; this round is much smaller (~21 file edits total).

## Research Decisions

- None needed. All assumptions resolved or scoped out:
  - A1-A11 (prior round): VALIDATED or scoped out.
  - A12 (`Continue` with no-op continuation is safe): VALIDATED.
  - A13 (orphaned validation classifier porting): scope dropped per operator (no per-vendor classifier work in this round).
  - A14 (`defaultTransportBackoff`): scope dropped (no consumer without per-vendor classifiers).
  - A15 (parallel worker partition): not applicable (no parallel workers).

## Worker Plan

- **W-PA** (sequential, single worker) — Foundation fixes closing B1/B2/M1/M2.
  - Inputs: prior phase commits `e11ade6`, `a23baf0`, `da2543e`, `492cf97` (HEAD).
  - Output: single commit `fix(resilience): honor PartialResult for hook-less collectors and add executor regression guard`.
  - Allowed files (only these):
    - `Shared/Cymulate.Integration.Adapters.Shared/Resilience/Policies/ServerSuggestedRetryDelayPolicy.cs` (MaxRetries=3)
    - 15 × `Collectors/<X>Collector/<X>Collector.cs` (RecoveryHandler swap to Continue)
    - `UnitTests/Collectors/.../DummyCollector.Test/AdapterFailureDecisionExecutorTests.cs` (new executor end-to-end test)
    - `UnitTests/Collectors/.../DummyCollector.Test/AdapterResilienceStrategyTests.cs` (assertion bumps)
  - Dependencies: none.
  - Phase gate: full solution build + DummyCollector filtered tests + per-collector smoke tests + Falcon full suite, all green.

- **W-PB** (sequential, single worker, depends on W-PA) — Cleanup + docs.
  - Inputs: W-PA committed.
  - Output: single commit `docs(resilience): document hybrid retry boundary and clarify Continue/Decline executor branch`.
  - Allowed files (only these):
    - `Shared/Cymulate.Integration.Adapters.Shared/Resilience/README.md` (rewrite hybrid retry boundary)
    - `Shared/Cymulate.Integration.Adapters.Shared/Resilience/Policies/ServerSuggestedRetryDelayPolicy.cs` (docstring only)
    - `Shared/Cymulate.Integration.Adapters.Shared/Resilience/Logic/AdapterFailureDecisionExecutor.cs` (docstring only)
  - Dependencies: W-PA.
  - Phase gate: full solution build + DummyCollector + Falcon full suites, all green.

## Synthesis Approach

Sequential, two commits. After W-PA returns: verify Phase A gate, then dispatch W-PB. After W-PB returns: verify Phase B gate. Then verifier + code-reviewer subagents in that order. Code-reviewer in isolation per skill rules — no contract / verifier output context.

## Verification Obligations

- Phase A: new executor end-to-end test in `AdapterFailureDecisionExecutorTests` passes (B1/M1 regression guard).
- Phase A: 15 per-collector smoke tests still green (B1/B2 fix doesn't break the prior Phase 3 work).
- Phase A: Falcon's full test suite green (no-regression — Falcon's `RecoverFreshAsync` is unchanged but the `MaxRetries=3` shared policy change touches its decision space too).
- Phase B: full solution build clean, README accurately describes the actual policy chain + retry boundary.
- Verifier (run 2) at `review/verifier-2.md`: pass against contract Success Criteria + the simplified-scope authority of `state.json` workflow + plan file.
- Code-reviewer (run 2) at `review/code-reviewer-2.md`: independent review of the diff, isolated context.

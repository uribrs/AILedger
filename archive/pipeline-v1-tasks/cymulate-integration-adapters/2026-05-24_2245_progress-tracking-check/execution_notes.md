# Execution Notes

## Synthesis sources
- `ai/active/2026-05-24_1427_failure-handling-redesign/HANDOFF.md`
- `ai/active/2026-05-24_1427_failure-handling-redesign/state.json`
- `ai/active/2026-05-24_1427_failure-handling-redesign/phase-{0a,0b,1,2,3}/state.json`
- `plan.md` rev 4 §6 (phase index)

## Phase-by-phase status (5 of 7 done, 2 remain)

| Phase | Plan §6 line | Status | Delivered | Verifier | Code-rev | Key signed-off decision |
|---|---|---|---|---|---|---|
| **P0a** | 484-510 | signed_off | 3 files / 768 LoC | ×2 PASS | ×1 APPROVE-WITH-NOTES | Facade `AdapterFlowRunner` with no API removed |
| **P0b** | 512-544 | signed_off_with_deferred_finding | 64 files / +466 net LoC | ×1 PASS | ×1 (1 Major) | Inlined + deleted 14 `<Vendor>ResumeRunner.cs` + shared runners. Major: `UnknownFlowRetryPolicy.IsUnknownRetryCandidate` marker-exclusion fix scheduled to P4 |
| **P1** | 546-599 | signed_off | 10 new + 3 modified / 1127 LoC | ×1 PASS | ×1 (0 blockers, 0 majors) | `IFailurePolicy` + `DefaultFailurePolicy` + stub classifier/planner + diagnostic guard wired at both catch sites |
| **P2** | 601-621 | executed_and_patched | ~225 LoC + 2 post-execution patches | ×1 PASS | ×1 REQUEST-CHANGES → repaired | D-P2-17 (programmer-bug runner gating) + D-P2-18 (`AttemptNumber` 0-based normalization) reverse the original D-P2-15 accept-risk stance |
| **P3** | 623-662 | executed_with_redesign | ~548 LoC (production ~236, tests ~312) | ×1 PASS | ×1 REQUEST-CHANGES → mid-flight redesign | D-P3-3 fell back from option (2) explicit flush to option (3) best-effort durability after `AdvancePage(0,0)` side-effect verified via regression test |
| **P4** | 664-678 | not_started | — | — | — | Falcon switches `DefaultFailurePolicy` → tuned policy enabling classified-retry. Marker-exclusion fix lands here (carried from P0b) |
| **P5** | 680-694 | not_started | — | — | — | Migrate 13 cloud collectors; converge resume path to Shape A; delete `ClassifiedRetryableTriggerException`; close C8 unconditionally |

**Done: 5/7 (71%).** Remaining: P4 (Falcon canary, ~50-100 LoC) and P5 (cloud-collector sweep + marker removal, ~250-350 LoC).

## Quality signals

- **Build**: 0 errors / 0 warnings (full solution, post-P3 redesign).
- **Tests**: 113/113 pass in `DummyCollector.Test` project in ~112-250 ms.
- **C10 closed**: previously-21-min `DummyCollectorTests` now sub-second.
- **No SDK bump leak**: Phase 0b's accidental bump reverted; SDK stays at 2.0.26 across P0a-P3.
- **No out-of-scope file touched** across any phase's execution per each verifier confirmation.
- **Working tree**: clean by name (only the expected modified files); no commits this session.

## On-track judgment

**Yes — on track**, with two qualifiers operator should know about:

1. **Original C6 ("hard cross-redelivery cap") weakened to "best-effort cap" in P3** because SDK 2.0.26 has no flush-without-page-advance primitive. Code-reviewer Blocker forced D-P3-3 to fall back from plan-Prereq-1 option (2) to option (3). The plan explicitly listed option (3) as an acceptable resolution, so this is within plan latitude — but it IS a downgrade from the strongest variant. Documented in `RetryBudget.cs` xmldoc and `phase-3/decisions.md`.

2. **Working tree carries 5 phases of uncommitted changes.** No commits this session. Per the plan's "Phases 1–4 are designed to be independently shippable" (§6 line 693), each phase SHOULD ship as its own PR. Currently all phases are stacked in one working tree. Operator owns commit cadence (per `phase-2/constraints.md` line 3).

Reasoning for "on track" despite the qualifiers:
- 5 of 7 phases done in one session — well ahead of any reasonable
  per-phase cadence.
- Every quality gate (build, tests, verifier, code-reviewer) cleared
  with documented repairs.
- All 4 outstanding code-reviewer concerns from P2 + P3 are either
  repaired in-place or documented as accepted risks (no silent gaps).
- P4 and P5 scopes are small (P4 ~50-100 LoC, P5 ~250-350 LoC) and
  have no open architectural questions left.

## Top 3 risks for P4 + P5

1. **A-P3-13 platform-team consumer check still open.** P4 ships
   Falcon with classified-retry — first production caller of the
   `_retry.*` keys. If downstream consumers (analytics, dashboards)
   render `AdapterState` without filtering underscore-prefixed keys,
   the keys leak. No correctness risk; visibility/log noise only.
   Should be confirmed with platform team BEFORE P4 ships.

2. **P5 LoC estimate not audited.** Plan estimates ~250-350 LoC net
   for the cloud-collector migration, but explicitly flags this as
   "not verified line-by-line" (plan.md line 431-ish per earlier W4
   reading). The 13 collectors each carry their own classifier and
   partial-success builder; the sweep may surface more incidental
   work than the estimate captures.

3. **Best-effort C6 — first real production stress test in P4.**
   Falcon's classified-retry will be the first time the retry budget
   actually pages across redeliveries. If a worker crash during a
   retry sleep loses the budget AND the platform aggressively
   redelivers, the cap can be exceeded by up to the cap value per
   crash. Tolerable for a canary; worth watching during the Phase 4
   production cycle (plan §6 line 674: "Watch one production cycle
   before proceeding to Phase 5").

## Recommended next move

**Confirm A-P3-13 with the platform team** before drafting the P4
contract. Two specific questions:

1. Does any downstream pipeline iterate `AdapterCheckpoint.AdapterState`
   without filtering underscore-prefixed keys?
2. Acceptable retention story for `_retry.*` keys on terminal failure
   (per P2 Major #2 carry-over) — clear on all terminal paths, or
   only on success?

Then `prompt-contract-designer` for P4. P5 follows after P4's
production canary.

# Orchestration Plan

## Complexity Decision
- Path: decompose
- Rationale: Four independent artifact-reading workstreams (phase-0a, phase-0b,
  phase-1, plan.md P2-P5 extraction) with no inter-dependency; synthesis is
  trivial concatenation. Parallel subagents materially reduce wall time vs
  sequential reads, and each scope is sharp enough for a single-shot Explore
  agent. Direct path would have serialized four reads for no benefit.

## Research Decisions
- None needed. This is a synthesis task over local artifacts; no external
  vendor / library / protocol behavior is in question.

## Worker Plan
- W1 — scope: Phase 0a status summary. inputs:
  `phase-0a/{state.json,execution_notes.md,review/*}`. output: <=250-word
  bullet summary covering status, deliverable, deferred, verifier+code-reviewer
  passes, unfinished items. dependencies: none.
- W2 — scope: Phase 0b status summary (same shape as W1 plus orchestrator
  repairs). inputs: `phase-0b/{state.json,execution_notes.md,review/*}`.
  output: <=250-word summary. dependencies: none.
- W3 — scope: Phase 1 status summary (same shape as W1 plus Shape A / Shape B
  status + UnknownFlowRetryPolicy deferral status). inputs:
  `phase-1/{state.json,execution_notes.md,review/*}`. output: <=300-word
  summary. dependencies: none.
- W4 — scope: Extract scope, file targets, LoC budget, and prerequisites for
  the unstarted phases P2, P3, P4, P5 from `plan.md` (rev 4). Also list
  open / lived-with concerns. inputs: `plan.md`. output: <=600-word structured
  bullets. dependencies: none.

## Synthesis Approach
Main thread concatenates W1-W4 outputs into three sections (Done / Remaining /
Inconsistencies & Next Step). Cross-checks per-phase `state.json` against root
`state.json` to surface drift. Cross-checks P0b deferred-finding schedule
("Phase 4 prerequisite") against W4's reading of plan.md ("not scheduled —
P5 only") and surfaces the contradiction.

## Verification Obligations
- Cross-check against `prompt_contract.md` Success Criteria 1-5.
- Confirm LoC and file-count numbers in the report match verbatim what the
  worker outputs lifted from the artifacts.
- Confirm every unstarted-phase prerequisite from `plan.md` appears in the
  report.
- Confirm the surfaced inconsistencies are real (re-read the conflicting
  artifacts in the verifier pass).
- Code-reviewer skipped — task is not code-bearing.

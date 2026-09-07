# Prompt Contract — Failure-Handling Status Report

Role:
You are a senior .NET engineer auditing the in-flight failure-handling-redesign task.

Goal:
Produce an accurate, terse, operator-readable status report on
`ai/active/2026-05-24_1427_failure-handling-redesign/`:
what has been done, what remains, and which inconsistencies must be reconciled
before the next phase begins.

Context:
- Source task tracks a 6-phase (P0a, P0b, P1, P2, P3, P4, P5) refactor of the
  collector failure-handling pipeline introducing `IFailurePolicy` and merging
  `AdapterBusEntrypointRunner` + `CollectorResumeRunner` into a single
  `AdapterFlowRunner`.
- Working branch: `falcon-another-resume-layer`; head `f51ed3d`.
- The source task root `state.json` may lag the latest per-phase `state.json`
  files (root last updated 15:35; phase-1 closed at 22:30 UTC). Trust per-phase
  artifacts over root when they conflict.

Constraints:
- Read each phase's own `state.json`, `execution_notes.md`, and
  `review/verifier-N.md` / `review/code-reviewer-N.md`. Do not infer phase
  status from the root state alone.
- For unstarted phases, read `plan.md` (rev 4) directly — do not guess from
  prior reviewer language.
- Quote LoC, file counts, verifier/code-reviewer pass counts, and deferred
  findings verbatim from the artifacts. Do not round, paraphrase, or
  reconstruct.
- Surface every inconsistency between the root state, per-phase states, and
  plan.md. Do not silently resolve them.
- No production code changes. No edits to the source task directory.

Success Criteria:
1. For each of P0a, P0b, P1: status, deliverable LoC and file counts, verifier
   pass count, code-reviewer pass count, deferred findings, and any
   orchestrator repairs that occurred.
2. For each of P2, P3, P4, P5: concrete code changes required, files expected
   to change, LoC budget if stated, and any prerequisites or open questions
   that must be resolved before the phase contract can be drafted.
3. Explicit list of deferred / lived-with concerns (C7, C9, C12, C8/C11
   conditionals, marker-leak chain-walk fix).
4. Explicit list of inconsistencies between root `state.json`, per-phase
   `state.json` files, and `plan.md` — including the P0b → P4 vs P5
   marker-leak scheduling contradiction and the root-state staleness.
5. One concrete recommended next step, with rationale.

Execution Rules:
- Run the per-phase reads in parallel via Explore subagents.
- Quote artifacts verbatim where numeric.
- Do not propose fixes for the source task's open questions; only surface them.

Output Format:
- Three top-level sections: Done / Remaining / Inconsistencies & Next Step.
- Bullet form. No headers below H2. No prose paragraphs over 3 lines.

Stop Conditions:
- Per-phase `state.json` is missing or unreadable.
- A success criterion cannot be answered from artifacts (escalate, do not guess).

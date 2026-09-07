# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: One coherent edit (~3 lines) in a single method, plus three
  local decisions (A2/A3/A5) that all depend on the same reading of the same
  file. Worker boundaries would be fuzzy and coordination overhead would exceed
  the work. Scored low on Separability, high on Coupling.

## Research Decisions
- None needed. Every OPEN assumption (A1–A5) is answerable by reading this
  repository — no vendor API, library internal, or protocol behavior is in
  question. `researchNeeded = false`.
- A1 was resolved in the main thread **before** the path decision, because it
  gated the scope itself rather than the execution: had `CybiBatchUploader`
  retained the rows, the approved fix would have relocated the leak and scope
  would have needed to be reopened with the user. It does not — see
  `assumptions.md` A1 (VALIDATED) and `decisions.md` D7.

## Worker Plan
Not applicable — direct path.

## Synthesis Approach
Not applicable — direct path.

## Verification Obligations
- Cross-check against every Success Criterion in `prompt_contract.md`.
- Byte-identical output: confirm nothing in the emission path (build, enqueue,
  serialize, upload) changed — only reference ownership.
- Confirm the release cannot skip an asset: it must sit after `AddAsync`
  returned, unconditionally, for every index in the batch window.
- Confirm index safety: `iAssets.Count` unchanged, no `Remove`/`RemoveRange`,
  `Skip(i)` never dereferences nulled slots.
- Confirm A2, A3, A5 were each explicitly resolved and recorded rather than
  silently chosen.
- Confirm the legacy StreamWriter branch either is untouched or was shown safe.
- Confirm deferred items were not "improved" in passing.
- Confirm two separate commits and that no `ai/` artifact was staged.
- Style: explicit types, no `i*` prefix in new code, comment carries the
  ownership invariant rather than restating the assignment.

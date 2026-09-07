# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: one coherent mechanism centered on `CybiBatchUploader` + a small
  executor arming hook + tests. Pieces are tightly coupled (wire format ↔
  counter ↔ close semantics); worker boundaries would be fuzzy and integration
  cost would exceed the work itself.

## Research Decisions
- None needed. A1 (opt-in key name) and A6 (granularity default) are
  operator-acknowledged placeholders — implementation parameterizes them.
  A2 (CyAgentServer passthrough) is explicitly out of scope; A3–A5, A7 are
  VALIDATED from source.

## Worker Plan
Not applicable — direct path.

## Verification Obligations
- Every Success Criterion in prompt_contract.md, notably:
  - dormant-path byte-identity proven by test (exact payload string), not inspection
  - UUID vectors independently recomputed (e.g. Python uuid.uuid5) and pinned as constants
  - announce-once-per-folder incl. completion-time close of the final partial folder
  - diff-surface audit: no interface / collector / Actions / shared-type changes
- Build + full existing test suite green; `dotnet format` conformance on touched files.
- Coding standards conformance (ReadMEs/coding-standards.md).

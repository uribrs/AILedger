# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: The fix is one new flatten-aware records resolver in `CollectorExecutor/Interpreter/Interpreter.cs`
  + two call sites (`ResponseMapper.Map`, `ExtractIds`) + tests. Tightly coupled, single file of substance —
  decomposition would only add coordination overhead.

## Research Decisions
- None needed. Defect is already proven this session (multi-host 0/3, single-host 2/2 via the real
  `XmlResponse.ToJson` → `ResponseMapper.Map` path); root cause at file:line; all `records_path` consumers
  enumerated. A1–A4 are internal design calls resolved by reading `Interpreter.cs`, not external research.

## Worker Plan
Not applicable — direct path.

## Synthesis Approach
Not applicable — direct path.

## Scope
- Add a NEW flatten-aware records resolver (e.g. `JsonNav.ListAtFlattened` or similar) that traverses arrays
  mid-path and concatenates. `At`/`StringAt` left byte-for-byte unchanged.
- Wire `ResponseMapper.Map` (+ `ExtractIds` per A3) to the new resolver.
- Resolutions to pin: A1 implicit-flatten; A2 emit objects, flatten array-of-arrays one more level, skip bare
  scalars at record position; A3 ExtractIds shares resolver; A4 single-object tolerance preserved.

## Verification Obligations
- Cross-check against prompt_contract.md SC1–SC5.
- SC2: 71 existing green + new tests (multi-host→3, single-host→2, array-of-arrays, cursor/next-token/watermark/
  capture regression-lock).
- SC3: qualys.yaml:47 findings emit across multiple hosts (harness test, multi-host+multi-detection XML mock).
- Guardrail audit: confirm `At`/`StringAt` are textually unchanged; confirm no vendor branch; confirm passthrough
  + envelope semantics untouched.
- Verifier must RUN the suite and confirm the 3/2 numbers itself. Code-reviewer must check for over-collection /
  infinite-loop risk on deeply nested structures and that shared nav was not altered.

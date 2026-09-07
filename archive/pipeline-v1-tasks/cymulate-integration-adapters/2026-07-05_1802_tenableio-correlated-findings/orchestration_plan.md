# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: single repo, single collector, one coherent rewrite; flow/checkpoint/config/tests are tightly coupled around the same types; a validated reference implementation (Falcon correlated flow) pins the idioms. Decomposition would create coordination overhead with no parallelism gain (contrast: the Falcon task had three disjoint workstreams across two repos).

## Research Decisions
- None needed. All external-behavior assumptions are VALIDATED from official Tenable docs + live probes on the largest tenant (assumptions.md A1–A8); OPEN items A10/A11 are internal inspection concerns assigned to execution.

## Worker Plan
Not applicable — direct path. Execution delegated as one contract-driven unit (full contract context), synthesis trivial.

## Synthesis Approach
Single execution unit — main thread reviews the diff and execution notes before verification.

## Verification Obligations
- Cross-check against prompt_contract.md Success Criteria (build, vstest coverage list, runner end-to-end, docs sync, version bumps, no Shared/ changes).
- Specifically verify: spool budget-overflow degrade emits chunk-0-then-host-less records; miss lane counted+logged and never buffers; claimed-first resume rebuild ordering; checkpoint round-trip at new format version; zero-vuln sweep only after all chunks; num_assets default 50; existing carried-over machinery (409 reuse, chunk retry/exclusion, MaxSkippedChunkRatio, progressive loop) still wired.
- Docs contain no stale two-lane descriptions.

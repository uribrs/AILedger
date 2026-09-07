# Orchestration Plan

## Complexity Decision
- Path: **direct**
- Rationale: One coherent unit — relocate 4 analyzed source files into Kernel, split one along a fixed
  seam (D2), add docs + a test project. Tightly coupled, small scope; worker boundaries would be fuzzy and
  decomposition would only add coordination overhead. Iteration in one context is preferable.

## Research Decisions
- None needed. Every assumption (A1–A4) is already VALIDATED by in-repo code-trace; A5 (test-project
  shape) is a non-blocking execution detail with a sensible default (xUnit). No external-system behavior
  is in question.

## Worker Plan
Not applicable — direct path.

## Synthesis Approach
Not applicable — direct path. `contract-driven-execution` produces the artifacts; the orchestrator
verifies and code-reviews.

## Verification Obligations
- Cross-check against the 8 Success Criteria in prompt_contract.md.
- Behavior preserved verbatim — classifier markers/socket-codes/chain-walk identical; exception property
  mapping identical; `IsUnknownRetryCandidate` logic identical.
- Kernel stays Polly-free: no `Polly` reference reachable from Kernel; `CreatePipeline` absent from
  IntegrationInfra.
- Source repo not mutated.
- `dotnet build` clean; tests pass; build/test output recorded in execution_notes.md.
- Then (code-bearing): an isolated code-reviewer pass with minimal context.

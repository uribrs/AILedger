# Orchestration Plan

## Complexity Decision
- Path: **direct**
- Rationale: Behavior-preserving refactor coupled to a single file (`RetryPolicy.cs`) plus a small XML-doc edit on two interfaces. Tight iteration in one context; decomposition would be pure coordination overhead. No independent parallelizable pieces.

## Research Decisions
- None needed. The only OPEN assumption (externalization invocation sites + decision precedence) is code-local and resolved by reading `RetryPolicy.cs`/`ServerSuggestedDelayExternalizer.cs`, not external-system behavior. `researchNeeded = false`.

## Worker Plan
Not applicable — direct path.

## Synthesis Approach
N/A (direct path). Execution via `contract-driven-execution`.

## Verification Obligations
- Cross-check all Success Criteria in prompt_contract.md.
- Behavior preservation is the bar: build green + `dotnet test Cymulate.Http.Package.sln` = 268/268 with existing retry/externalization tests unchanged.
- Externalization precedence preserved exactly after consolidating resolution to one point (verify the OPEN assumption).
- Sub-item 1 is docs-only (no signature/API change). No version bump. No commit. No unexpected CHANGELOG entry.

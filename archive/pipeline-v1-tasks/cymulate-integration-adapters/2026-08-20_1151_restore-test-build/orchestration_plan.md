# Orchestration Plan

## Complexity Decision
- Path: direct
- Axis scores: Complexity low | Separability low | Coupling high | Dependency order high | Execution risk low | Worker clarity high
- Rationale: Recon reproduced all 56 errors in one test project and traced them to one deleted `ProjectReference`. No independent file sets exist; the project edge and its compile consumers are one atomic repair.

## Research Decisions
- None needed. Every OPEN assumption is resolvable from repository history, MSBuild diagnostics, and local test execution; no external behavior controls the fix.
- Internal recon: complete → `research/internal-recon.md`

## File Ownership
- Disjoint sets found: 0
- No disjoint sets. Overlapping paths: `src/Cymulate.Integration.Adapters/UnitTests/Indicators/Cymulate.Integration.Adapters.Indicators.Defender.Test/Cymulate.Integration.Adapters.Indicators.Defender.Test.csproj` and its Defender test compile consumers. The single project edge controls every observed diagnostic, so implementation and verification cannot be partitioned into meaningful edits.
- Shared surface frozen: existing Defender production/test assembly contract, project paths, type signatures, package graph, and test bodies.

## Worker Plan
Not applicable — direct path.

## Verification Obligations
- Cross-check against `prompt_contract.md` Success Criteria.
- Build and run the Defender indicator test project.
- Build the full adapter solution after the focused repair.
- Confirm no production source, package references, or test assertions were changed.
- Run independent verifier and isolated code-reviewer passes.

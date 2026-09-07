# Orchestration Plan

## Complexity Decision
- Path: decompose
- Axis scores: Complexity medium | Separability high | Coupling low | Dependency order low | Execution risk medium | Worker clarity high
- Rationale: Recon identified three independent collector/test file sets with no overlap. Each can change its own default/path expectations against a frozen shared emission surface.

## Research Decisions
- None needed. All assumptions concern local configuration, emitter construction, paths, and tests; no vendor behavior is required.
- Internal recon: complete → `research/internal-recon.md`

## File Ownership
- Disjoint sets found: 3
- W1 owns: InsightVM Cloud configuration, assets/findings page publishers, `InsightVmCloudBatchScopedStorageTests.cs`, and `InsightVmCloudCollectorTests.cs` after bounded test/review repair extensions.
- W2 owns: Qualys configuration, findings batch publisher, and `QualysBatchScopedStorageTests.cs` after a bounded review repair extension.
- W3 owns: TenableIo correlated publisher/flow comments plus correlated flow and collector tests.
- Shared surface frozen in phase 0: IntegrationInfra `BatchScopedStorage`/`NdjsonBatchEmitter`, collector publisher APIs, Falcon behavior, pagination/checkpoint contracts, and test-project structure.

## Worker Plan
- W1 — scope: disable InsightVM Cloud default, align default/path assertions, and normalize legacy scoped resume metadata — owns: expanded InsightVM Cloud set — inputs: frozen surface, recon, and code-review finding — output: source/test diff plus focused/project test results — phase: 1 plus repair — continuity: resumed for findings on its own output
- W2 — scope: disable Qualys default, align its assertion, and normalize legacy scoped resume metadata — owns: expanded Qualys set — inputs: frozen surface, recon, and code-review finding — output: source/test diff plus focused/project test results — phase: 1 plus repair — continuity: resumed for findings on its own output
- W3 — scope: disable TenableIo hard-coded opt-in, align flat paths, and normalize legacy scoped resume metadata — owns: recon `tenable-io` set — inputs: frozen surface, recon, and code-review finding — output: source/test diff plus focused/project test results — phase: 1 plus repair — continuity: resumed for findings on its own output

## Synthesis Approach
Inspect each worker's exclusive diff, confirm no shared files changed, run the production-only literal-true scan, then build the solution and consolidate test evidence.

## Verification Obligations
- Cross-check against `prompt_contract.md` Success Criteria.
- Confirm no production collector default or emitter argument remains literal `true`.
- Confirm InsightVM Cloud and Qualys default assertions are false while explicit opt-in tests remain.
- Confirm TenableIo correlated initial, retry, and resume paths are flat and monotonically numbered.
- Run all three focused/project tests and the full solution build.
- Run independent verifier and isolated code-reviewer passes.

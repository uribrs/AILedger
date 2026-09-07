# Orchestration Plan

## Complexity Decision
- Path: direct
- Axis scores: Complexity low | Separability low | Coupling high | Dependency order high | Execution risk low | Worker clarity high
- Rationale: Recon found one tight configuration-to-flow-to-publisher seam whose test harness shares the constructor/configuration surface. Splitting it would create transient signature mismatches.

## Research Decisions
- None needed. All behavior is repository-local configuration and constructor wiring.
- Internal recon: complete → `research/internal-recon.md`

## File Ownership
- Disjoint sets found: 0
- No disjoint sets. Overlapping paths: TenableIo configuration record, correlated flow, publisher, configuration tests, and correlated-flow harness. The configuration value and constructor signature must change together.
- Frozen surface: shared emitter, storage layout, recovery/checkpoints, InsightVM Cloud and Qualys diffs.

## Worker Plan
Not applicable — direct path.

## Verification Obligations
- Confirm default `false` on TenableIo configuration.
- Confirm production flow forwards configuration and publisher contains no literal boolean.
- Confirm configured `true` reaches the emitter through the existing scoped-path flow test.
- Run focused and full TenableIo tests plus solution build.
- Run independent verifier and isolated code reviewer.

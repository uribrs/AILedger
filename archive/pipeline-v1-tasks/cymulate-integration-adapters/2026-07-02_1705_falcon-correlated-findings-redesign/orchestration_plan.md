# Orchestration Plan

## Complexity Decision
- Path: decompose
- Rationale: three workstreams with disjoint file ownership across two repos; only interface is the pinned record schema. W1+W2 stay fused (removing the lane machinery IS the rewrite). Parallel workers, centralized synthesis + end-to-end verification.

## Research Decisions
- None needed. All external-behavior assumptions are VALIDATED from live prototype runs (see assumptions.md); the two OPEN items (customer-scale re-anchor frequency, FalconRecoverySimulation scope) are internal inspection/test concerns assigned to worker A.

## Worker Plan
- **A — collector rewrite (W1+W2)** — scope: `Collectors/FalconCollector/**`, its test project, LocalAdapterRunner Falcon simulations review, FalconDocs/CollectorDocs, Collectors/README.md, ai/skills Falcon content. Inputs: prototype (behavioral spec), contract files. Output: correlated flow + reshaped checkpoint + tests + docs, adapters solution building, Falcon tests green. Dependencies: none. Runs in the main tree (owns the solution build).
- **B — parser dual-shape (W3)** — scope: `/Users/user/Dev/cymulate-integration-parsers` crowdstrike parser + yaml spec + tests. Inputs: pinned record schema, current parser files. Output: auto-detecting parser handling both shapes, tests green for both. Dependencies: none (schema pinned).
- **C — egress page hash (W4)** — scope: `Shared/.../DataPipeline/Egress/**` + its tests + READMEs. Output: streaming per-logical-object hash on the publish-completion log line. Dependencies: none. Runs in a worktree (avoids build collision with A); merged in synthesis.

## Synthesis Approach
Merge C's worktree change after A lands; full solution build + full Falcon/Shared test pass once (phase-boundary testing); then W6 end-to-end: LocalAdapterRunner collection on the lab tenant → B's parser ingests the output; parity check against the prototype-phase methodology.

## Verification Obligations
- Cross-check against prompt_contract.md Success Criteria (all six).
- Specifically verify: checkpoint round-trip + re-anchor paths under test; parser fixtures for BOTH shapes; hash identical for single vs multipart publication of same content; docs no longer describe lanes/segments; major version bump present.

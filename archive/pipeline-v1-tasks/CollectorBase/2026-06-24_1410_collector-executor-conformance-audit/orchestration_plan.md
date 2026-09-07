# Orchestration Plan

## Complexity Decision
- Path: decompose
- Rationale: four separable, low-coupling deliverables (native-reference baseline; one adversarial
  cross-verifier; three independent isolated code reviews; synthesis). Parallelizable; integration is a
  light synthesis in the main thread.

## Research Decisions
- None needed. All sources (native collectors, YamlCollector, Shared, CollectorExecutor) are local on disk.

## Worker Plan
- W1 (reference) — scope: read native collectors (Falcon/TenableIo/Qualys/DefenderVm/CortexXdr) + their
  Shared usage across all subsystems. output: review/native-reference-baseline.md. deps: none.
- W2 (cross-verifier) — scope: adversarially check YamlCollector AND CollectorExecutor against the baseline
  per subsystem; re-test conformance claims in actual CollectorExecutor source; flag looks-conformant-but-isn't.
  output: review/cross-verifier-1.md. deps: W1. (Mandatory verifier pass.)
- W3a/W3b/W3c (code review) — scope: three INDEPENDENT, isolated, minimal-context reviews of the
  CollectorExecutor implementation; differing primary lenses for coverage. outputs:
  review/code-reviewer-1.md, -2.md, -3.md. deps: none (run parallel with W1).

## Synthesis Approach
Main thread merges W2 + W3a/b/c: real conformance gaps and material code findings, separated from
minor/accepted; note agreement/disagreement among the three reviewers and any conflict with the
cross-verifier.

## Verification Obligations
- W2 IS the mandatory verifier pass (adversarial, against the native baseline + contract claims).
- W3a/b/c are the (tripled) code-reviewer obligation; isolated/minimal-context enforced sender-side.
- Cross-check the four Success Criteria in prompt_contract.md.

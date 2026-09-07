# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: two specified one-liners (SessionFactory RetryOptions; Runner publish branch) + tests/doubles. Tightly coupled to the runner terminal-outcome cluster.

## Research Decisions
- None needed. Both code-grounded; fix shapes specified.

## Worker Plan
Not applicable — direct path.

## Verification Obligations
- SC1–SC4.
- A-M1: long Retry-After (>cap) → deferred PartialResult, fast (no real sleep); short (≤cap, the existing 1s test) still in-process.
- A-M2: publish-fail-after-≥1-emitted → partial-success (emitted preserved); zero-emit publish-fail → transient.
- Guardrail audit: only the 2 RetryOptions fields + the 1 Runner branch changed; resilience policy order untouched; no vendor identity.
- Verifier runs the suite, confirms the long-Retry-After test doesn't actually block, and that partial-success preserves emitted.

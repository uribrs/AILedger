# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: One coherent concern carried verbatim — same shape as the prior 6 carries. The 38 files interlink
  through a single namespace sweep + a fixed set of cross-concern rewires; splitting into workers would create
  fuzzy boundaries and pure coordination overhead with no parallelism benefit. Tight single-context iteration
  (scripted cp + perl rewrite → build → fix usings build-driven) is the correct execution mode.

## Research Decisions
- None needed. All edges are in-repo code-trace; the design was already resolved from three real consumers
  (FalconCollector, Falcon indicator, CollectorExecutor). OPEN assumptions A1–A5 are execution-time
  confirmations against the source repo (STOP conditions), not external-system behavior — no researcher required.

## Worker Plan
Not applicable — direct path.

## Synthesis Approach
Not applicable — direct path.

## Verification Obligations
- Cross-check against all 7 Success Criteria in prompt_contract.md.
- Confirm the hard behavioral invariants are preserved verbatim (partial-success-wins; cancellation-NACK;
  success-completion ordering; flow-retry-OUTER / resilience-strategy-INNER; deferred-wait externalization;
  checkpoint-before-advance; best-effort shutdown with CancellationToken.None; forwarding never affects publishing).
- Confirm D1 (UnknownFlowRetryPolicy.CreatePipeline reconstructed in FaultGovernance; Kernel diff empty).
- Confirm D2 (no D8 renaming; Collector*/Adapter* names intact) and D3 (assets/findings hardwiring carried verbatim + flagged).
- Confirm zero residual `Cymulate.Integration.Adapters.Shared.*` in Conducting.
- Confirm Kernel/Conversation/Emission/Job/Reporting/Envelopes.Common + existing FaultGovernance git diff clean
  except FaultGovernance +CreatePipeline.
- Confirm Conducting does NOT depend on Emission (DAG invariant).
- Confirm build clean + tests pass; boundary-item dispositions (A1/A2) recorded.

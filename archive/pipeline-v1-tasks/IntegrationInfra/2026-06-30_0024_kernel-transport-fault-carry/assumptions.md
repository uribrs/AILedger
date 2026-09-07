# Assumptions

- **A1 (VALIDATED):** The transport-fault vocabulary is Kernel-eligible — zero third-party deps, no
  charter of its own, consumed downward by ≥2 concerns (Conversation, FaultGovernance, and the
  Conducting/Orchestration layer). Confirmed by reading all 4 source files: only `System.Net` /
  `System.Net.Sockets` usings; the Polly circuit-breaker check is a deliberate type-name string match
  to avoid a Polly reference.

- **A2 (VALIDATED):** `UnknownFlowRetryPolicy` has exactly one Polly-dependent member — `CreatePipeline`
  (uses `Polly`, `Polly.Retry`, returns `ResiliencePipeline`). All other members are Polly-free data /
  predicates. Confirmed by reading the source.

- **A3 (VALIDATED — operator-pinned):** `CreatePipeline` has no concern home yet (no concern relocated),
  and assigning pipeline-construction ownership to a domain is explicitly NOT in our remit. Therefore it
  stays in the source repo, out of scope, to be carried later with its concern. Single consistent answer
  — not a fork.

- **A4 (VALIDATED):** The split is realized only at the IntegrationInfra destination by choosing what to
  carry. The source repo is not mutated, so its intact `UnknownFlowRetryPolicy.CreatePipeline` continues
  to reference the source's own Polly-free members — no broken cross-repo reference is created.

- **A5 (OPEN — execution detail, non-blocking):** Test project shape (xUnit vs other) and its placement
  (`tests/` sibling) are unspecified by the operator. Default: xUnit, `tests/IntegrationInfra.Kernel.Tests`,
  referenced by a solution file if one is introduced. Resolve during execution; does not affect correctness.

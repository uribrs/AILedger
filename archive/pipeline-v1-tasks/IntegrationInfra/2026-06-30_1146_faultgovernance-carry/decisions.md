# Decisions (operator-fixed — inputs, not to be re-debated)

- **D1:** FaultGovernance = `Resilience/*` (renamed) + `Recovery/*` (3 files) folded in. Carry verbatim.
- **D2:** It is NOT event-based — a synchronous failure-policy decision engine + checkpoint persist/restore.
  Do NOT change functionality or introduce eventing.
- **D3:** Dependency envelope DTOs do NOT go to Kernel (wrong charter — they are envelope shapes, not
  primitives). Provide the shapes the consumer surface needs, in a shared envelope-shapes area; re-home
  properly when Events is carved.
- **D4:** Verbatim relocation, namespace-only rewrite, source repo reference-only — same spirit as the
  prior carries (Kernel 9630194, transport 72292d5).
- **D5:** Reclaim `FlowExceptionHandling` + `AdapterFlowFailureHandling` from Orchestration into
  FaultGovernance — they are governance's own classification types, mis-filed (charter's stated intent).
- **D6:** Fixed policy-chain order is an invariant (values-only parameterization); never expose chain
  reordering. Checkpoint-before-advance preserved.
- **D7 (standing — every carry):** SOLID separation, DRY, excellent XML docs, documentation, tests.

- **D8 (operator, post-review):** Naming is NOT bound by the verbatim rule — only logic/behavior is.
  Identity-bearing type names may be neutralized to fit generic infra. Renamed the carried envelope DTOs
  `Collector*` → `Adapter*` (`CollectorError`→`AdapterError`, `CollectorPartialCompletionMetadata`→
  `AdapterPartialCompletionMetadata`, `AdapterFailureHandling.ToCollectorError`→`ToAdapterError`),
  matching the neutral `Adapter*` prefix used throughout the concern. WIRE-SAFE: only C# type/method
  names changed; the JSON contract (`[JsonPropertyName]` values) is untouched, and no type-name-based
  serialization exists. Caveat accepted by operator ("worst case we change it later") — the generic-vs-
  collector-specific question is ultimately an Events-carve decision; this is the provisional neutral choice.
  Precedent: the `UnknownFlowRetryPolicy`→`UnknownFlowRetryClassification` rename in the transport carry.

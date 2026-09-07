# Assumptions

- **A1 (VALIDATED):** FaultGovernance is NOT event-based. Confirmed by reading
  `AdapterResilienceStrategy` (synchronous `CreateDefault` → `DecideAsync` policy walk) and
  `AdapterFailureHandling` (a `readonly record struct` whose `ToCollectorError()` just maps fields into a
  DTO). The `Events.*` dependency is data-mapping, not eventing.

- **A2 (VALIDATED):** The reclaim is clean. `FlowExceptionHandling` depends only on `Sdk.Events`;
  `AdapterFlowFailureHandling` depends on `Kernel.Transport` (carried), `Sdk.Contracts`, logging, regex —
  no further Orchestration drag. Clean in the destination because Conducting/Orchestration is not carried.

- **A3 (VALIDATED):** Transport vocab the policies need is already in `Kernel.Transport` (commit 72292d5),
  including the `UnknownFlowRetryPolicy` → `UnknownFlowRetryClassification` rename.

- **A4 (OPEN — executor to verify, non-blocking):** `CollectorError` + `CollectorPartialCompletionMetadata`
  drag no further Shared-internal deps (expected: plain DTOs + SDK enum). Verify when carrying; if they
  drag more, surface rather than expand silently.

- **A5 (OPEN — placement detail, non-blocking):** The shared envelope-shapes namespace is proposed as
  `Cymulate.IntegrationInfra.Envelopes.Common`. Executor may refine the exact name/location; constraint is
  only "shared area, not Kernel, not inside FaultGovernance." Re-homed when Events is carved.

- **A6 (OPEN — test-scope detail, non-blocking):** Reuse the existing `tests/IntegrationInfra.Kernel.Tests`
  project or add a `tests/IntegrationInfra.FaultGovernance.Tests` project. Default: a new FaultGovernance
  test project (keeps test projects aligned to concern). Executor picks the simplest that builds.

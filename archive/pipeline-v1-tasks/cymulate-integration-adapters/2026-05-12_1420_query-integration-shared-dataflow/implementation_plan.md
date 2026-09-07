# Implementation Plan

- Reuse as-is:
  - Shared session, HTTP transport failure classification, retry policy, JSON helpers, recovery checkpoint helpers, and publish request surfaces.
- Generalize now:
  - Make `AdapterBusEntrypointDefinition<TRequest>` carry an `AdapterCategory`, defaulting to collectors for current callers.
  - Promote resume runner scaffolding from `Collectors.Recovery` to generic `Orchestration` and update usages.
- Add Query-specific first-slice pieces:
  - `Shared/Query/README.md`
  - query topic/output constants
  - query pipeline context
  - query checkpoint key constants
  - query publication mapper that converts section/job outputs to existing `DataBatchRequest<T>` publish requests
- Defer:
  - full Dataflow topology
  - real/fake clients
  - adapters implementation of `IExecutionPlanStore`
  - `ExpressionDialect`, expanded `QueryTargeting`, and composite `ResultType` policy

## `IExecutionPlanStore` Intent

- SDK `IExecutionPlanStore` XML docs state the implementation lives in `Cymulate.IntegrationServiceBus.Infrastructure.Postgres`.
- SDK Query README describes the SDK module as contracts only and places pipeline implementation in bus Infrastructure/Core.
- Adapters Shared should not implement Postgres or a replacement plan store in this slice.

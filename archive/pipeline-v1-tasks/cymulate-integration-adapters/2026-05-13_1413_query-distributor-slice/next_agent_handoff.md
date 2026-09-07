# Next Agent Handoff

## Start Here

Read these files first:

1. `ai/active/2026-05-13_1238_query-domain-creation/state.json`
2. `ai/active/2026-05-13_1413_query-distributor-slice/state.json`
3. `ai/active/2026-05-13_1413_query-distributor-slice/phase_documentation.md`
4. `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/README.md`
5. `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Dataflow/README.md`
6. `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Planning/README.md`

## Current State

The Query Shared planning, topology composition, unit dispatch, and distribution stages are complete:

- `QueryPlanBuilder` implements SDK `IPlanBuilder`.
- `QueryPlanOptimizer` implements SDK `IPlanOptimizer`.
- `NativeQueryCoalescer` implements SDK `INativeCoalescer`.
- `QueryAdapterPipeline` implements SDK `IQueryAdapterPipeline`.
- `QueryUnitDispatcher` implements SDK `IUnitDispatcher`.
- `QueryDistributor` implements SDK `IDistributor`.

Final validation for this slice:

```text
dotnet build src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Cymulate.Integration.Adapters.Shared.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false
```

Passed with 0 warnings and 0 errors.

```text
dotnet test src/Cymulate.Integration.Adapters/UnitTests/Query/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false -v minimal
```

Passed: 86 tests.

NU1900 warnings appeared because external vulnerability indexes were unavailable.

## Behavior To Preserve

- Distributor only fans raw responses out to represented query ids and section ids.
- Time-window and native units are both indexed by `UnitId`.
- Section ids are resolved from `ExecutionPlan.SectionMembership`.
- The original `RawResponse` instance is preserved.
- Unknown response unit ids fail clearly.
- Represented query ids missing from section membership fail clearly.
- Distributor does not match, track sections, publish, deduplicate, or persist recovery state.
- No real or fake Query client product code belongs in Shared distributor infrastructure.

## Recommended Next Slice

Implement the concrete Query `IMatcher` behind the SDK contract.

The matcher slice should decide only candidate-response filtering into findings:

- accept SDK `DistributedResponse`, logical `QueryV2`, and job id
- preserve adapter-side matching semantics if the response is already admitted
- apply any currently supported structured matching rules that are already defined by SDK models
- produce SDK `Finding` values
- leave section accumulation, publication, recovery persistence, and client behavior to later slices

## Still Not Next Without A New Contract

- Real Query clients
- Fake Query clients as product code
- Concrete section tracker
- Query recovery planner/resume runner
- `IExecutionPlanStore` persistence
- ServiceBus/RabbitMQ/Postgres/outbox/storage/session/HTTP infrastructure

## Open Product/SDK Questions

- Should composite `QueryV2.ResultType` be rejected, split, or treated as a combined result type?
- Should `QueryV2.Expression` gain `ExpressionDialect`?
- Should `QueryTargeting` gain cloud/SaaS dimensions?
- Should native query normalization be more than trim-only?
- What exact boundary owns `INativeCoalescingOptOut` decisions?
- Where will per-logical-query output caps be enforced after merged physical requests?
- Should SDK `AdapterProgressContext.FromPlatformEvent` explicitly map Query topics to `AdapterCategory.Queries`?
- Should SDK `DispatchAsync` or `ExecutionPlan` carry job settings so dispatcher can honor `QueryJobSettings.MaxConcurrentUnits` exactly?
- Should duplicate logical query ids across sections remain supported fan-out behavior?

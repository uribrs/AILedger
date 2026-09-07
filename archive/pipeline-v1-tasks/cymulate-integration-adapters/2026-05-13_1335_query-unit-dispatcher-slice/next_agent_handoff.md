# Next Agent Handoff

## Start Here

Read these files first:

1. `ai/active/2026-05-13_1238_query-domain-creation/state.json`
2. `ai/active/2026-05-13_1335_query-unit-dispatcher-slice/state.json`
3. `ai/active/2026-05-13_1335_query-unit-dispatcher-slice/phase_documentation.md`
4. `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/README.md`
5. `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Dataflow/README.md`
6. `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Planning/README.md`

## Current State

The Query Shared planning, topology composition, and unit dispatch stages are complete:

- `QueryPlanBuilder` implements SDK `IPlanBuilder`.
- `QueryPlanOptimizer` implements SDK `IPlanOptimizer`.
- `NativeQueryCoalescer` implements SDK `INativeCoalescer`.
- `QueryAdapterPipeline` implements SDK `IQueryAdapterPipeline`.
- `QueryUnitDispatcher` implements SDK `IUnitDispatcher`.

Final validation for this slice:

```text
dotnet build src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Cymulate.Integration.Adapters.Shared.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false
```

Passed with 0 warnings and 0 errors.

```text
dotnet test src/Cymulate.Integration.Adapters/UnitTests/Query/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false -v minimal
```

Passed: 79 tests.

NU1900 warnings appeared because external vulnerability indexes were unavailable.

## Behavior To Preserve

- Dispatcher only executes units and streams raw responses.
- Time-window units go through `IQueryAdapter.ExecuteAsync`.
- Native units go through `IQueryAdapter.ExecuteNativeAsync`.
- Capability markers are checked before dispatching unsupported unit kinds.
- Dispatcher uses bounded concurrency and bounded response buffering.
- Dispatcher rejects adapter responses whose `RawResponse.UnitId` does not match the unit being dispatched.
- Dispatcher does not distribute, match, track sections, publish, or persist recovery state.
- No real or fake Query client product code belongs in Shared dispatcher infrastructure.

## Recommended Next Slice

Implement the concrete Query `IDistributor` behind the SDK contract.

The distributor slice should decide only raw-response fan-out from physical execution units to represented logical query ids:

- read unit-to-query mappings from `ExecutionPlan`
- fan each `RawResponse` to every represented query id
- preserve the original `RawResponse`
- fail clearly for unknown unit ids
- leave matching, section accumulation, publication, and recovery persistence to later slices

## Still Not Next Without A New Contract

- Real Query clients
- Fake Query clients as product code
- Concrete matcher
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

# Next Agent Handoff

## Start Here

Read these files first:

1. `ai/active/2026-05-13_1238_query-domain-creation/state.json`
2. `ai/active/2026-05-13_1306_query-dataflow-topology-slice/state.json`
3. `ai/active/2026-05-13_1306_query-dataflow-topology-slice/phase_documentation.md`
4. `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/README.md`
5. `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Dataflow/README.md`
6. `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Planning/README.md`

## Current State

The Query Shared planning and topology composition stages are complete:

- `QueryPlanBuilder` implements SDK `IPlanBuilder`.
- `QueryPlanOptimizer` implements SDK `IPlanOptimizer`.
- `NativeQueryCoalescer` implements SDK `INativeCoalescer`.
- `QueryAdapterPipeline` implements SDK `IQueryAdapterPipeline`.

Final validation for this slice:

```text
dotnet build src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Cymulate.Integration.Adapters.Shared.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false
```

Passed with 0 warnings and 0 errors.

```text
dotnet test src/Cymulate.Integration.Adapters/UnitTests/Query/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false -v minimal
```

Passed: 68 tests.

NU1900 warnings appeared because external vulnerability indexes were unavailable.

## Behavior To Preserve

- Topology composes SDK stage contracts; it does not implement stage business logic.
- Planning order is build, optimize, native coalesce.
- Dispatcher, distributor, matcher, tracker, and publisher are injected collaborators.
- One section tracker is created per execution plan through `IQuerySectionTrackerFactory`.
- Query publication goes through SDK `ISectionPublisher`.
- Dataflow options keep bounded capacity and explicit parallelism positive.
- Cancellation tokens are passed to stage calls and async streams.
- No real or fake Query client product code belongs in Shared topology.

## Recommended Next Slice

Implement the concrete Query `IUnitDispatcher` behind the SDK contract.

The dispatcher slice should decide only execution-unit scheduling and adapter method selection:

- dispatch time-window units through `IQueryAdapter.ExecuteAsync`
- dispatch native units through `IQueryAdapter.ExecuteNativeAsync`
- enforce time-window/native capability markers
- apply bounded unit concurrency from job settings or topology defaults
- stream `RawResponse` without materializing the full job result
- leave distributor, matcher, section tracker, and recovery persistence to later slices

## Still Not Next Without A New Contract

- Real Query clients
- Fake Query clients as product code
- Concrete distributor
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

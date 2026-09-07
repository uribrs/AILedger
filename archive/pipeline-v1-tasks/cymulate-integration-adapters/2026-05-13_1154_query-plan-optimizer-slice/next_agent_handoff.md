# Next Agent Handoff

## Start Here

Read these files first:

1. `ai/active/2026-05-13_1154_query-plan-optimizer-slice/state.json`
2. `ai/active/2026-05-13_1154_query-plan-optimizer-slice/phase_documentation.md`
3. `ai/active/2026-05-13_1154_query-plan-optimizer-slice/next_step_usage_examples.md`
4. `ai/active/2026-05-12_1728_query-plan-builder-slice/next_agent_handoff.md`
5. `/Users/user/Dev/Uri/Planning/2026-05-11_1326_crowdstrike-domain-doc-validation/QUERY_INTEGRATION_SHARED_DATAFLOW_BLUEPRINT_PRD.md`
6. `/Users/user/Dev/Uri/Planning/2026-05-11_1326_crowdstrike-domain-doc-validation/QUERY_INTEGRATION_SDK_CONTRACT_DESCRIPTOR.md`

## Current State

The Query Shared plan-builder and plan-optimizer slices are complete and verified.

Implemented product code lives under:

```text
src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Planning/
```

Tests live under:

```text
src/Cymulate.Integration.Adapters/UnitTests/Query/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test/
```

Final validation:

```text
dotnet build src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Cymulate.Integration.Adapters.Shared.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false
```

Passed with 0 warnings and 0 errors.

```text
dotnet test src/Cymulate.Integration.Adapters/UnitTests/Query/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false -v minimal
```

Passed: 50 tests.

NU1900 warnings appeared because external vulnerability indexes were unavailable.

## Behavior To Preserve

- `QueryPlanBuilder` emits draft units and does not optimize.
- `QueryPlanOptimizer` implements SDK `IPlanOptimizer`.
- Optimizer merges only time-window units.
- Optimizer grouping identity is plan product, result type, UTC time window, and canonical targeting.
- Optimizer unit ids exclude original query id.
- Optimizer accumulates represented query ids.
- Optimizer uses the least restrictive merged result limit.
- Native units pass through unchanged.
- Section membership and plan metadata pass through unchanged.

## Recommended Next Slice

Implement `INativeCoalescer`.

Suggested location:

```text
src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Planning/
```

Suggested class:

```text
NativeQueryCoalescer.cs
```

SDK contract:

```csharp
Task<ExecutionPlan> CoalesceAsync(
    ExecutionPlan plan,
    CancellationToken cancellationToken);
```

Expected behavior from SDK docs:

- Group native units by:
  - plan `Product`
  - `NativeExecutionUnit.NormalizedQuery`
  - `NativeExecutionUnit.ResultType`
- Accumulate `RepresentedQueryIds`.
- Preserve time-window units unchanged.
- Define coalesced native `UnitId` generation explicitly.
- Respect `INativeCoalescingOptOut` at composition level if that concern is introduced in the same slice.

## Still Not Next

Do not jump straight to:

- real Query clients
- fake clients as product code
- Dataflow topology
- dispatcher
- distributor
- matcher
- section tracker
- recovery planner
- `IExecutionPlanStore`
- ServiceBus/Postgres/RabbitMQ/outbox/storage infrastructure

## Open Product/SDK Questions

- Should composite `QueryV2.ResultType` be rejected, split, or treated as a combined result type?
- Should `QueryV2.Expression` gain `ExpressionDialect`?
- Should `QueryTargeting` gain cloud/SaaS dimensions?
- Should native query normalization be more than trim-only?
- What exact boundary owns `INativeCoalescingOptOut` decisions?
- Where will per-logical-query output caps be enforced after merged physical requests?

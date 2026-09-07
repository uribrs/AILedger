# Next Agent Handoff

## Start Here

Read these files first:

1. `ai/active/2026-05-13_1238_query-domain-creation/state.json`
2. `ai/active/2026-05-13_1247_query-native-coalescer-slice/state.json`
3. `ai/active/2026-05-13_1247_query-native-coalescer-slice/phase_documentation.md`
4. `ai/active/2026-05-13_1247_query-native-coalescer-slice/next_step_usage_examples.md`
5. `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/README.md`
6. `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Planning/README.md`

## Current State

The Query Shared planning stages are complete and verified:

- `QueryPlanBuilder` implements SDK `IPlanBuilder`.
- `QueryPlanOptimizer` implements SDK `IPlanOptimizer`.
- `NativeQueryCoalescer` implements SDK `INativeCoalescer`.

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

Passed: 62 tests.

NU1900 warnings appeared because external vulnerability indexes were unavailable.

## Behavior To Preserve

- `QueryPlanBuilder` emits draft units and does not optimize or coalesce.
- `QueryPlanOptimizer` merges only time-window units.
- `NativeQueryCoalescer` coalesces only native units.
- Coalesced native grouping identity is plan product, normalized query text, and result type.
- Native normalized query text is exact-match only.
- Coalesced native unit ids exclude original query id.
- Coalesced native units accumulate represented query ids.
- Time-window units pass through native coalescing unchanged.
- Section membership and plan metadata pass through unchanged.
- `INativeCoalescingOptOut` remains a composition-level concern.

## Recommended Next Step

Reassess Query Dataflow topology using the completed planning stages.

Topology should have a fresh contract because it will need to decide:

- exact stage composition and ownership boundaries
- bounded capacity
- parallelism
- cancellation and completion propagation
- dispatcher/distributor/matcher placeholders or sequencing
- publication through `QuerySectionPublisher`
- recovery handoff points without implementing `IExecutionPlanStore` locally

## Still Not Next Without A New Contract

Do not jump straight to:

- real Query clients
- fake clients as product code
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
- Should SDK `AdapterProgressContext.FromPlatformEvent` explicitly map Query topics to `AdapterCategory.Queries`?

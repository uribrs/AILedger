# Next Agent Handoff

## Start Here

Read these files first:

1. `ai/active/2026-05-12_1728_query-plan-builder-slice/state.json`
2. `ai/active/2026-05-12_1728_query-plan-builder-slice/phase_documentation.md`
3. `ai/active/2026-05-12_1728_query-plan-builder-slice/next_step_usage_examples.md`
4. `ai/active/2026-05-12_1420_query-integration-shared-dataflow/next_agent_handoff.md`
5. `/Users/user/Dev/Uri/Planning/2026-05-11_1326_crowdstrike-domain-doc-validation/QUERY_INTEGRATION_SHARED_DATAFLOW_BLUEPRINT_PRD.md`
6. `/Users/user/Dev/Uri/Planning/2026-05-11_1326_crowdstrike-domain-doc-validation/QUERY_INTEGRATION_SDK_CONTRACT_DESCRIPTOR.md`

## Current State

The Query Shared plan-builder slice is complete and verified.

Implemented product code lives under:

```text
src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Planning/
```

Tests live at:

```text
src/Cymulate.Integration.Adapters/UnitTests/Query/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test/QueryPlanBuilderTests.cs
```

Final validation:

```text
dotnet build src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Cymulate.Integration.Adapters.Shared.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false
```

Passed with 0 warnings and 0 errors.

```text
dotnet test src/Cymulate.Integration.Adapters/UnitTests/Query/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false -v minimal
```

Passed: 41 tests.

NU1900 warnings appeared because external vulnerability indexes were unavailable.

## Behavior To Preserve

- `QueryPlanBuilder` implements SDK `IPlanBuilder`.
- Plan builder produces draft plans only.
- No optimizer/coalescer behavior lives in `QueryPlanBuilder`.
- Draft `UnitId` includes original `QueryId` so units are unique before merge/coalesce stages exist.
- `WindowsByResultType` applies only to exact configured result types.
- Composite `QueryV2.ResultType` remains accepted but policy-neutral.
- Time windows are canonicalized to UTC in execution payload.
- Chunked inclusive time windows do not overlap.
- Targeting is canonicalized in execution payload.
- Private IPs must be canonical private IPv4 values.
- Empty private-IP lists normalize to `null`.
- Native expressions are trim-only normalized.
- Plan-size guard enforces `MaxUnitsPerPlan` before materializing chunks.

## Recommended Next Slice

Implement `IPlanOptimizer` for time-window units.

Suggested location:

```text
src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Planning/
```

Suggested class:

```text
QueryPlanOptimizer.cs
```

SDK contract:

```csharp
Task<ExecutionPlan> OptimizeAsync(
    ExecutionPlan plan,
    CancellationToken cancellationToken);
```

Expected behavior from SDK docs:

- Merge time-window `ExecutionUnit` entries sharing:
  - `plan.Product`
  - `ExecutionUnit.ResultType`
  - `ExecutionUnit.TimeWindow`
  - `ExecutionUnit.Targeting`
- Accumulate `RepresentedQueryIds`.
- Preserve `NativeUnits` unchanged.
- Preserve `SectionMembership` unchanged.
- Preserve `JobId`, `ClientId`, `Product`, and `CreatedAt`.
- Define merged `UnitId` generation explicitly.

## Optimizer Cautions

- Do not mutate the input `ExecutionPlan`.
- Do not merge native units in `IPlanOptimizer`; native coalescing belongs to `INativeCoalescer`.
- Do not decide composite `ResultType` policy in optimizer unless the new contract explicitly says to.
- Keep target canonicalization assumptions aligned with `QueryPlanBuilder`.
- Include cancellation checks for large plans.
- Add tests for:
  - no-op when no matching units
  - merge of equivalent time-window units
  - accumulation of represented query ids
  - native units preserved unchanged
  - section membership preserved unchanged
  - deterministic merged unit ids
  - cancellation path if implementation loops large collections

## Likely Slice After Optimizer

Implement `INativeCoalescer`.

SDK contract:

```csharp
Task<ExecutionPlan> CoalesceAsync(
    ExecutionPlan plan,
    CancellationToken cancellationToken);
```

Expected behavior:

- Group native units by:
  - `plan.Product`
  - `NativeExecutionUnit.NormalizedQuery`
  - `NativeExecutionUnit.ResultType`
- Accumulate `RepresentedQueryIds`.
- Preserve time-window units unchanged.
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

Those need their own contracts because coupling and policy decisions increase quickly. Tiny bureaucratic pause, big debugging savings.

## Open Product/SDK Questions

- Should composite `QueryV2.ResultType` be rejected, split, or treated as a combined result type?
- Should `QueryV2.Expression` gain `ExpressionDialect`?
- Should `QueryTargeting` gain cloud/SaaS dimensions?
- Should native query normalization be more than trim-only?
- How should optimizer/coalescer merged `UnitId`s be generated and documented?
- What exact boundary owns `INativeCoalescingOptOut` decisions?

## Files Not To Disturb Without A New Contract

- Existing collector orchestration/resume code.
- Query publisher code.
- Query checkpoint keys.
- SDK source under `/Users/user/Dev/IntegrationServiceBus`.
- Any real collector/client implementations.

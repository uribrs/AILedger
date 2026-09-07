# Next-Step Usage Examples

## Basic Optimizer Usage

```csharp
using Cymulate.Integration.Adapters.Shared.Orchestration.Query.Planning;
using Cymulate.Integration.Sdk.Query.Models.Plan;
using Cymulate.Integration.Sdk.Query.Pipeline;

IPlanOptimizer optimizer = new QueryPlanOptimizer();

ExecutionPlan optimizedPlan = await optimizer.OptimizeAsync(
    draftPlan,
    cancellationToken);
```

## Expected Merge Shape

```csharp
// The builder emits draft units, each representing one original query id.
// The optimizer collapses equivalent time-window units into one physical unit.
ExecutionUnit mergedUnit = optimizedPlan.TimeWindowUnits.Single();

IReadOnlySet<Guid> representedQueries = mergedUnit.RepresentedQueryIds;
string optimizedUnitId = mergedUnit.UnitId;
```

Equivalent time-window units share:

- plan `Product`
- `ExecutionUnit.ResultType`
- UTC `ExecutionUnit.TimeWindow`
- canonical `ExecutionUnit.Targeting`

## Native Units

```csharp
// Native units pass through unchanged in this slice.
IReadOnlyList<NativeExecutionUnit> nativeUnits = optimizedPlan.NativeUnits;
```

Native coalescing belongs to the next slice and should implement SDK `INativeCoalescer`.

## Result Limits

```csharp
// If merged represented units have limits 10 and 20, the physical unit uses 20.
// If any represented unit is unlimited, the physical unit remains unlimited.
int? mergedLimit = mergedUnit.ResultsLimit;
```

The optimizer avoids under-fetching. Any per-logical-query output cap should be handled by later distribution/matching behavior when that stage exists.

## Do Not Do In The Next Step

- Do not add a real vendor query client.
- Do not add a fake vendor client as product code.
- Do not implement ServiceBus/Postgres/outbox/RabbitMQ infrastructure in adapters Shared.
- Do not implement Dataflow topology before native coalescing has a concrete implementation.
- Do not decide composite `ResultType`, `ExpressionDialect`, or expanded `QueryTargeting` policy casually.
- Do not move optimizer behavior back into `QueryPlanBuilder`.

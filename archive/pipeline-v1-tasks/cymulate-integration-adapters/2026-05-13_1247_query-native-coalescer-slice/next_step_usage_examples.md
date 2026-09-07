# Next-Step Usage Examples

## Basic Native Coalescer Usage

```csharp
using Cymulate.Integration.Adapters.Shared.Orchestration.Query.Planning;
using Cymulate.Integration.Sdk.Query.Models.Plan;
using Cymulate.Integration.Sdk.Query.Pipeline;

INativeCoalescer coalescer = new NativeQueryCoalescer();

ExecutionPlan coalescedPlan = await coalescer.CoalesceAsync(
    optimizedPlan,
    cancellationToken);
```

## Expected Planning Sequence

```csharp
ExecutionPlan draftPlan = await planBuilder.BuildAsync(job, cancellationToken);
ExecutionPlan optimizedPlan = await planOptimizer.OptimizeAsync(draftPlan, cancellationToken);
ExecutionPlan coalescedPlan = await nativeCoalescer.CoalesceAsync(optimizedPlan, cancellationToken);
```

## Native Coalescing Shape

Equivalent native units share:

- plan `Product`
- `NativeExecutionUnit.NormalizedQuery`
- `NativeExecutionUnit.ResultType`

The coalesced unit:

- gets a deterministic unit id based on product, normalized query, and result type
- accumulates represented query ids
- preserves normalized query text exactly
- uses the least restrictive result limit

## Opt-Out Boundary

`INativeCoalescingOptOut` remains a composition-level decision.

The coalescer itself does not inspect adapter capabilities. Future topology/composition should decide whether to invoke this stage based on adapter capabilities.

## Recommended Next Step

Reassess Query Dataflow topology now that the three planning stages exist:

1. `IPlanBuilder`
2. `IPlanOptimizer`
3. `INativeCoalescer`

Create a fresh task contract before topology implementation. Topology will introduce stronger coupling around cancellation, capacity, ordering, dispatcher/distributor contracts, publication, and recovery handoff.

## Still Not Next Without A New Contract

- real Query clients
- fake clients as product code
- dispatcher
- distributor
- matcher
- section tracker
- recovery planner
- `IExecutionPlanStore`
- ServiceBus/Postgres/RabbitMQ/outbox/storage infrastructure

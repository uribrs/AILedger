# Next-Step Usage Examples

## Basic Plan Builder Usage

```csharp
using Cymulate.Integration.Adapters.Shared.Orchestration.Query.Planning;
using Cymulate.Integration.Sdk.Enums;
using Cymulate.Integration.Sdk.Query.Models.Common;
using Cymulate.Integration.Sdk.Query.Models.Wire;
using Cymulate.Integration.Sdk.Query.Pipeline;

IPlanBuilder planBuilder = new QueryPlanBuilder();

var job = new QueryJobV2(
    JobId: Guid.NewGuid(),
    ClientId: "client-1",
    Product: PlatformType.CrowdStrikeFalcon,
    Settings: new QueryJobSettings(
        WindowsByResultType: new Dictionary<ResultType, TimeSpan>
        {
            [ResultType.Alert] = TimeSpan.FromHours(1)
        },
        MaxConcurrentUnits: null,
        DelayBeforeQueryStart: null,
        MaxUnitsPerPlan: 5_000,
        MaxJobDuration: null,
        AutoChunk: null,
        InlineFindingsThreshold: null,
        SectionBatchSize: null),
    Sections:
    [
        new QuerySectionV2(
            SectionId: Guid.NewGuid(),
            Queries:
            [
                new QueryV2(
                    QueryId: Guid.NewGuid(),
                    Kind: QueryKind.TimeWindow,
                    ResultType: ResultType.Alert,
                    Targeting: new QueryTargeting("host-1", ["10.0.0.1"]),
                    TimeWindow: new TimeWindow(
                        DateTimeOffset.Parse("2026-05-12T10:00:00Z"),
                        DateTimeOffset.Parse("2026-05-12T12:00:00Z")),
                    Expression: "severity >= high",
                    Limits: new QueryLimits(MaxResults: 100, MaxBytesPerResponse: null))
            ])
    ]);

ExecutionPlan plan = await planBuilder.BuildAsync(job, cancellationToken);
```

## Expected Plan Shape For Optimizer Input

```csharp
// The builder emits draft units, not optimized units.
// Multiple equivalent source queries remain separate units because optimizer/coalescer are later slices.
ExecutionUnit firstUnit = plan.TimeWindowUnits[0];

// Draft UnitId is stable for the same original query/chunk and unique across original query ids.
string unitId = firstUnit.UnitId;

// Each draft unit represents one original query id.
Guid representedQueryId = firstUnit.RepresentedQueryIds.Single();
```

## Exact Result-Type Window Splitting

```csharp
var settings = new QueryJobSettings(
    WindowsByResultType: new Dictionary<ResultType, TimeSpan>
    {
        [ResultType.Alert] = TimeSpan.FromMinutes(30)
    },
    MaxConcurrentUnits: null,
    DelayBeforeQueryStart: null,
    MaxUnitsPerPlan: null,
    MaxJobDuration: null,
    AutoChunk: null,
    InlineFindingsThreshold: null,
    SectionBatchSize: null);

// Alert query windows are split into 30-minute chunks.
// Incident queries are not split unless ResultType.Incident is configured.
// Composite Alert | Event remains accepted but receives no special composite interval policy.
```

## Native Query Usage

```csharp
var nativeQuery = new QueryV2(
    QueryId: Guid.NewGuid(),
    Kind: QueryKind.Native,
    ResultType: ResultType.Event,
    Targeting: new QueryTargeting("host-1", ["10.0.0.1"]),
    TimeWindow: null,
    Expression: "  event_simpleName=ProcessRollup2  ",
    Limits: new QueryLimits(MaxResults: 100, MaxBytesPerResponse: null));

// The builder trims native Expression and stores it as NativeExecutionUnit.NormalizedQuery.
// It does not parse KQL/SPL/AQL or apply ExpressionDialect logic.
```

## Targeting Rules To Preserve

```csharp
var targeting = new QueryTargeting(
    Hostname: " host-1 ",
    PrivateIps: ["10.0.0.2", "10.0.0.1", "10.0.0.2"]);

// Execution units receive canonical targeting:
// Hostname: "host-1"
// PrivateIps: ["10.0.0.1", "10.0.0.2"]
```

Invalid private-IP examples that should stay rejected:

```csharp
new QueryTargeting(null, [""]);
new QueryTargeting(null, ["10.1"]);
new QueryTargeting(null, ["010.000.000.001"]);
new QueryTargeting(null, ["8.8.8.8"]);
new QueryTargeting(null, ["127.0.0.1"]);
new QueryTargeting(null, ["::1"]);
```

## Optimizer Slice Starting Point

```csharp
using Cymulate.Integration.Sdk.Query.Models.Plan;
using Cymulate.Integration.Sdk.Query.Pipeline;

public sealed class QueryPlanOptimizer : IPlanOptimizer
{
    public Task<ExecutionPlan> OptimizeAsync(
        ExecutionPlan plan,
        CancellationToken cancellationToken)
    {
        // Next slice:
        // Merge time-window units that share:
        // Product from plan.Product
        // ExecutionUnit.ResultType
        // ExecutionUnit.TimeWindow
        // ExecutionUnit.Targeting
        //
        // Accumulate RepresentedQueryIds.
        // Decide merged UnitId generation explicitly.
        throw new NotImplementedException();
    }
}
```

## Native Coalescer Slice Starting Point

```csharp
using Cymulate.Integration.Sdk.Query.Models.Plan;
using Cymulate.Integration.Sdk.Query.Pipeline;

public sealed class NativeQueryCoalescer : INativeCoalescer
{
    public Task<ExecutionPlan> CoalesceAsync(
        ExecutionPlan plan,
        CancellationToken cancellationToken)
    {
        // Later slice:
        // Group native units by:
        // plan.Product
        // NativeExecutionUnit.NormalizedQuery
        // NativeExecutionUnit.ResultType
        //
        // Accumulate RepresentedQueryIds.
        // Respect INativeCoalescingOptOut at composition level, not inside this class unless explicitly designed.
        throw new NotImplementedException();
    }
}
```

## Dataflow Topology Usage Later

```csharp
// Future topology should invoke stages in SDK order:
ExecutionPlan draftPlan = await planBuilder.BuildAsync(job, cancellationToken);
ExecutionPlan optimizedPlan = await planOptimizer.OptimizeAsync(draftPlan, cancellationToken);
ExecutionPlan coalescedPlan = await nativeCoalescer.CoalesceAsync(optimizedPlan, cancellationToken);

// Dispatcher/distributor/matcher/tracker/publisher are later stages.
```

## Do Not Do In The Next Step

- Do not add a real vendor query client.
- Do not add a fake vendor client as product code.
- Do not implement ServiceBus/Postgres/outbox/RabbitMQ infrastructure in adapters Shared.
- Do not decide composite `ResultType` semantics casually.
- Do not add a query-specific copy of generic Shared orchestration/publishing/recovery infrastructure.
- Do not change `QueryPlanBuilder` to perform optimizer/coalescer behavior unless the active contract explicitly changes.

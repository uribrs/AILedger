# Query Plan Optimizer Phase Documentation

## Scope

This phase implemented the Shared Query planning optimizer stage for SDK time-window execution units.

The phase stayed inside Shared Query planning and did not implement native coalescing, dispatcher, distributor, matcher, section tracker, Dataflow topology, recovery planner, plan-store persistence, real clients, or fake clients.

## Product Code Added

- `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Planning/QueryPlanOptimizer.cs`

## Product Code Updated

- `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Planning/QueryPlanUnitIds.cs`

## Tests Added

- `src/Cymulate.Integration.Adapters/UnitTests/Query/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test/QueryPlanOptimizerTests.cs`

The Query shared test project now has 50 passing tests.

## Implemented Behavior

- `QueryPlanOptimizer` implements SDK `IPlanOptimizer`.
- Input: SDK `ExecutionPlan`.
- Output: SDK `ExecutionPlan`.
- Time-window units are grouped by:
  - plan `Product`
  - `ExecutionUnit.ResultType`
  - UTC `ExecutionUnit.TimeWindow`
  - canonical `ExecutionUnit.Targeting`
- Merged units accumulate all `RepresentedQueryIds`.
- Merged unit ids are deterministic hashes of product, result type, UTC time window, and canonical targeting.
- Merged result limits use the least restrictive cap:
  - `null` if any represented unit is unlimited
  - otherwise the maximum represented unit limit
- Native units are preserved unchanged.
- Section membership is preserved unchanged.
- `JobId`, `ClientId`, `Product`, and `CreatedAt` are preserved.
- Cancellation is checked before and during the grouping loop.

## Deliberately Not Implemented

- `INativeCoalescer`
- `IUnitDispatcher`
- `IDistributor`
- `IMatcher`
- `ISectionTracker`
- Query Dataflow topology
- Query recovery planner
- `IExecutionPlanStore`
- real Query Integration client
- fake product client
- RabbitMQ/Postgres/outbox/storage/host replacement infrastructure
- `ExpressionDialect`
- expanded cloud/SaaS `QueryTargeting`
- composite `ResultType` policy

## Verification

- `dotnet build src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Cymulate.Integration.Adapters.Shared.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false`
  - Passed with 0 warnings and 0 errors.
- `dotnet test src/Cymulate.Integration.Adapters/UnitTests/Query/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false -v minimal`
  - Passed: 50 tests.
  - Emitted NU1900 warnings because external NuGet/CodeArtifact vulnerability indexes were unavailable.
- Final verifier reported no blocking implementation findings after repairs.
- Final code-review pass reported no material implementation-quality issues.

## Residual Risks

- Actual pipeline/DI composition is not covered in this slice because topology is still intentionally deferred.
- Per-query limit enforcement after merged physical requests remains a downstream distributor/matcher concern; the optimizer fetches with the least restrictive cap to avoid under-fetching.

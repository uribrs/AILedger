# Query Native Coalescer Phase Documentation

## Scope

This phase implemented the Shared Query planning stage for SDK native execution unit coalescing.

The phase stayed inside Shared Query planning and did not implement Dataflow topology, dispatcher, distributor, matcher, section tracking, recovery planning, plan-store persistence, real clients, or fake clients.

## Product Code Added

- `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Planning/NativeQueryCoalescer.cs`
- `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Planning/QueryPlanResultLimits.cs`

## Product Code Updated

- `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Planning/QueryPlanUnitIds.cs`
- `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Planning/QueryPlanOptimizer.cs`

## Tests Added Or Updated

- `src/Cymulate.Integration.Adapters/UnitTests/Query/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test/NativeQueryCoalescerTests.cs`
- `src/Cymulate.Integration.Adapters/UnitTests/Query/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test/QueryPlanOptimizerTests.cs`

The Query shared test project now has 62 passing tests.

## Implemented Behavior

- `NativeQueryCoalescer` implements SDK `INativeCoalescer`.
- Input: SDK `ExecutionPlan`.
- Output: SDK `ExecutionPlan`.
- Native units are grouped by:
  - plan `Product`
  - `NativeExecutionUnit.NormalizedQuery`
  - `NativeExecutionUnit.ResultType`
- Coalesced units accumulate all `RepresentedQueryIds`.
- Coalesced native unit ids are deterministic hashes of product, normalized query, and result type.
- Native normalized query text is preserved exactly.
- Coalesced result limits use the least restrictive cap:
  - `null` if any represented unit is unlimited
  - otherwise the maximum represented unit limit
- Time-window units are preserved unchanged.
- Section membership is preserved unchanged.
- `JobId`, `ClientId`, `Product`, and `CreatedAt` are preserved.
- Cancellation is checked before grouping, during grouping, and during output materialization.

## Deliberately Not Implemented

- Query Dataflow topology
- `IUnitDispatcher`
- `IDistributor`
- `IMatcher`
- `ISectionTracker`
- Query recovery planner
- `IExecutionPlanStore`
- real Query Integration client
- fake product client
- RabbitMQ/Postgres/outbox/storage/host replacement infrastructure
- `ExpressionDialect`
- expanded cloud/SaaS `QueryTargeting`
- composite `ResultType` policy
- deeper native query normalization
- `INativeCoalescingOptOut` detection inside the coalescer

## Verification

- `dotnet build src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Cymulate.Integration.Adapters.Shared.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false`
  - Passed with 0 warnings and 0 errors.
- `dotnet test src/Cymulate.Integration.Adapters/UnitTests/Query/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false -v minimal`
  - Passed: 62 tests.
  - Emitted NU1900 warnings because external NuGet/CodeArtifact vulnerability indexes were unavailable.
- Final verifier reported no material request-coverage issues after repairs.
- Task-orchestrator code-reviewer pass reported no material implementation-quality issues.
- Final explicit `code-reviewer` pass reported no material implementation-quality issues.

## Residual Risks

- Native coalescing is exact-match on `NormalizedQuery`; semantically equivalent text variants do not merge.
- Least-restrictive limit merging can increase upstream fetch volume for mixed capped/unlimited native groups.
- Composition-level `INativeCoalescingOptOut` wiring is still future work.

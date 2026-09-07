# Query Plan Builder Phase Documentation

## Scope

This phase implemented the first real Query pipeline behavior in adapters Shared: a minimal SDK `IPlanBuilder` implementation that converts `QueryJobV2` into `ExecutionPlan`.

The phase stayed inside Shared Query planning and did not implement optimizer, native coalescer, dispatcher, distributor, matcher, section tracker, Dataflow topology, recovery planner, plan-store persistence, real clients, or fake clients.

## Product Code Added

- `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Planning/QueryPlanBuilder.cs`
- `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Planning/QueryPlanDraft.cs`
- `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Planning/QueryPlanUnitIds.cs`
- `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Planning/QueryPlanValidator.cs`
- `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Planning/QueryPrivateIpCanonicalizer.cs`
- `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Planning/QueryTargetingCanonicalizer.cs`
- `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Planning/QueryTimeWindowSplitter.cs`
- `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Planning/QueryUnitDrafts.cs`

## Tests Added

- `src/Cymulate.Integration.Adapters/UnitTests/Query/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test/QueryPlanBuilderTests.cs`

The Query shared test project now has 41 passing tests.

## Implemented Behavior

- `QueryPlanBuilder` implements SDK `IPlanBuilder`.
- Input: SDK `QueryJobV2`.
- Output: SDK `ExecutionPlan`.
- Plan output preserves:
  - `JobId`
  - `ClientId`
  - `Product`
  - `SectionMembership`
  - `CreatedAt`
  - time-window units
  - native units
- Time-window queries produce `ExecutionUnit`.
- Native queries produce `NativeExecutionUnit`.
- Each draft unit represents exactly one original query id.
- Equivalent original queries are not optimized or coalesced in this slice.
- Draft `UnitId` includes `QueryId` so same-content draft units do not collide before optimizer/coalescer stages exist.
- Native query expression is trimmed and treated as opaque text.
- `WindowsByResultType` chunking is applied only for exact configured result-type keys.
- Composite `QueryV2.ResultType` values remain accepted but do not receive special composite interval behavior.
- Time-window payloads are canonicalized to UTC before entering execution units and unit-id hashing.
- Chunked time windows avoid inclusive-boundary overlap by starting each next chunk at previous `To + 1 tick`.
- `MaxUnitsPerPlan` is enforced before materializing planned units; default is 5,000 when absent.
- Targeting payloads are canonicalized before entering execution units and unit-id hashing.
- Empty private-IP lists normalize to `null`.
- Private IP values must be canonical private IPv4 addresses.

## Validation Rules

The builder rejects:

- null job
- empty job id
- blank client id
- null settings
- null sections
- empty sections
- duplicate section ids
- null section queries
- empty section queries
- null query
- null targeting
- empty query id
- duplicate query ids across the job
- missing time window for `QueryKind.TimeWindow`
- invalid time-window ordering
- non-null time window for `QueryKind.Native`
- blank native expression
- unsupported `QueryKind`
- `ResultType.None`
- result-type values containing undefined bits
- invalid `WindowsByResultType` keys
- non-positive `WindowsByResultType` intervals
- negative `MaxResults`
- negative `MaxBytesPerResponse`
- non-positive `MaxUnitsPerPlan`
- planned unit count exceeding `MaxUnitsPerPlan`
- null, blank, malformed, shorthand, leading-zero, public, IPv6, or loopback private-IP entries

## Deliberately Not Implemented

- `IPlanOptimizer`
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

## Important Design Decisions

- Keep code under `Orchestration/Query/Planning` to preserve concern-first Shared structure.
- Implement SDK `IPlanBuilder` directly rather than creating a local duplicate contract.
- Use SDK models directly for input and output.
- Use per-original-query draft unit ids to avoid duplicate persistence/checkpoint keys before optimizer/coalescer stages exist.
- Leave content-based merge/coalesce behavior for `IPlanOptimizer` and `INativeCoalescer`.
- Keep composite `ResultType` accepted but policy-neutral.
- Treat native query normalization as trim-only and opaque.
- Keep plan persistence out of adapters Shared; SDK docs place `IExecutionPlanStore` under ServiceBus Infrastructure/Postgres.

## Verification

- `dotnet build src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Cymulate.Integration.Adapters.Shared.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false`
  - Passed with 0 warnings and 0 errors.
- `dotnet test src/Cymulate.Integration.Adapters/UnitTests/Query/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false -v minimal`
  - Passed: 41 tests.
  - Emitted NU1900 warnings because external NuGet/CodeArtifact vulnerability indexes were unavailable.
- Final verifier reported no issues.
- Final code-review pass reported no material implementation-quality issues.

## Residual Risks

- Composite `ResultType` semantics are still a product/SDK policy decision.
- Downstream optimizer/coalescer stages must decide how to replace per-query draft unit ids with merged execution identities.
- Native query normalization is intentionally minimal.
- Only the plan-builder slice is implemented; there is not yet a runnable Query pipeline.

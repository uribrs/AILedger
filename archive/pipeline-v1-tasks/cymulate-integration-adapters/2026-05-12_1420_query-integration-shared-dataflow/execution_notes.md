# Execution Notes

## 2026-05-12

- Read the planning handoff and adjacent documents.
- Captured user decisions:
  - collector-named Shared infrastructure may be renamed/generalized when Query Integration uses it
  - `AdapterBusEntrypointRunner` and resume infrastructure should become category-aware/generic instead of creating query-specific duplicate runners
  - Dataflow outputs should publish through existing publish infrastructure
  - `IExecutionPlanStore` ownership remains evidence-driven and requires SDK/source documentation review
  - `ExpressionDialect`, cloud/SaaS targeting dimensions, and composite `ResultType` policy remain open
- Created repo-local execution state and prompt contract.
- Created the short implementation plan required by the handoff.
- Verified from SDK docs/source that `IExecutionPlanStore` is intended for IntegrationServiceBus Infrastructure/Postgres, not adapters Shared.
- Implemented category-aware Shared orchestration:
  - `AdapterBusEntrypointDefinition<TRequest>.Category`
  - generic `ExecuteFlowAsync` hook for non-collector flows
  - `AdapterBusEntrypointRunner` progress context creation uses the definition category
- Promoted `CollectorResumeRunner` to `AdapterResumeRunner` under generic Shared orchestration and updated collector usages/namespaces.
- Added initial Query Shared slice:
  - Query README
  - query topic/output constants
  - `QueryPipelineContext`
  - `QueryCheckpointKeys`
  - query publication mapper and `ISectionPublisher` implementation over existing `DataBatchRequest<T>` publish requests
- Added focused Query shared infrastructure tests.
- Validation:
  - `dotnet build src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Cymulate.Integration.Adapters.Shared.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false` passed.
  - `dotnet restore src/Cymulate.Integration.Adapters/UnitTests/Query/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test.csproj --disable-build-servers` passed with NU1900 warnings because vulnerability indexes were unavailable.
  - `dotnet test src/Cymulate.Integration.Adapters/UnitTests/Query/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false -v minimal` passed: 5 tests.
  - `dotnet test src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.DummyCollector.Test/Cymulate.Integration.Adapters.Collectors.DummyCollector.Test.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false --filter "AdapterBusEntrypointRunnerPolicyTests|AdapterResumeRunnerPolicyTests" -v minimal` passed: 8 tests.
  - `dotnet build src/Cymulate.Integration.Adapters/Tools/Cymulate.Integration.Adapters.Tools.LocalAdapterRunner/Cymulate.Integration.Adapters.Tools.LocalAdapterRunner.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false` passed.
- Residual risks:
  - SDK `AdapterProgressContext.FromPlatformEvent` still maps `AdapterCategory.Queries` topic to `indicators`; this slice documents the gap and relies on `AdapterProgressContext.Category`.
  - Full Query Dataflow topology, plan builder/optimizer, dispatcher, matcher, and section tracker remain future work.

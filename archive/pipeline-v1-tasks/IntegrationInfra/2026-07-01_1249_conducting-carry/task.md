# Task: Carry the Conducting concern (final concern)

Relocate the CONDUCTING concern VERBATIM (logic/behavior unchanged; namespace-only rewrite + cross-concern
rewires) from the in-production Shared library into IntegrationInfra. This is the last of the seven concern
carries; after it, IntegrationInfra is complete.

## What Conducting is
The collector RUN PIPELINE plus the shared low-altitude orchestration primitives:
- **Fresh-run template**: `AdapterBusEntrypointRunner.RunAsync` + `AdapterBusEntrypointDefinition<TRequest>`.
- **Resume template**: `CollectorResumeRunner.ResumeAsync` + `CollectorResumeDefinition<TState>`.
- **Collector-shaped composition surface**: `DelegateCollectorBusEntrypointSource<T>` +
  `CollectorBusEntrypointDefinitionBuilder.Build(...)`, `ICollectorBusEntrypointSource<T>`,
  `CollectorResultPayload`, guards, triggers, validation.
- **Shared primitives** used even by non-collector adapters (indicators): `AdapterPlatformEventFactory`,
  `AdapterInProcEventForwarder`, `CollectorCommonDependencies`.

Consumers COMPOSE against these by injecting delegates. There is NO façade to build — verdict grounded in
three real consumers (FalconCollector, Falcon indicator, CollectorExecutor). The contract SDK
(`Cymulate.Integration.Sdk` 3.2.0) already exists as an external package; IntegrationInfra sits behind it and
never carries it.

## Source / target
- Source (REFERENCE ONLY — never mutate): `/Users/user/Dev/Uri/localprojects/IntegrationsInfra`
- Target: `/Users/user/Dev/Uri/localprojects/IntegrationInfra`, namespace `Cymulate.IntegrationInfra.Conducting(.*)`
- Branch: `carry/conducting` (cut from main; PRs #1-#5 merged)

## Scope
~38 files under source `Orchestration/` (everything not already carried) → `src/IntegrationInfra/Conducting/`,
plus one FaultGovernance addition (the reconstructed `UnknownFlowRetryPolicy.CreatePipeline`) and the two
boundary items resolved during execution (`CollectorCommonDependencies`, `BaseFlowHandler`).

Full scope, decisions, invariants, and success criteria live in `prompt_contract.md`, `constraints.md`,
`decisions.md`, and `assumptions.md`.

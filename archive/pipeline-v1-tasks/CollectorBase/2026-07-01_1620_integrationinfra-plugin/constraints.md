# Constraints

- Reference/namespace rewire ONLY — no logic changes to consumer code (behavior preserved).
- Do NOT edit IntegrationInfra (it is merged + packed at 1.0.0-preview.2). A real gap → STOP + surface, never patch around in CollectorBase.
- CollectorBase is NOT git — deliverable is a clean build + passing tests, NO commit/push/PR.
- No residual `Cymulate.Integration.Adapters.Shared.*` anywhere after the rewire.
- nuget.config must ADD the local feed without `<clear/>` — the inherited org feed must stay active (it resolves the transitive Cymulate.* packages, Sdk 3.2.0, Http.Package.Session 2.0.2).
- Central package management: version pins live in Directory.Packages.props (add IntegrationInfra 1.0.0-preview.2; bump Sdk to 3.2.0). Individual csprojs carry version-less `<PackageReference>`.
- `dotnet build CollectorBase.slnx` clean (0 errors; pre-existing NU warnings acceptable).
- CollectorExecutor tests (Tests/CollectorExecutor.Test) + Collectors.Tests.Infrastructure pass.
- Specific-before-bare namespace substitution ordering (Orchestration.Collectors.Recovery / .Collectors before bare Orchestration; Session.TransportErrorHandling before bare Session).
- SDK types (IResumableAdapter, ICollectorCapability, IIntegrationAdapter, PlatformEvent, AdapterResult, IAdapterExecutionContext) are NOT renamed or remapped.
- Conducting family machinery keeps `Collector*` names (CollectorResumeRunner, CollectorResumeDefinition, CollectorBusEntrypointDefinitionBuilder, DelegateCollectorBusEntrypointSource, ICollectorBusEntrypointSource, CollectorResultPayload, CollectorGuards, CollectorTriggerParsing, PartialFlowSuccessContext) — do not rename these.

# Task: Cleanup + Indicator-Mechanism Carry

Repo: /Users/user/Dev/Uri/localprojects/IntegrationInfra (work on a new branch off `main`, currently clean at f0d323d).

Three fixes, all stemming from the 2026-07-07 Shared↔IntegrationInfra parity audit:

1. **Dedup trigger parsing.** `Conducting/Collectors/Triggers/CollectorTriggerParsing.cs` is logic-identical to `Job/AdapterTriggerParsing.cs` (diff-verified: namespace/class-name/doc-comments only; zero call sites in src/). Job owns the charter ("which stream, since when"). Remove the Conducting copy and its duplicated test class (`CollectorTriggerParsingTests` inside `tests/IntegrationInfra.Conducting.Tests/PureUnitTests.cs`); confirm `tests/IntegrationInfra.Job.Tests/AdapterTriggerParsingTests.cs` covers the same cases.

2. **Rename banned "Legacy" identifiers.** `AdapterBusLegacyFlowExecutor` (Conducting/Bus/Logic) and `CollectorResumeLegacyExecutor` (Conducting/Collectors/Recovery). Both are live DEFAULT no-strategy paths (chosen when `ResilienceStrategy == null`; classify-exception + Polly-retry behavior). Name for behavior, not lineage. "Legacy" in comments about the `{metadata}` wire shape and the `IHttpSession` ctor path is acceptable and out of scope.

3. **Carry indicator MECHANISMS from the adapters repo Shared** (`/Users/user/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/`), verbatim relocation per this repo's carry discipline, namespaces rewired:
   - `Contracts/Handlers/ITopicHandler.cs` (16 L)
   - `Contracts/Handlers/BaseFlowHandler.cs` (99 L)
   - `Helpers/IocTypeDetector.cs` (77 L)
   - `Helpers/PrivateIpDetector.cs` (47 L)
   - `Models/Platform/IocUploadRequest.cs` (71 L)
   - `Models/Platform/IoaUploadRequest.cs` (159 L)
   - `Converters/IoaRuleRequestConverter.cs` (116 L) — required: `IoaUploadRequest.cs` references it via `[JsonConverter(typeof(IoaRuleRequestConverter))]`
   - **Excluded:** `IndicatorNames` (identity/catalog stays consumer-side — operator decision).

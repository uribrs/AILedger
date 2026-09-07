# Decisions

- D-plugin: Replace the deleted Shared project with the Cymulate.IntegrationInfra package (1.0.0-preview.2, local feed). (VALIDATED)
- D-sdk-bump: Bump Cymulate.Integration.Sdk 3.1.8 → 3.2.0 to satisfy IntegrationInfra's dependency floor; aligns CollectorBase to the same SDK the substrate was built against. (VALIDATED — operator-accepted)
- D-contracts-neutral: `Shared.Contracts.Collectors` → `Cymulate.IntegrationInfra.Contracts` as identity-less `IAdapter`/`IAssetsAdapter`/`IFindingsAdapter` (was ICollectorAdapter/IAssetsCollectorAdapter/IFindingsCollectorAdapter). (VALIDATED)
- D-conducting-keeps-collector: Conducting's collector-family machinery keeps its `Collector*` names; only Emission/Reporting/Contracts types were D8-renamed. (VALIDATED)
- D-no-workaround: If a Shared type used by consumers is genuinely missing from IntegrationInfra, STOP + surface (new carry decision); do not re-declare or patch it locally in CollectorBase. (VALIDATED)

## Namespace map (source Shared → Cymulate.IntegrationInfra.*)
- `.Resilience` → `FaultGovernance`
- `.Session` → `Conversation`
- `.Session.TransportErrorHandling` → `Kernel.Transport` (+ `FaultGovernance` for `UnknownFlowRetryPolicy`, build-driven)
- `.DataPipeline.Egress` → `Emission`
- `.Events` (+ `.Events.CollectorEnvelopes.Logic`) → `Reporting` (+ `Envelopes.Common` build-driven)
- `.Orchestration` → `Conducting`; `.Orchestration.Collectors` → `Conducting.Collectors`; `.Orchestration.Collectors.Recovery` → `Conducting.Collectors.Recovery`
- `.Glossary` → `Job` (`AdapterTopics`) / `Emission` (`AdapterGlobalDefaults`), build-driven per identifier
- `.Contracts.Collectors` → `Contracts`

## Type renames (D8 + neutralized contracts; only where they appear)
CollectorNdjsonPublisher→AdapterNdjsonPublisher · CollectorOutputDefaults→AdapterOutputDefaults ·
CollectorGlobalDefaults→AdapterGlobalDefaults · CollectorStatus→AdapterStatus ·
CollectorEnvelopeBuilder→AdapterEnvelopeBuilder · CollectorInProcEventHub→AdapterInProcEventHub ·
ICollectorEventSink→IAdapterEventSink · ICollectorAdapter→IAdapter · IAssetsCollectorAdapter→IAssetsAdapter ·
IFindingsCollectorAdapter→IFindingsAdapter

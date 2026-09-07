# Decisions

- D-verbatim: Carry logic/behavior verbatim; namespace-only rewrite + rewires. (VALIDATED — precedent: prior 6 carries.)
- D-charter: Conducting depends on Kernel + Conversation + FaultGovernance + Reporting + Envelopes.Common + SDK; NOT Emission. (VALIDATED — egress is wired by consumers into their collect-delegate body; DAG stays acyclic, Conducting is apex.)
- D1: Reconstruct `UnknownFlowRetryPolicy.CreatePipeline(ILogger, vendor, flow)` in FaultGovernance, keeping the type name so consumer call-sites port verbatim; delegate to Kernel `UnknownFlowRetryClassification` for constants/predicate. (VALIDATED — closes the split the transport carry opened; it is public surface: FalconCollector + the bus flow executor both call it.)
- D2: D8 does NOT fire here — KEEP `Collector*` type names; `Collector` is genuine family identity (bus hardwires CollectAssets/CollectFindings; Collectors vs Indicators families). `Adapter*` names unchanged. (VALIDATED.)
- D3: Assets/findings hardwiring in `AdapterBusEntrypointDefinition` carried verbatim; tracked as a reshape candidate (same seam as the deferred Emission ISink reshape). Do NOT reshape mid-carry. (VALIDATED.)
- D-namespace: Mirror source sub-structure under `Cymulate.IntegrationInfra.Conducting.*` rather than flatten. (VALIDATED — adjustable micro-choice; default = mirror; large surface + real Bus/Collectors seam.)
- D-sdk: The contract SDK `Cymulate.Integration.Sdk` (3.2.0, external PackageReference) is never carried; IntegrationInfra sits behind it. (VALIDATED — source Shared defines zero `.Sdk` types, consumes it in 64 files.)

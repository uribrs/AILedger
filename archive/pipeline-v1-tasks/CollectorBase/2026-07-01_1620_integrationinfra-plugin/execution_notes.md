# Execution Notes — IntegrationInfra plug-in

## 2026-07-01 — Execution start (contract-driven-execution, direct)
CollectorBase (not git). Contract read + valid. Plumbing first (S1-S5) → restore → rewrite (S6) → build/test (S7).

## 2026-07-01 — Execution complete

### Plumbing (S1-S5)
- slnx: /Shared/ folder + project entry removed.
- csprojs: 4 dangling Shared ProjectReferences removed; `Cymulate.IntegrationInfra` PackageReference added to CollectorExecutor, Runner, Collectors.Tests.Infrastructure, CollectorExecutor.Test.
- Directory.Packages.props: added Cymulate.IntegrationInfra 1.0.0-preview.2; bumped Cymulate.Integration.Sdk 3.1.8 -> 3.2.0.
- nuget.config: local feed ../IntegrationInfra/artifacts added (no <clear/>).
- `dotnet restore` clean — both feeds resolve (local preview.2 + org Sdk 3.2.0 / Http.Package 2.0.2).

### Rewrite (S6) + build-driven fixups (S7)
- Scripted namespace remaps (specific-before-bare) + D8 type renames across the 22 consumer .cs. Zero residual Cymulate.Integration.Adapters.Shared.*. KEEP-list (CollectorResumeRunner, CollectorBusEntrypointDefinitionBuilder, DelegateCollectorBusEntrypointSource) intact.
- Two build-driven corrections (types whose concern != their source sub-namespace):
  1. AdapterHttpRequestFailedException lives in Kernel.Exceptions (not Kernel.Transport, where Session.TransportErrorHandling mapped) -> added `using ...Kernel.Exceptions;` to the 6 files using it.
  2. The RUN inbound parser is in Job and was D8-renamed: CollectorRunEnvelopeParser -> AdapterRunEnvelopeParser; RunPayloadCredentialHydrator kept its name, also in Job -> added `using ...Job;` to CollectorExecutorRunInputBuilder.cs.

### Assumption dispositions (all VALIDATED)
- A1: NO genuine gap — every Shared type used by consumers exists in IntegrationInfra (the 2 fixups above were namespace/name corrections, not missing types).
- A2: Strategies needed no direct package ref (transitive via CollectorExecutor — build green without it).
- A3: Sdk 3.2.0 resolved + did not break CollectorBase compilation.
- A4: both feeds resolved.
- A5: pure mechanical rewire — no consumer logic changed.

### Result
`dotnet build CollectorBase.slnx`: Build succeeded, 0 errors (10 NU warnings). `dotnet test`: CollectorExecutor.Test **96 passed / 0 failed / 0 skipped**. IntegrationInfra is proven end-to-end by a real consumer. No commit (CollectorBase not git).

SEAM-MAP REFINEMENT (for future consumer plug-ins): source Session.TransportErrorHandling splits — the classifier/UnknownFlowRetryClassification -> Kernel.Transport, but AdapterHttpRequestFailedException -> Kernel.Exceptions. Source Events splits by direction — outbound -> Reporting, inbound RUN parser -> Job (as AdapterRunEnvelopeParser) + RunPayloadCredentialHydrator -> Job.

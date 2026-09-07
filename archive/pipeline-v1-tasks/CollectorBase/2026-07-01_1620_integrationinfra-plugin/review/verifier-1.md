# Verifier-1 — IntegrationInfra plug-in (dependency swap)

Independent verification. Repo inspected directly; build/tests run by verifier. Notes NOT trusted.

Date: 2026-07-01
Repo: /Users/user/Dev/Uri/localprojects/CollectorBase (not git — deliverable = green build + tests)
Package: Cymulate.IntegrationInfra 1.0.0-preview.2 (local feed ../IntegrationInfra/artifacts)

## Per-criterion

### C1 — Shared fully removed — PASS
- `CollectorBase.slnx`: no `/Shared/` folder or Project entry. slnx now lists Tests x2, CollectorExecutor, Runner, Strategies only.
- No `...Cymulate.Integration.Adapters.Shared.csproj` ProjectReference in any csproj (grep over all csprojs: none).
- Remaining `Adapters.Shared` string hits are ONLY in docs/task-history markdown (CLAUDE.md, ai/**), never in slnx/csproj/.cs.

### C2 — Package wiring — PASS
- `Cymulate.IntegrationInfra` PackageReference present on all 4 consumers: CollectorExecutor, Runner, Collectors.Tests.Infrastructure, CollectorExecutor.Test.
- `Directory.Packages.props`: `PackageVersion Cymulate.IntegrationInfra 1.0.0-preview.2` present (line 9); `Cymulate.Integration.Sdk` = 3.2.0 (line 8, comment notes 3.1.8→3.2.0 bump).
- `CollectorBase/nuget.config`: adds `integrationinfra-local` = ../IntegrationInfra/artifacts; NO `<clear/>` — org feed (cym-dom/cym-repo-nuget) stays active (confirmed by restore resolving 2 sources).

### C3 — Zero residual Adapters.Shared.* in code — PASS
- Grep of all .cs under the tree (excl obj/bin, excl ai/ docs): zero `Cymulate.Integration.Adapters.Shared`.

### C4 — dotnet build clean — PASS
- `dotnet build CollectorBase.slnx --no-incremental`: **Build succeeded, 0 Errors**. Warnings are all NU1507 (2 package sources / central-package-management source-mapping advisory) — pre-existing, acceptable per contract. (10 warnings incremental / 12 on full restore, all NU.)

### C5 — dotnet test CollectorExecutor tests pass — PASS
- `dotnet test CollectorBase.slnx`: **Passed! Failed: 0, Passed: 96, Skipped: 0, Total: 96** (CollectorExecutor.Test, net8.0, ~10s).

### C6 — No consumer LOGIC changed; KEEP-list + SDK types intact — PASS
- Spot-checked `CollectorExecutorRunInputBuilder.cs`: only usings changed (added `Cymulate.IntegrationInfra.Job`, `.Reporting`); method bodies (Build/HydrateRunEnvelope/DeriveFloorFromRunEnvelope) unchanged in behavior — pure namespace/type-name rewire.
- KEEP-list Conducting types NOT over-renamed: CollectorResumeRunner, CollectorBusEntrypointDefinitionBuilder, DelegateCollectorBusEntrypointSource all still used by original names, resolving from `Cymulate.IntegrationInfra.Conducting[.Collectors[.Recovery]]` (see CollectorExecutorAdapter.cs). CollectorResultPayload/CollectorGuards/CollectorTriggerParsing not consumed by CollectorBase (0 refs — never used originally, not evidence of over-rename). Verified NO `Adapter*` mis-renames of any KEEP type exist.
- SDK types untouched: IResumableAdapter (1 file), IIntegrationAdapter (2 files) still referenced by original names; ICollectorCapability not used by CollectorBase (0 refs, expected).
- Neutralized-contract + D8 renames complete: old names ICollectorAdapter / IAssetsCollectorAdapter / IFindingsCollectorAdapter / ICollectorEventSink / CollectorNdjsonPublisher all have 0 residual refs.

### C7 — No silent workaround — PASS
- IntegrationInfra repo git status clean; HEAD = preview.2 re-pack commit (b55ea12), predating this task → NOT edited for this task.
- No Shared type re-declared locally in CollectorBase (grep for `class {AdapterHttpRequestFailedException,AdapterRunEnvelopeParser,RunPayloadCredentialHydrator,UnknownFlowRetryPolicy}` in consumer .cs: none).
- Both reported build-driven corrections confirmed LEGITIMATE against real IntegrationInfra src (/Users/user/Dev/Uri/localprojects/IntegrationInfra/src/IntegrationInfra/IntegrationInfra):
  - AdapterHttpRequestFailedException → declared in `namespace Cymulate.IntegrationInfra.Kernel.Exceptions` (Kernel/Exceptions/AdapterHttpRequestFailedException.cs). Consumer added `using ...Kernel.Exceptions` in exactly 6 files (matches claim).
  - AdapterRunEnvelopeParser (renamed from CollectorRunEnvelopeParser) → `namespace Cymulate.IntegrationInfra.Job` (Job/AdapterRunEnvelopeParser.cs).
  - RunPayloadCredentialHydrator → `namespace Cymulate.IntegrationInfra.Job` (Job/RunPayloadCredentialHydrator.cs).
  These are genuine namespace/name corrections against the real package surface, not local re-declarations or workarounds.

## Verdict: SATISFIED

All 7 Success Criteria PASS. Build 0 errors (NU warnings only), 96/96 tests pass, Shared fully excised, package wiring correct, 22 files rewired with logic intact, KEEP-list/SDK types preserved, both build-driven fixups validated against real IntegrationInfra source. No gaps, no workarounds.

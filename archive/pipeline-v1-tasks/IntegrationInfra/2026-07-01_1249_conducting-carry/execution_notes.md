# Execution Notes — Conducting carry

## 2026-07-01 — Execution start (contract-driven-execution, direct path)

Branch: carry/conducting. Read state.json, prompt_contract.md, constraints.md, assumptions.md, decisions.md,
orchestration_plan.md. Contract valid. Beginning S1 recon + STOP-condition checks.

### S1 — Recon (DONE)

Scope confirmed: 38 Orchestration files + CollectorCommonDependencies. Cross-Shared using survey + target-landing
map complete. External deps in scope: Microsoft.Extensions.Logging, Polly ONLY (no DefensiveToolkit usings in the
Conducting core — the ServerSuggestedRetryDelay handling lives in the consumer/resilience-strategy, not here).

Rewire map (source using → target namespace):
- `...Orchestration.Bus.Logic` → `...Conducting.Bus.Logic`; `...Orchestration.Bus.Models` → `...Conducting.Bus.Models`
- `...Orchestration.Collectors[.Recovery[.Models]/.Guards/.Triggers/.Validation]` → `...Conducting.Collectors[...]`
- `...Orchestration.Diagnostics` → `...Reporting`; `...Events` → `...Reporting`
- `...Orchestration.Models` → split: PartialFlowSuccessContext → `...Conducting` (root); FlowExceptionHandling → `...FaultGovernance` (already there)
- `...Orchestration` (bare) → `...Conducting`
- `...Resilience` (10) → `...FaultGovernance` (FLAT ns: AdapterResilienceStrategy, AdapterRecoveryContext/Result, AdapterBackoffPlans all flat)
- `...Recovery` (CheckpointAdapter) → `...FaultGovernance.Recovery`
- `...Session.TransportErrorHandling` → `...Kernel.Transport` (HttpTransportFailureClassifier/IHttpFailureClassifier) + `...FaultGovernance` (UnknownFlowRetryPolicy)
- `...Session` (bare) → `...Conversation`
- `...Glossary` (AdapterTopics) → `...Job`
- `...DependencyInjection` → `...Conducting.DependencyInjection`

STOP-condition checks / assumption dispositions:
- A1 CollectorCommonDependencies → VALIDATED in-scope. Deps: DefaultSessionProvider (Conversation), IEncryptionService/IAdapterExecutionContext (SDK), MS.DI + Logging. No Orchestration-internal, no Emission drag. Place at Conducting/DependencyInjection/ (ns ...Conducting.DependencyInjection).
- A2 BaseFlowHandler → RESOLVED = EXCLUDE. It + ITopicHandler (both in source Contracts/Handlers/) are an indicator-side handler-template contract pair: ZERO references from any of the 38 collector-pipeline files, not carried anywhere in target. NOT Conducting (collector run pipeline). FOLLOW-UP: indicator handler contracts (BaseFlowHandler/ITopicHandler + the TopicHandlerFactory pattern) have no home in the current 7-concern decomposition — surface to operator as a separate decision. Not a blocker.
- A3 UnknownFlowRetryPolicy.CreatePipeline → VALIDATED reconstructable verbatim. CreatePipeline body uses MaxRetries/RetryDelays/IsUnknownRetryCandidate; the predicate calls HttpTransportFailureClassifier.IsRetryableTransportFailure — all present in Kernel (UnknownFlowRetryClassification + HttpTransportFailureClassifier in Kernel.Transport). FaultGovernance UnknownFlowRetryPolicy = CreatePipeline (real body) + thin forwarders (MaxAttempts, IsUnknownRetryCandidate) so flow-executor + external consumer call-sites (`UnknownFlowRetryPolicy.X`) port verbatim via a using swap. Logic single-homed in Kernel; no duplication.
- A4 Polly → already referenced in FaultGovernance (confirmed). No DefensiveToolkit needed in Conducting core.
- NEW FINDING A7 (DAG correction): Conducting ALSO depends on **Job** (AdapterTopics, used by AdapterBusFlowDispatcher + CollectorTriggerParsing) and **Kernel** (transport classifier/exceptions). Corrected DAG: Conducting → Kernel + Conversation + FaultGovernance + Reporting + **Job** + Envelopes.Common + SDK. Still NOT Emission. Still acyclic (no concern depends on Conducting). Update README charter accordingly.

### S2–S6 — Copy + rewrite + D1 + build (DONE)

- S2: 39 .cs copied (38 Orchestration + CollectorCommonDependencies) into mirrored `Conducting/` structure. Telemetry hub / Diagnostics / FlowExceptionHandling / session-lifecycle correctly NOT present.
- S3: namespace rewrite (perl, specific-before-bare). ZERO residual `Cymulate.Integration.Adapters.Shared.*`. Namespaces faithful to source (note: source `PartialFlowSuccessContext` declared ns `...Orchestration` [root] despite its Models/ folder → lands at `...Conducting` root; source `Recovery/Models/*` declared `...Collectors.Recovery` [folder≠ns] → preserved as `...Conducting.Collectors.Recovery`).
- S4: cross-concern rewires applied. Build-driven using fixups added:
  - `using ...FaultGovernance;` → AdapterBusEntrypointRunner, AdapterBusEntrypointSetup, AdapterBusFlowExecutor, AdapterBusLegacyFlowExecutor, AdapterBusFailurePublisher, AdapterPartialSuccessPublishContext, PartialFlowSuccessContext, CollectorBusEntrypointDefinitionBuilder, CollectorResumeFailurePublisher, CollectorResumeLegacyExecutor, CollectorResumePartialSuccessPublishContext (FlowExceptionHandling/AdapterFlowFailureHandling/UnknownFlowRetryPolicy all live in FaultGovernance flat ns).
  - `ConfigurationValidation.cs`: source used ambient `Sdk.Models.X` (resolved via old `Cymulate.Integration.*` ancestry, now broken by `IntegrationInfra`). Fixed with additive `using Sdk = Cymulate.Integration.Sdk;` — zero body change.
- S5 (D1): `UnknownFlowRetryPolicy` created at `FaultGovernance/Logic/UnknownFlowRetryPolicy.cs` (ns flat `...FaultGovernance`). CreatePipeline body verbatim; MaxRetries/RetryDelays/IsUnknownRetryCandidate delegate to Kernel `UnknownFlowRetryClassification`; MaxAttempts + IsUnknownRetryCandidate re-exposed as forwarders so all `UnknownFlowRetryPolicy.*` call-sites port verbatim. Logic single-homed in Kernel.
- S6: **Build succeeded, 0 errors** (2 NU warnings only). Isolation verified via git status: Kernel/Conversation/Emission/Job/Reporting/Envelopes UNTOUCHED; FaultGovernance gains ONLY UnknownFlowRetryPolicy.cs; csproj/Directory.Packages.props unchanged (Polly already present from FaultGovernance carry). A3/A5(build) VALIDATED; Kernel diff EMPTY (D1 criterion met).

### FINDING — DAG correction (surface to operator)
`CollectorTriggerParsing.NormalizeFlowName` references `AdapterGlobalDefaults.{AssetsFileName,FindingsFileName}`
(two file-name constants) which live in **Emission** (renamed from `CollectorGlobalDefaults` during the Emission
carry). This is verbatim source behavior and creates a thin **Conducting→Emission** edge — contradicting the
charter claim "Conducting does NOT depend on Emission". It is the SAME mis-homed-constant coupling already
flagged in the Job carry (Job→Emission via `AdapterGlobalDefaults.DefaultLookbackDays`).
- Disposition: fixed verbatim (rewire to Emission's `AdapterGlobalDefaults`); NOT hard-stopped (thin, constant-only, behavior-identical, matches known coupling).
- Corrected DAG: Conducting → Kernel + Conversation + FaultGovernance + Reporting + Job + **Emission** + Envelopes.Common + SDK. Still acyclic.
- FOLLOW-UP (unification reshape, operator's call): the shared glossary constants in `AdapterGlobalDefaults`
  (AssetsFileName/FindingsFileName/DefaultLookbackDays) are consumed by Job + Conducting + Emission → mis-homed.
  Relocating them to a foundational concern (Kernel or a shared glossary) would break both Job→Emission and
  Conducting→Emission edges. Do NOT do mid-carry — tracked follow-up, unify with the Job→Emission item.

### S7 — XML docs + README (DONE)
- Added XML docs to the primary public entrypoints (source lacked them): `AdapterBusEntrypointRunner` (class + RunAsync), `CollectorResumeRunner.ResumeAsync`, `CollectorBusEntrypointDefinitionBuilder.Build`. Existing docs preserved. No CS1591 enforcement in the build; per-member exhaustive docs remain the standing cross-carry follow-up.
- Rewrote `Conducting/README.md` (the skeleton enshrined the disproven "façade front door / absorbs the wiring / zero-mechanics proof gate" premise). New README: charter, the two run-templates, composable-not-façade verdict grounded in the 3 consumers, D1–D3, corrected DAG (incl. the Emission edge + follow-up), invariants, and the BaseFlowHandler/ITopicHandler out-of-scope note.

### S8 — Tests (DONE)
- `tests/IntegrationInfra.Conducting.Tests` (xUnit + Moq; ProjectReference; added to IntegrationInfra.slnx). Moq added to Directory.Packages.props (test-only; needed to stub `IAdapterExecutionContext` whose full surface isn't locally defined). No InternalsVisibleTo needed — all tested units are public.
- Pure units: CollectorTriggerParsing (flow-name mapping via the real `AdapterGlobalDefaults`/`AdapterTopics` constants, bool/date parsing, key fallback), ConfigurationValidation (success/factory-failure), CollectorGuards (ready/not-ready), CollectorResultPayload (stable-key mapping + per-type totals), AdapterPlatformEventFactory.ResolveForFlow (mint / storageUrl promotion / no-overwrite), UnknownFlowRetryPolicy predicate + MaxAttempts.
- Two invariant tests on the real `AdapterBusEntrypointRunner` with a recording `Mock<IAdapterExecutionContext>`:
  (a) partial-success built → partial returned, completion published, ErrorRequest NEVER published (failure suppressed);
  (b) OperationCanceledException → no completion/error published at all. Both green → A5 validated behaviorally.
- Note: `IAdapterExecutionContext.PublishAsync` returns `Task<PublishResult>`; the stub returns `PublishResult.Ok()` to avoid null-Task NRE.

### S9 — Build + test + mirror (DONE)
- `dotnet test IntegrationInfra.slnx`: **all 7 projects pass** — Conducting.Tests 30/30; Kernel 33, FaultGovernance 20, Conversation 10, Emission 15, Job 31, Reporting 15 — 0 failed, 0 skipped. Build clean (0 errors; NU1507/NU1900 only).
- A5 VALIDATED. Task dir mirrored to global archive.

## RESULT: all 7 success criteria met. Residual (tracked follow-ups, non-blocking):
1. Conducting→Emission thin edge via `AdapterGlobalDefaults.{AssetsFileName,FindingsFileName}` — corrected DAG + unify with the Job→Emission constant-relocation reshape.
2. BaseFlowHandler/ITopicHandler (indicator handler contracts) have no home in the 7-concern decomposition — separate operator decision.
3. D3 assets/findings hardwiring reshape candidate (sibling of the Emission ISink reshape).
4. Per-member XML doc completeness (recurring cross-carry C5-PARTIAL) — single-pass candidate.

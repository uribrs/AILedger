# Verifier Report — Conducting Carry (TASK-20260701-1249)

Verifier: independent pass. Repo inspected directly; build + tests run; source diffed. Execution claims were
re-derived, not trusted.

Branch: `carry/conducting`. Work is present as untracked/modified files (not committed) — expected mid-task state.

---

## Per-Criterion Findings

### SC1 — 38 Orchestration files under Conducting, ns `Cymulate.IntegrationInfra.Conducting(.*)`, verbatim logic — **PASS**
- Target has exactly 39 `.cs` files: 38 Orchestration-origin + `CollectorCommonDependencies` (A1 in-scope). Count math confirmed: source `Orchestration/` has 48 `.cs`; the 10 not carried here (`Diagnostics/*`, `AdapterInProcEventRaiser`, `AdapterSessionLifecycle`, `AdapterFlowFailureHandling`, `FlowExceptionHandling`, `CollectorInProcEventHub`) are all already-carried types now living in Reporting/Conversation/FaultGovernance — verified present there. `CollectorInProcEventHub` is the sole one not landing anywhere (telemetry hub, intentionally dropped per exec_notes; not referenced by the 38).
- All namespaces are `Cymulate.IntegrationInfra.Conducting[.Bus.Logic|.Bus.Models|.Collectors(.Guards/.Recovery/.Triggers/.Validation)|.DependencyInjection]` — mirrored sub-structure, not flattened. Confirmed.
- **Verbatim sweep across all 38 files** (usings/namespaces/XML-docs excluded): every file is byte-identical in body EXCEPT `CollectorTriggerParsing.cs`, whose only change is the documented reference rewire `CollectorGlobalDefaults.*` → `AdapterGlobalDefaults.*` (a type-rename reference introduced by the prior Emission carry — same constant, not a logic change). Spot-checked bodies of AdapterBusEntrypointRunner, AdapterBusFlowExecutor, AdapterBusPartialSuccessPublisher, CollectorResumeRunner, CollectorBusEntrypointDefinitionBuilder, ConfigurationValidation, all Bus/Logic + Recovery executors, AdapterPlatformEventFactory, CollectorResultPayload, CollectorGuards, CollectorCommonDependencies — all identical. Added XML docs on 3 public entrypoints are the only other delta, contract-sanctioned.
- Internal accessibility preserved: all Bus/Logic + Recovery executors remain `internal` (constraint honored). Boundary dispositions (A1 in / A2 out) recorded in execution_notes.md.

### SC2 — D1 UnknownFlowRetryPolicy reconstructed in FaultGovernance; Kernel diff EMPTY — **PASS**
- `FaultGovernance/Logic/UnknownFlowRetryPolicy.cs` exists. `CreatePipeline` body is behavior-identical to source `Session/TransportErrorHandling/UnknownFlowRetryPolicy.cs`: same MaxRetryAttempts, ShouldHandle predicate, DelayGenerator index logic, OnRetry log template. Constants/predicate delegate to Kernel `UnknownFlowRetryClassification` (MaxRetries=3, delays 30/60/120, identical predicate incl. static-ctor length invariant — verified against source). Forwarders `MaxAttempts`/`IsUnknownRetryCandidate` preserved so consumer call-sites port verbatim. Logic single-homed in Kernel; no duplication.
- `git diff src/IntegrationInfra/Kernel` = **0 lines**. Confirmed.

### SC3 — D2 honored; `Collector*`/`Adapter*` names intact, no D8 renaming — **PASS**
- Type names unchanged (CollectorResumeRunner, AdapterBusEntrypointRunner, CollectorGuards, etc.). No renaming beyond namespaces observed anywhere in the sweep.

### SC4 — All rewires done; zero residual Shared; other trees UNCHANGED except FaultGovernance +CreatePipeline — **PASS**
- `grep Cymulate.Integration.Adapters.Shared` in Conducting + new FaultGovernance file = **0 hits**.
- `git status`: Kernel/Conversation/Emission/Job/Reporting = **no changes**. Envelopes.Common = no changes. FaultGovernance = only `+ Logic/UnknownFlowRetryPolicy.cs` (untracked). Main `IntegrationInfra.csproj` unchanged. Source reference repo `git status --porcelain` = clean (not mutated).
- Only other tracked changes: `IntegrationInfra.slnx` (+Conducting.Tests entry) and `Directory.Packages.props` (+Moq, test-only, annotated) — both expected per SC6.

### SC5 — Build clean; XML docs on public entrypoints; README written — **PASS**
- `dotnet build IntegrationInfra.slnx`: **0 Errors**, 32 warnings, all NU1507/NU1900 (contract-accepted, offline-source noise). Clean.
- XML docs added to primary public entrypoints (AdapterBusEntrypointRunner class+RunAsync, CollectorResumeRunner.ResumeAsync, CollectorBusEntrypointDefinitionBuilder.Build) + the new UnknownFlowRetryPolicy. Note: no repo-wide CS1591 enforcement, so per-member exhaustive docs are not complete — this is disclosed as a recurring cross-carry follow-up, not a gap against this criterion.
- `Conducting/README.md` rewritten: charter, both run-templates (fresh + resume), composable-not-façade verdict grounded in the 3 named consumers, D1–D3, invariants, DAG, and the BaseFlowHandler/ITopicHandler out-of-scope note. See SC-note below re: the Emission DAG line.

### SC6 — Tests exist + assert real behavior (pure units + 2 runner invariants) — **PASS**
- `tests/IntegrationInfra.Conducting.Tests` (xUnit + Moq, **ProjectReference** to real lib, in slnx). No InternalsVisibleTo needed (all tested units public — verified).
- Pure units cover every enumerated unit: CollectorTriggerParsing (flow-name mapping via real AdapterGlobalDefaults/AdapterTopics, bool/date parse, key fallback), ConfigurationValidation (success + factory-failure), CollectorGuards, CollectorResultPayload (stable keys + per-type totals), AdapterPlatformEventFactory.ResolveForFlow (mint / storageUrl promotion / no-overwrite), UnknownFlowRetryPolicy predicate + MaxAttempts. All assert concrete values — non-tautological. (Resume checkpoint fingerprint/serialize was conditional "if pure" in the contract; its absence is not a gap.)
- **Two invariant tests exercise the REAL `AdapterBusEntrypointRunner.RunAsync`**, not a stub of it — only the SDK boundary `IAdapterExecutionContext` is mocked (the correct seam). (a) forces a mid-flow throw + supplies a partial builder, asserts CompletionRequest published AND ErrorRequest `Times.Never` → genuinely proves partial-success-wins + failure suppression. (b) throws OperationCanceledException, asserts CompletionRequest AND ErrorRequest both `Times.Never` → genuinely proves cancellation-NACK. Assertions are on observable publishing behavior through the real runner. Not tautological, not brittle full-bus.

### SC7 — `dotnet test IntegrationInfra.slnx` passes; no regressions in the other 6 projects — **PASS**
- All 7 projects green, 0 failed / 0 skipped: Conducting 30, Kernel 33, FaultGovernance 20, Conversation 10, Emission 15, Reporting 15, Job 31. Matches execution_notes exactly.

---

## Assumption Dispositions & Findings

- **A1 (CollectorCommonDependencies in-scope)** — supported; placed at `Conducting/DependencyInjection/`, body verbatim, no Emission drag. PASS.
- **A2 (BaseFlowHandler EXCLUDED)** — supported; source `Contracts/Handlers/{BaseFlowHandler,ITopicHandler}.cs` not carried anywhere; README + assumptions record the follow-up. PASS.
- **A3 (CreatePipeline verbatim vs Kernel residue)** — supported; body + Kernel values verified identical. PASS.
- **A5 (invariants survive namespace-only move)** — supported behaviorally by the two green runner tests + the byte-identical body sweep. PASS.
- **DAG-correction finding (Conducting→Job AND Conducting→Emission)** — **REAL and HONESTLY RECORDED, not hidden.** `CollectorTriggerParsing.cs` line 1 imports `Cymulate.IntegrationInfra.Emission` and reads `AdapterGlobalDefaults.{AssetsFileName,FindingsFileName}`; AdapterTopics pulls Job. execution_notes.md logs both edges as findings; README §"Dependencies (DAG)" explicitly lists Emission + Job and flags the Emission edge as a tracked unification follow-up (sibling of Job→Emission). This directly and openly contradicts the original charter's "does NOT depend on Emission" — the honest correction is the right call.

## Unsupported Claims / Gaps

- **Contract-wording deviation (not a defect):** SC5 literally asks the README to state "Conducting does NOT depend on Emission." Execution found that premise FALSE (thin constant-only edge) and wrote the corrected DAG instead, disclosing it as a finding. This is truth-over-contract, properly surfaced — I do not treat it as a failure, but flag it so the operator is aware the literal SC5 sub-clause was superseded by reality.
- **XML-doc completeness:** only primary public entrypoints are documented; no CS1591 enforcement. Consistent with prior carries and disclosed. Below the bar for "XML docs on public members" if read maximally, but the load-bearing public surface is covered.
- No claim in execution_notes was found to be false or overstated. Build/test numbers reproduce exactly.

---

## Verdict: **SATISFIED**

All 7 success criteria PASS. Verbatim carry holds (38/38 bodies identical bar one documented type-rename reference rewire). Kernel diff empty, isolation clean, source unmutated, D1/D2/D3 honored, both runner invariant tests genuinely exercise the real runner, build clean, all 7 test projects green. The Conducting→Emission DAG correction is real and honestly recorded rather than hidden. The only deviation from literal contract text (the Emission DAG line in SC5) is a correct truth-over-premise correction, disclosed. Residual items (Emission-edge unification, BaseFlowHandler home, D3 reshape, exhaustive XML docs) are properly tracked as non-blocking follow-ups.

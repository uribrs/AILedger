# Execution Notes

Action log (append-only; crash-resilience requirement). Each entry: timestamp · action · result.

## Contract phase
- 2026-06-30_1146 · prompt-contract-designer · Full-tier contract created. Source edges analyzed this
  session (transport rewire, Orchestration reclaim ×2, Common DTOs ×2). A1–A3 VALIDATED; A4/A5/A6 OPEN
  (non-blocking: DTO drag-check, shared-shapes namespace, test-project choice). Contract VALID.

## Execution phase
- 2026-06-30 · S1 · Read both envelope DTOs — clean (only System.Text.Json + each other). A4 VALIDATED.
  Relocated `CollectorError` + `CollectorPartialCompletionMetadata` → `src/IntegrationInfra/Envelopes/Common/`,
  ns `Cymulate.IntegrationInfra.Envelopes.Common`, with XML docs. DONE.
- 2026-06-30 · S2 · Reclaimed `FlowExceptionHandling` (Models/, flat ns) + `AdapterFlowFailureHandling`
  (root, flat ns) into FaultGovernance, transport ref → Kernel.Transport. FINDING: AdapterFlowFailureHandling
  is a MIXED type — `ClassifyUnhandledException` (governance) + `PublishError*/PublishFailureCompletion*`
  (Reporting/Conducting publishing). Carried verbatim per the no-reshape rule; flagged in code + README as a
  future separation candidate. DONE.
- 2026-06-30 · S3/S4 · Relocated Resilience/* (25) + folded Recovery/* (3) into FaultGovernance/
  {Logic,Models,Policies,Recovery} via faithful `cp` (verbatim). NAMESPACE CORRECTION: source uses a FLAT
  `…Resilience` ns for Models/Logic/IAdapterFailurePolicy and `…Resilience.Policies` for the policy
  implementations (child sees parent automatically in C#). Mirrored exactly → flat
  `Cymulate.IntegrationInfra.FaultGovernance` + `.Policies` + `.Recovery`. DONE.
- 2026-06-30 · S5 · Namespace-prefix substitution via `find -exec perl` (first attempt failed silently on a
  long shell var; redone correctly): Events.Common→Envelopes.Common, Session.TransportErrorHandling→
  Kernel.Transport, Orchestration→FaultGovernance, Shared.Recovery→FaultGovernance.Recovery,
  Shared.Resilience→FaultGovernance. Special rename `UnknownFlowRetryPolicy`→`UnknownFlowRetryClassification`
  (1 site, UnknownFlowFailurePolicy.cs). Verified: 0 residual `Integration.Adapters.Shared` refs;
  AdapterHttpRequestFailedException not referenced (no Kernel.Exceptions using needed). Main project builds
  0 errors — confirms all references resolved. DONE.
- 2026-06-30 · S6 · Extended `FaultGovernance/README.md` (Recovery fold, reclaim incl. the mixed-type note,
  shared-shapes dependency). XML docs: added new DTO docs (S1), reclaimed-type docs (S2), and full docs on
  the public entry `AdapterResilienceStrategy` (class + ctor + CreateDefault chain-order + DecideAsync).
  The 25 verbatim-carried files retain their original XML docs (the non-obvious members were already
  documented at source — IAdapterFailurePolicy's budget-ownership remark, AdapterFailureContext,
  RecoveryBudgetDecision). DELIBERATE SCOPE: did NOT add boilerplate per-member docs to self-evident
  positional records/properties — "excellent docs, not noise." Per-member doc enrichment on the model
  records is a tracked polish item, not a behavior gap.
- 2026-06-30 · S7 · Added `tests/IntegrationInfra.FaultGovernance.Tests` (xUnit), added to slnx. 9 tests:
  chain order + short-circuit via DecideAsync (UserCancellation→CancelWithoutPublish; definitive bug→FailFast;
  transient transport+backoff→RequestDeferredRecovery; unknown→RethrowForUnknownRetry; plain HttpRequestException
  →Fallback PublishFailure), CreateDefault empty-guard, FlowExceptionHandling round-trip, ToCollectorError
  mapping. (Chain order tested behaviorally — the policy list is private by design.) DONE.
- 2026-06-30 · S8 · Full solution build 0 errors. Tests: 33 (Kernel) + 9 (FaultGovernance) = 42 pass.
  Invariants: Kernel untouched (empty git diff); 0 `Shared.*` refs in src; source repo unmodified
  (mtime Jun 22, pre-task). DONE.

## Residual risk / notes
- AdapterFlowFailureHandling mixed responsibility (classify vs publish) — future separation candidate when
  Reporting/Conducting are carved. Carried verbatim now.
- Envelope DTOs in `Envelopes.Common` are an interim home (a slice of the future Events carve); re-home then.
- XML-doc coverage is type-level + entry-surface + non-obvious members; exhaustive per-member docs on the
  verbatim model records deferred as polish.

## Review phase
- Verifier (review/verifier-1.md): **PASS** (one PARTIAL). Normalized diff of all 28 relocated files vs
  source = exactly one content change (the UnknownFlowRetryClassification rename); GetDelay/strategy/budget/
  checkpoint byte-identical. 42 tests pass; Kernel untouched; source unmutated. PARTIAL: SC5 XML docs (18/33
  types undocumented — matched source, none removed).
- Code-reviewer (review/code-reviewer-1.md): **no blockers/majors**. Verbatim logic confirmed correct
  (GetDelay overflow clamp, recovery budget ordering, checkpoint round-trip, decision-record equality).
  Minors M1/M2/M3 + most nits = pre-existing source behavior. M4 = missing tests for GetDelay/Evaluate.
- Repairs (additive only, no behavior change to carried code):
  - M4 + test nits: added RecoveryBudgetAndBackoffTests.cs (11 tests for RecoveryBudgetEvaluator.Evaluate
    + AdapterBackoffPlan.GetDelay); fixed `Assert.Equal(true/false, bool?)` nits. Now 33 + 20 = 53 pass.
  - SC5: documented the public surface (AdapterResilienceStrategy entry, the AdapterFailureDecision
    vocabulary, AdapterBackoffPlan + GetDelay, AdapterFailureHandling + ToCollectorError) + the new DTOs and
    reclaimed types; removed a redundant self-namespace using introduced by substitution.
- Recorded as accepted (NOT changed — verbatim battle-tested source, operator no-reshape rule): M1 (jitter
  range-check throws even when UseJitter=false), M2 (MaxRetries<=0 beats DelaySequence), M3
  (RecoveryParsingHelper culture/negative leniency), remaining nits.
- Residual / offered follow-up: one-line docs on the 8 policy classes + self-evident model records.

## Post-review rename (operator)
- Collector→Adapter on the envelope DTOs: AdapterError, AdapterPartialCompletionMetadata, ToAdapterError.
  Files renamed; all refs updated (executor, MappedFailurePolicy, AdapterFailureDecision.CompletePartial,
  AdapterFailureHandling, tests, READMEs). Build 0 errors; 53 tests pass. Wire contract unchanged (see D8).
  Domain-prose comments mentioning the "collector" family and other concerns' charter READMEs left as-is.

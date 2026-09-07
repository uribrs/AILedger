# Constraints

- Carry logic/behavior VERBATIM — namespace-only rewrite + cross-concern rewires; no logic changes.
- Source repo `/Users/user/Dev/Uri/localprojects/IntegrationsInfra` is REFERENCE-ONLY — never mutate.
- Target namespace `Cymulate.IntegrationInfra.Conducting(.*)`; net8.0; RootNamespace `Cymulate.IntegrationInfra`.
- Namespace layout: mirror source sub-structure (`.Bus.Logic`, `.Bus.Models`, `.Collectors[.Guards/.Recovery/.Recovery.Models/.Triggers/.Validation]`) — not flattened.
- KEEP `Collector*` type names (genuine family identity) and existing `Adapter*` names. No D8 renaming in this carry.
- Bus/Logic executors stay `internal` (as in source).
- Cross-concern dependencies only in the allowed direction: Conducting → Kernel + Conversation + FaultGovernance + Reporting + Envelopes.Common + SDK. Conducting must NOT depend on Emission.
- No residual `Cymulate.Integration.Adapters.Shared.*` references after rewire.
- Already-carried types are referenced, never re-carried: session lifecycle (Conversation); AdapterFlowFailureHandling + FlowExceptionHandling + AdapterResilienceStrategy + AdapterRecoveryContext + AdapterRecoveryResult (FaultGovernance); AdapterInProcEventRaiser + AdapterInProcEventHub + AdapterResultDiagnosticsEnricher + Diagnostics/* + Events root (Reporting); UnknownFlowRetryClassification (Kernel/Transport).
- Kernel, Conversation, Emission, Job, Reporting, Envelopes.Common, and existing FaultGovernance files must be UNCHANGED (git diff empty) EXCEPT FaultGovernance gaining the new `UnknownFlowRetryPolicy.CreatePipeline` file (+ any needed using).
- KEEP external deps: `Cymulate.Integration.Sdk.*`, Polly, `Microsoft.Extensions.*`, DefensiveToolkit (if referenced).
- Packages resolved build-driven at central-managed floor versions; STOP if a package/version cannot resolve.
- XML docs on public members (add where missing; preserve existing).
- `dotnet build` clean (0 errors; pre-existing NU1507/NU1900 warnings acceptable) + `dotnet test` pass.

## Hard behavioral invariants (carry verbatim — the reason this concern exists)
- Partial-success-wins: publish the partial-success result BEFORE any failure (AdapterBusEntrypointRunner + AdapterBusPartialSuccessPublisher).
- Cancellation is NACK-not-failure: on `OperationCanceledException`, publish NO completion/error event (message NACKed + requeued; checkpoint resume handles continuation).
- Success-completion ordering: flow executes → on success publish completion(Success=true) → build success result.
- Resilience policy nesting: flow-retry pipeline is OUTER, resilience strategy is INNER (AdapterBusFlowExecutor).
- Deferred-wait externalization: `ServerSuggestedRetryDelayException` must propagate to the resilience strategy (not be swallowed by the bus).
- Checkpoint-write-before-step-advance (bus/resume executors + resume checkpoint manager).
- Best-effort shutdown in `finally` using `CancellationToken.None`.
- Best-effort in-proc forwarding must NEVER affect publishing semantics.

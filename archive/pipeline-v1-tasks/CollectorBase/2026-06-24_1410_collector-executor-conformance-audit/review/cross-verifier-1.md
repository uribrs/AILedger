# Cross-Verifier 1 — Adversarial Conformance Re-Test (CollectorExecutor vs YamlCollector vs Native)

**Headline verdict.** CollectorExecutor genuinely routes through the Shared collector pipeline
(`AdapterBusEntrypointRunner` / `CollectorResumeRunner`) and is the closest of the two YAML collectors to
the native Shared-substrate contract — it actually consumes `DefaultSessionProvider` + `AuthSelection`,
`CollectorNdjsonPublisher`, the Shared resilience chain and recovery budget. **The 7 stated conformance
claims are substantially TRUE; 2 are OVERSTATED, 0 are FALSE.** The two overstatements are a redundant
double-restore on resume (idempotent, harmless) and a "page metric" semantic mismatch in the success path
(`pages` reflects pagination depth of the last step, not output-file count). No claim is fabricated and no
data-loss or wrong-resume defect was found. The biggest *operational* truth to record is the contrast, not
a CollectorExecutor defect: **YamlCollector (the other repo) DIVERGES from the native substrate** — it runs
its own `Cymulate.Integration.Yaml.Engine` (own HttpClient, own `IAuthenticator`, own pagination) and
publishes via a raw `DataBatchRequest` rather than Shared egress; it conforms only on the bus/resume shells.

---

## Per-subsystem conformance table

Legend: CONFORMS / PARTIAL / DIVERGES. Native baseline = the contract in `native-reference-baseline.md`.

| # | Subsystem | Native contract | **YamlCollector** (`cymulate-integration-adapters`) | **CollectorExecutor** (under audit) |
|---|---|---|---|---|
| 1 | **Bus entrypoint** | one `DelegateCollectorBusEntrypointSource` → `CollectorBusEntrypointDefinitionBuilder.Build` → `AdapterBusEntrypointRunner.RunAsync` | **CONFORMS** — `YamlCollectorAdapter.cs:175-212` builds it and calls `RunAsync` at `:211`. Supplies all 13 required delegates. | **CONFORMS** — `CollectorExecutorAdapter.cs:156-203`, `RunAsync` at `:203`. All 13 required delegates supplied (see Claim 2). |
| 2 | **Resume** | `IResumableAdapter` → `CollectorResumeRunner` + `CollectorResumeDefinition<TState>` | **CONFORMS** — `YamlCollectorAdapter.cs:215-260` (`CollectorResumeDefinition<YamlCollectorCheckpointState>`, `CollectorResumeRunner.ResumeAsync` at `:257`). | **CONFORMS w/ redundancy** — `CollectorExecutorAdapter.cs:206-245`. Re-restores progress inside the flow (see Claim 3). |
| 3 | **Session / transport** | `DefaultSessionProvider` + `AdapterSessionLifecycle`; `SessionSpec.Auth = AuthSelection.*`; secrets from dispatch | **DIVERGES** — does NOT use `DefaultSessionProvider`/`SessionSpec`/`AuthSelection`. The engine creates its own `HttpClient` (`IntegrationEngine.cs:171`) and its own `IAuthenticator` (`:181`). A parallel transport substrate. | **CONFORMS** — `DefaultSessionProvider.Create` + `AdapterHttpClient` (`CollectorExecutorRunner.cs:301-303`); `SessionSpec.Auth = AuthSelection.*` via `BuildAuthSelection` (`:887-990`); secrets only from `creds` (dispatch). Variation: builds the session inline rather than via `AdapterSessionLifecycle`/`InitializeAsync` (stateless per-run), but the substrate is the Shared one. |
| 4 | **Egress + page numbering** | Shared `CollectorNdjsonPublisher.*PageAsync`; mandatory `{name}_{page:D6}.json`, `page>=1`; monotonic per-stream counter in checkpoint, re-seeded on resume | **DIVERGES (egress) / CONFORMS (numbering shape)** — builds the filename itself and publishes a raw `DataBatchRequest` (`IsbExecutionSink.cs:54-58`), bypassing Shared `CollectorNdjsonPublisher` and its `page>=1`/naming guard. Page number = `pageState.PagesRetrieved + 1` (`IntegrationEngine.cs:481-482`) — monotonic, decoupled, but not the Shared publisher. | **CONFORMS** — `CollectorNdjsonPublisher.PublishAssets/FindingsUtf8PageAsync` with caller counter `++runCtx.AssetsPage/FindingsPage` (`CollectorExecutorRunner.cs:457-460`); counter persisted in `CheckpointState.AssetsPage/FindingsPage` (`CheckpointState.cs:38-39`) and re-seeded on resume (`CollectorExecutorRunner.cs:279-280`). Decoupled from pagination position. |
| 5 | **Resilience** | per-vendor `AdapterResilienceStrategy` (fixed `CreateDefault` order) + separate http.package session pipeline; `FlowExceptionClassifier`; `FlowRetryPipelineBuilder` | **PARTIAL** — supplies `ResilienceStrategy = YamlResilienceStrategyFactory.Create()` + `FlowExceptionClassifier` (`YamlCollectorAdapter.cs:207-208`) but **no `FlowRetryPipelineBuilder`** (bus default `UnknownFlowRetryPolicy` applies) and the engine ALSO has its own internal `RetryPolicyFactory`/backoff — two retry layers, one in-engine + the bus default. | **CONFORMS w/ deliberate design** — `ResilienceStrategy = BuildResilience()` (Shared `CreateDefault` + a classified-code pass-through policy, `CollectorExecutorAdapter.cs:291-301`), `FlowExceptionClassifier` (`:303-306`), and `FlowRetryPipelineBuilder = ResiliencePipeline.Empty` to avoid double-retry (`:200`). See Claim 6. |
| 6 | **Events** | implement `ICollectorEventSink` (forward via `AdapterInProcEventForwarder`); declare 5 SDK events; best-effort in-proc only | **PARTIAL** — does NOT implement `ICollectorEventSink`; declares only **3** SDK events (`ProgressChanged/OperationCompleted/ErrorOccurred`, `YamlCollectorAdapter.cs:55-57`), no `BatchProduced`/`CheckpointAdvanced`. Real publishing is the orchestrator's, so functionally fine, but it is NOT the native sink shape. | **CONFORMS** — implements `ICollectorEventSink` + forwards via `AdapterInProcEventForwarder` (`CollectorExecutorAdapter.cs:63-67`); declares all **5** SDK events (`:56-60`). Best-effort only; real path is the bus `PublishAsync`. |
| 7 | **Credential / RUN-envelope ingestion** | `RunPayloadCredentialHydrator.Hydrate` (endpoint + creds, encrypted blob → builder decrypt) + `action.lastRanAt` floor; secrets never from static config | **PARTIAL/CONFORMS** — has its own `YamlPlatformEventRequestParser` + payload-credential handling; uses the encryption service; does not visibly route through `RunPayloadCredentialHydrator` (own parser layer). Secrets from dispatch. | **CONFORMS** — `HydrateRunEnvelope` calls `RunPayloadCredentialHydrator.Hydrate(..., endpointConfigKey:"base_url")` (`CollectorExecutorRunner.cs:141`), decrypts `_encryptedCredentials` via `IEncryptionService.DecryptIfEncrypted` (`:149`), and `DeriveFloorFromRunEnvelope` reads `action.lastRanAt` (`:162-174`). Credentials decrypted via `DecryptConfiguration` (`:119`); secrets never from YAML. See Claim 5. |
| 8 | **Checkpoint contract** | per-vendor state record (Flow, page counter, cursor, totals, CreatedUtc, BaseDateUtc, IsDryRun); `SetState` BEFORE `AdvancePage`; staleness-gated `CanResumeFrom` | **CONFORMS** — `YamlCollectorCheckpointState` versioned; ordering invariant documented + enforced (`IsbExecutionSink.cs:11-14,78+`); `CanResumeFrom` strategy allow-list + `_maxCheckpointAge` gate (`YamlCollectorAdapter.cs:262-284`). | **CONFORMS** — `CheckpointState` versioned + fingerprinted (`CheckpointState.cs`); `WriteCheckpoint` does `SetState` then caller does `AdvancePage` (`CollectorExecutorRunner.cs:472-473`); fingerprint + step-shape + cursor-TTL + restart-if-stale gates (`:250-293`). Staleness gate is per-flow `restart_if_stale`/`cursor_ttl` rather than a flat age cap (a richer, conformant variant). |

**Net:** YamlCollector conforms to the *orchestration shells* (bus, resume, checkpoint) but **diverges on the
three substrate subsystems the baseline cares most about — session/transport, egress, and (partly)
resilience/events** — because it deliberately runs a self-contained engine. CollectorExecutor conforms on
all eight, modulo the two overstatements below.

---

## The 7 claim re-tests (TRUE / OVERSTATED / FALSE + evidence)

### Claim 1 — Full interface set + 5 SDK events. **TRUE.**
`CollectorExecutorAdapter.cs:32-37` declares `IAssetsCollectorAdapter, IFindingsCollectorAdapter,
ICollectorEventSink, IResumableAdapter, IAsyncDisposable`. `ICollectorAdapter` is satisfied transitively
(it is the base the two capability interfaces compose; the 2 collector events `BatchProduced`/
`CheckpointAdvanced` are declared at `:59-60`). All 5 SDK events declared `:56-60`. `IAsyncDisposable`
implemented `:121`. Sink methods forward via `AdapterInProcEventForwarder` `:63-67`.
*Contrast:* YamlCollector implements only `IIntegrationAdapter<ICollectorCapability>, IResumableAdapter`
with 3 events and no sink (`YamlCollectorAdapter.cs:22,55-57`) — so this claim is a genuine CollectorExecutor
strength, not boilerplate parity.

### Claim 2 — Routes through `CollectorBusEntrypointDefinitionBuilder` + `DelegateCollectorBusEntrypointSource` (all 13 required delegates) → `AdapterBusEntrypointRunner.RunAsync`. **TRUE.**
`CollectorExecutorAdapter.cs:156-203`. The 13 `required` members of
`DelegateCollectorBusEntrypointSource` (`DelegateCollectorBusEntrypointSource.cs:17-29`) are all supplied:
`VendorName:159`, `ExtractRequest:160`, `GetFlowName:161`, `IsConfigured:162`, `BuildConfiguration:163`,
`SetConfiguration:164`, `ValidateConfigurationAsync:165`, `InitializeAsync:167`, `ShutdownAsync:168`,
`CollectAssetsAsync:172`, `CollectFindingsAsync:173`, `BuildSuccessResult:174`, `RaiseError:186`. `RunAsync`
at `:203`. The success completion (`CompletionRequest(Success:true)`) is published by the runner itself at
`AdapterBusEntrypointRunner.cs:115-118`, and the page-data path (`StreamBatchRequest`) is published inside
the collect delegate via `CollectorNdjsonPublisher` — identical to native (the bus flow executor only calls
the delegate; it does not publish batches itself — confirmed in `AdapterBusFlowDispatcher` /
`AdapterBusStrategyFlowExecutor.cs:25-49`).

### Claim 3 — Resume genuinely goes through `CollectorResumeRunner` + `CollectorResumeDefinition<CheckpointState>` and re-seeds correctly. **OVERSTATED (mechanically true; redundant re-restore).**
The wiring is real: `CollectorExecutorAdapter.cs:218-245` builds `CollectorResumeDefinition<CheckpointState>`
and calls `new CollectorResumeRunner(...).ResumeAsync(...)`. Because `ResilienceStrategy` is non-null, the
live path is `CollectorResumeStrategyExecutor` (`CollectorResumeRunner.cs:115-117`).
**Re-seed gap (operational, low severity):** the Shared `CollectorResumeSetup.TryCreateExecutionContext`
ALREADY creates the progress context, calls `RestoreProgress` and `AdapterRecoveryBudget.SeedFromPersistedState`
(`CollectorResumeSetup.cs:54-69`) **before** invoking the flow. CollectorExecutor's flow then calls
`progress.RestoreProgress(...)`, `progress.SetCursor(...)` and `AdapterRecoveryBudget.SeedFromPersistedState(...)`
**again** on the same context (`CollectorExecutorRunner.cs:283-286`). It is idempotent (same checkpoint
values), so it is harmless, but it is a divergence from native (where `RestoreProgress` is owned solely by the
Shared setup and the flow does not re-restore). The output-page re-seed (`AssetsPage`/`FindingsPage`,
`:279-280`) is NOT redundant — the Shared setup does not know about those keys — and is correct. Verdict
OVERSTATED only because "re-seeds correctly" hides a double-restore the native path does not do.

### Claim 4 — Output page number is a monotonic per-emit-target counter persisted in `CheckpointState`, decoupled from the pagination cursor. **TRUE.**
`CheckpointState.AssetsPage`/`FindingsPage` (`CheckpointState.cs:38-39`) are distinct from the pagination
position fields (`Cursor/Offset/NextPage/NextUrl/Watermark`, `:27-31`). Publish uses
`var pageNo = isFindings ? ++runCtx.FindingsPage : ++runCtx.AssetsPage;` (`CollectorExecutorRunner.cs:457`)
and passes `pageNo` to the publisher (`:459-460`). `WriteCheckpoint` persists both fields every write
(`:746-747`); resume re-seeds them (`:279-280`). I searched for any path using the pagination position as
the page number and found none — the publisher receives only `runCtx.AssetsPage/FindingsPage`. The Shared
publisher never derives a page (it requires `page>=1`, `CollectorOutputDefaults.cs:23-26`). Decoupling holds
even across `for_each` items and multi-page steps (the counter is on `RunContext`, not reset per step).

### Claim 5 — RUN-envelope ingress (creds incl. encrypted blob; `action.lastRanAt` → floor) works AND is reachable; Runner shape is a no-op. **TRUE.**
- **Reachable / hydrates:** `BuildEffectiveInputs` (`CollectorExecutorRunner.cs:115-132`) is called from both
  `Preflight` (`:101`) and `RunAsync` (`:227`). It calls `HydrateRunEnvelope` (`:123`/`:137-157`) →
  `RunPayloadCredentialHydrator.Hydrate(raw, config, endpointConfigKey:"base_url")` (`:141`). The hydrator
  writes the endpoint into `config["base_url"]` from any of `apiEndpoint/baseUrl/host/url/...`
  (`RunPayloadCredentialHydrator.cs:12,36-40`) — so a production RUN can supply `base_url`, which the runner
  then requires (`:228-229`).
- **Encrypted blob decrypts:** plain-JSON creds expand into config in the hydrator; an opaque blob is stored
  as `config["_encryptedCredentials"]` (`RunPayloadCredentialHydrator.cs:76`) and CollectorExecutor decrypts
  it via `_encryptionService.DecryptIfEncrypted(blob)` then expands into `credentials`
  (`CollectorExecutorRunner.cs:143-156`). The top-level `platformEvent.Credentials` are also decrypted via
  `DecryptConfiguration` (`:118-119`). Both methods exist on the host `IEncryptionService` (NuGet; confirmed
  by the Runner's `LocalRunHost.cs:52,54` and the test fake's matching signatures).
- **`action.lastRanAt` floor:** `DeriveFloorFromRunEnvelope` (`:162-174`) reads
  `start.Payload.Action.LastRanAt` and writes canonical `base_date`/`start_time`/`since_unix`/`end_time`
  inputs (explicit inputs win).
- **Runner no-op:** the local Runner builds `Payload = { yaml, inputs, config }` only — **no `action`
  object** (`Runner/Program.cs:118-130`). `RunPayloadCredentialHydrator` early-returns when there is no
  `action` (`RunPayloadCredentialHydrator.cs:25-30`) and `DeriveFloorFromRunEnvelope` early-returns when
  `TryParseStartEnvelope` finds no `action.lastRanAt` (`CollectorExecutorRunner.cs:164-167`). So both are
  genuine no-ops for the Runner shape, as claimed.

### Claim 6 — No double-resilience; classified vendor codes survive to the published failure (not flattened to ADAPTER_EXCEPTION). **TRUE.**
- **No double-retry:** the bus wraps the whole flow in `FlowRetryPipelineBuilder` (`AdapterBusFlowExecutor`).
  CollectorExecutor passes `ResiliencePipeline.Empty` for both process and resume
  (`CollectorExecutorAdapter.cs:200,240`), so the bus adds no retry loop on top of the engine's internal page
  loop. The strategy executor itself has no extra Polly loop — it calls the delegate once
  (`AdapterBusStrategyFlowExecutor.cs:25-49`).
- **No double recovery-budget (the subtle part I specifically chased):** the engine's internal failure path
  (`ExecuteDecisionAsync`, `CollectorExecutorRunner.cs:781-816`) runs the full Shared
  `AdapterFailureDecisionExecutor`, which only touches the recovery budget for budgeted
  `RecoverAndRetry`/`RequestDeferredRecovery` decisions (`AdapterFailureDecisionExecutor.cs:131-140`). When it
  does (e.g. `body_retry_after`/`shared` → `RequestDeferredRecovery`, `BodyRetryAfterFailureStrategy.cs:31`,
  `SharedChainFailureStrategy.cs:19`), the result is a `PartialResult`/`PartialWaitRequired`
  (`AdapterFailureDecisionExecutor.cs:360`), which `RunAndCountAsync` re-surfaces as a
  `ServerSuggestedRetryDelayException` (`CollectorExecutorAdapter.cs:265-273`). At the bus, that is classified
  by `ServerSuggestedRetryDelayPolicy`, which emits `RequestDeferredRecovery` with **`UseRecoveryBudget:
  false`** (`ServerSuggestedRetryDelayPolicy.cs:76-80`). So the bus pass is unbudgeted — the budget is written
  exactly **once**. For terminal `PublishFailure`/`FailFast`, the engine's executor never touches the budget,
  and the bus re-runs only a `PublishFailure` pass (unbudgeted) that does the single real publish. *This is a
  deliberately careful design, not an accident — I initially suspected a 2× budget bug and the
  `UseRecoveryBudget:false` flag is exactly what prevents it.*
- **Codes survive:** the engine throws `CollectorExecutorFlowException` carrying the classified
  `ErrorCode`/`IsRetryable` (`CollectorExecutorRequest.cs:21-32`). `BuildResilience()` adds a
  `DelegateAdapterFailurePolicy` that maps that exception back to `PublishFailure(handling)` with the same
  code (`CollectorExecutorAdapter.cs:291-301`), and `ClassifyFlowException` does likewise for the classifier
  path (`:303-306`). The decision's `Handling.ErrorCode` flows unchanged into the published
  `ErrorRequest`/`CompletionRequest` (`AdapterBusFailureContextFactory.GetDecisionHandling` →
  `AdapterFlowFailureHandling.PublishErrorAndFailureCompletionAsync`). Without `BuildResilience`'s policy the
  Shared fallback would flatten to `ADAPTER_EXCEPTION` (`AdapterFlowFailureHandling.ClassifyUnhandledException`,
  `DefaultErrorCode = "ADAPTER_EXCEPTION"`) — the policy is load-bearing and present. Test corroboration:
  `CollectorExecutorTests.cs:647` asserts `VENDOR_HTTP_ERROR` survives, `:704` asserts
  `ADAPTER_TRANSIENT_TRANSPORT` survives.

### Claim 7 — `CollectAssetsAsync`/`CollectFindingsAsync` throw `NotSupportedException`: real gap vs native, or acceptable for a generic engine? **Acceptable (not a conformance gap) — TRUE as stated.**
`CollectorExecutorAdapter.cs:131-135` throws `NotSupportedException` from the explicit-interface capability
methods. These take a fixed `(fql, baseDateUtc, isDryRun)` shape that cannot express a profile-driven run.
The live collection path is the bus collect *delegates* (`CollectAssetsAsync`/`CollectFindingsAsync` set on
the `DelegateCollectorBusEntrypointSource` at `:172-173`), which the dispatcher invokes — the same mechanism
native collectors use. Native collectors implement the public capability methods because their forward-collect
code lives there and the delegate forwards to it; CollectorExecutor inverts this (delegate → runner) because
there is no fixed-shape capability to honor. The host drives collectors through `ProcessAsync`/the bus, not by
calling `IAssetsCollectorAdapter.CollectAssetsAsync` directly, so the throw is unreachable in the real path.
*Caveat:* this is conformant **only** as long as no host code path calls the public capability method directly
(e.g. a connection-probe or a non-bus invocation). I found no such call in this repo, but that is the one
assumption the throw rests on — worth a one-line guard test if a host ever probes capabilities directly.
Qualys already sets a precedent of throwing `NotSupportedException` for its unsupported assets stream
(baseline §top-level), so a `NotSupported` capability method is within the established native idiom.

---

## "Looks-conformant-but-isn't" findings

1. **(Low) Resume double-restore.** `CollectorExecutorRunner.RunAsync` re-calls `RestoreProgress` /
   `SetCursor` / `SeedFromPersistedState` (`:283-286`) on the progress context the Shared
   `CollectorResumeSetup` already restored (`CollectorResumeSetup.cs:54-69`). Idempotent today, but it is
   redundant work the native flows do not do and a latent trap if the two restore sources ever disagree (e.g.
   if a future change makes the runner restore from `CheckpointState` fields while the bus restores from
   `AdapterCheckpoint.CurrentPage`). Recommend the flow trust the bus-supplied, already-restored context for
   progress/budget and only seed the output-page counters it owns.

2. **(Low) Success-path `pages` metric is the last step's pagination depth, not output-file count.**
   `RunAsync` accumulates `pages += sr.Pages` where a fetch step's `Pages` is its final pagination `page`
   value (`CollectorExecutorRunner.cs:514`, `StepResult.Completed(emitted, findingsEmitted, page)`), while the
   real output-file count is `AssetsPage + FindingsPage`. The success `Data["pages"]` (`:363`) can therefore
   misreport for multi-page/`for_each`/hydrate flows. This is telemetry only (the publisher and checkpoint use
   the correct `AssetsPage/FindingsPage`), so it does not affect data, resume, or done/error shape — but the
   "pages" number a downstream done-message surfaces is not the file count and should not be trusted as such.
   This is what makes the page-numbering claim's *framing* slightly overstated even though the file-numbering
   itself (Claim 4) is correct.

3. **(Informational, not a CollectorExecutor defect) YamlCollector is the real divergence.** If the audit's
   implicit question is "are the two YAML collectors equivalent peers of native?", the answer is no:
   YamlCollector bypasses Shared session/transport (`IntegrationEngine.cs:171,181`), Shared egress
   (`IsbExecutionSink.cs:54-58` publishes a raw `DataBatchRequest`, not `CollectorNdjsonPublisher`), does not
   implement `ICollectorEventSink`, and has a second in-engine retry layer under the bus default retry pipeline
   (no `FlowRetryPipelineBuilder` override → potential double-retry on transient transport, the inverse of
   CollectorExecutor's `ResiliencePipeline.Empty` discipline). CollectorExecutor is the more conformant of the
   two by a wide margin.

**Does routing through the bus produce the SAME done/error/progress publishing as native, or just compile?**
Same, not just compile. Success → `AdapterBusEntrypointRunner` publishes `CompletionRequest(Success:true)`
(`AdapterBusEntrypointRunner.cs:115-118`) then `BuildSuccessResult`. Failure → the strategy executor's
`PublishFailureAsync` hook calls `AdapterBusFailurePublisher.PublishFailureAsync` →
`AdapterFlowFailureHandling.PublishErrorAndFailureCompletionAsync` (ErrorRequest + non-retryable
CompletionRequest(false)). Partial → `AdapterBusPartialSuccessPublisher` publishes
`CompletionRequest(Success:true)` (`:35-38`). Page data → `CollectorNdjsonPublisher.*Utf8PageAsync` inside the
delegate, exactly as native. Cancellation is NACK/requeue with no failure publish
(`AdapterBusEntrypointRunner.cs:125-139`). The only deltas from native are the two low-severity items above,
neither of which changes the published done/error/progress *shape*.

---

## Final tally

- **Claims re-tested:** 7
- **TRUE:** 5 (Claims 1, 2, 4, 5, 6) + Claim 7 (acceptable-by-design)
- **OVERSTATED:** 1 firmly (Claim 3, redundant resume re-restore); Claim 4's *framing* partially overstated by
  the `pages` telemetry mismatch (finding #2) though the file-numbering claim itself is TRUE.
- **FALSE:** 0
- **Looks-conformant-but-isn't findings:** 2 low-severity in CollectorExecutor (resume double-restore;
  `pages` metric), 1 informational (YamlCollector substrate divergence).

**Overstated/false count: 1 overstated, 0 false.** No defect blocks the conformance conclusion; CollectorExecutor
genuinely rides the Shared collector pipeline and substrate. Recommend fixing the two low-severity items
before claiming byte-for-byte native parity in telemetry/resume.

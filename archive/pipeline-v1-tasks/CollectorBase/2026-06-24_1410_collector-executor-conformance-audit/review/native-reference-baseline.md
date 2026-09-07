# Native-Reference Baseline — How Cymulate's Hand-Coded Collectors Consume the Shared Substrate

**Purpose.** This is the *reference contract* against which the YAML `CollectorExecutor` (and any other
audit) is compared. It documents, for each of the 8 Shared subsystems, **the canonical pattern** as
exhibited by the five hand-coded native collectors, with file:line citations from **≥2 collectors** so
each claim is demonstrably the shared contract and not one vendor's quirk. Where a collector deviates, the
variation is called out.

**Source tree.** All paths are under
`/Users/user/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters/`. Shorthand:
- `Collectors/<Vendor>Collector/...` — the native collector being audited.
- `Shared/Cymulate.Integration.Adapters.Shared/...` — the substrate (cited as `Shared/...`).

**Collectors examined.** FalconCollector, TenableIoCollector, QualysCollector, DefenderVmCollector,
CortexXdrCollector. **CortexXdr — a native collector exists** (`Collectors/CortexXdrCollector/`); it is a
full hand-coded collector, not a YAML profile.

**Top-level shape (all five identical).** Every adapter is
`sealed class <Vendor>Collector : IAssetsCollectorAdapter, IFindingsCollectorAdapter, ICollectorEventSink,
IResumableAdapter, IAsyncDisposable` (Falcon `FalconCollector.cs:41`; Defender
`DefenderVmCollector.cs:28`; CortexXdr `CortexXdrCollector.cs:29`). Two deviations:
- **Qualys is findings-only** — `class QualysCollector : IFindingsCollectorAdapter, ICollectorEventSink,
  IResumableAdapter, IAsyncDisposable` (`QualysCollector.cs:29`); it wires `CollectAssetsAsync = (_,_,_,_)
  => CollectAssetsNotSupportedAsync()` which throws `NotSupportedException`
  (`QualysCollector.cs:188,339-340`).
- **Tenable's "findings" flow is a combined assets-then-findings run** on one progress context
  (`TenableIoCollector.cs:213-316`).

All five resolve their Shared dependencies identically in the ctor via
`CollectorCommonDependencies.Resolve(context, logger)` → `{ EncryptionService, SessionProvider }`
(Falcon `FalconCollector.cs:85-87`; Tenable `TenableIoCollector.cs:66-68`; Qualys
`QualysCollector.cs:66-68`; Defender `DefenderVmCollector.cs:64-66`; CortexXdr
`CortexXdrCollector.cs:65-67`), and construct a `CollectorInProcEventHub` + a per-vendor `*ResumeRunner`.

---

## 1. Bus entrypoint

**Canonical pattern.** `ProcessAsync(PlatformEvent, CancellationToken)` builds **one**
`AdapterBusEntrypointDefinition` via `CollectorBusEntrypointDefinitionBuilder.Build(new
DelegateCollectorBusEntrypointSource<TVendorTriggerRequest>{ ... })`, then returns
`await AdapterBusEntrypointRunner.RunAsync(_context, _logger, platformEvent, definition,
cancellationToken)`. The collector supplies a fixed set of delegates; the Shared runner owns the entire
lifecycle (progress-context creation, RUN-metadata resolution, correlation logging scope, configure →
validate → initialize → flow-execute → publish completion → build success result → shutdown).

**Citations (≥2):**
- Falcon: `FalconCollector.cs:229-290` (`ProcessAsync` → `CollectorBusEntrypointDefinitionBuilder.Build(new
  DelegateCollectorBusEntrypointSource<FalconCollectorTriggerRequest>{...})` → `AdapterBusEntrypointRunner.RunAsync` at 287-289).
- Tenable: `TenableIoCollector.cs:450-493`.
- Qualys: `QualysCollector.cs:175-232`.
- Defender: `DefenderVmCollector.cs:147-192`.
- CortexXdr: `CortexXdrCollector.cs:190-243`.
- Shared source of truth: `DelegateCollectorBusEntrypointSource<TRequest>`
  (`Shared/Orchestration/Collectors/DelegateCollectorBusEntrypointSource.cs:15-66`) and
  `AdapterBusEntrypointRunner.RunAsync` (`Shared/Orchestration/AdapterBusEntrypointRunner.cs:13-172`).

**Required delegates** (`required` on `DelegateCollectorBusEntrypointSource`,
`DelegateCollectorBusEntrypointSource.cs:17-29`) — every collector supplies all of these:
`VendorName`, `ExtractRequest`, `GetFlowName`, `IsConfigured` (always `() => _sessionSpec != null`),
`BuildConfiguration`, `SetConfiguration`, `ValidateConfigurationAsync`, `InitializeAsync`,
`ShutdownAsync`, `CollectAssetsAsync`, `CollectFindingsAsync`, `BuildSuccessResult`, `RaiseError`.

**Optional delegates** (`DelegateCollectorBusEntrypointSource.cs:31-43`) — supplied selectively:
`SetCurrentPlatformEvent` (all five set it: `e => _currentPlatformEvent = e`),
`OnFlowNameResolved` (Falcon `FalconCollector.cs:273-277` and Qualys `QualysCollector.cs:215-221` use it
to stamp `FlowName` onto `_configuration`; Tenable/Defender/CortexXdr omit it),
`FlowExceptionClassifier` (per-vendor, all five), `ResilienceStrategy` (all five — see §5),
`FlowRetryPipelineBuilder` (all five), `PartialSuccessResultBuilder` (Falcon only,
`FalconCollector.cs:283,292-304`), `RecoveryHandler` (Falcon only, `FalconCollector.cs:285`).

`BuildSuccessResult` is uniform: it calls `CollectorResultPayload.BuildWithCollectorTotals(evt, flowName,
total, _lastTotalAssetsCollected, _lastTotalFindingsCollected, isDryRun, baseDate)` and sets
`result.ProcessingTime = duration` (Falcon `FalconCollector.cs:252-271`; Tenable
`TenableIoCollector.cs:467-483`; Qualys `QualysCollector.cs:197-213`; CortexXdr
`CortexXdrCollector.cs:215-231`).

**The runner's invariant lifecycle** (`AdapterBusEntrypointRunner.cs`): create progress context
(`:32-41`) → resolve RUN metadata incl. `storageUrl` (`:42`, `ResolveRunMetadata` `:188-220`) → open a
canonical logging scope `{CorrelationId, Vendor, Topic, Flow}` (`:46-64`) → `ConfigureFromEventAsync`
(`:75-82`) → `ValidateConfigurationAsync` (`:84-91`) → `InitializeAsync` (`:93`) →
`AdapterBusFlowExecutor.ExecuteAsync` (`:95-107`) → on success `context.PublishAsync(new
CompletionRequest(progressContext, Success: true))` (`:115-118`) then `BuildSuccessResult` (`:120-122`);
`OperationCanceledException` is **never** published as failure (NACK/requeue path, `:125-140`); shutdown
is best-effort in `finally` (`:167-171`, `ShutdownSafelyAsync` `:301-314`).

---

## 2. Resume

**Canonical pattern.** Implements `IResumableAdapter`. `CanResumeFrom(AdapterCheckpoint)` →
`CheckpointAdapter.HasData(...)` guard, then `<Vendor>CheckpointHelper.CanResumeFrom(CheckpointAdapter.GetData(checkpoint),
_logger)`. `ResumeAsync(...)` reads `data["flow"]`, routes via `RecoveryParsingHelper.IsAssetsFlow /
IsFindingsFlow`, and delegates to a per-vendor `<Vendor>ResumeRunner` that internally drives the Shared
`CollectorResumeRunner` + `CollectorResumeDefinition<TState>`. The Shared runner loads state, creates a
progress context, restores progress from the checkpoint, runs the selected flow under the resilience
strategy, and builds a consistent `AdapterResult`. **The collector keeps flow-routing and prerequisites at
its boundary; the Shared runner executes one already-selected flow** (documented in
`CollectorResumeRunner.cs:7-21`).

**Citations (≥2):**
- Falcon: `FalconCollector.cs:385-464` (`CanResumeFrom` `:385-395`; `ResumeAsync` flow-routing `:401-431`;
  delegates to `_resumeRunner.ResumeAssetsAsync/ResumeFindingsAsync` `:438-464`).
- Tenable: `TenableIoCollector.cs:506-605`.
- Qualys: `QualysCollector.cs:234-293`.
- Defender: `DefenderVmCollector.cs:231-303` (note: Defender re-derives configuration and re-initializes
  the session inside `ResumeAsync` if `_sessionSpec == null`, `:261-274` — a resume-on-cold-adapter
  variation; the others assume init already ran).
- CortexXdr: `CortexXdrCollector.cs:284-367`.
- Shared: `CollectorResumeRunner.cs:22-118`, `CollectorResumeDefinition<TState>`
  (`CollectorResumeDefinition.cs:8-32`).

**`CollectorResumeDefinition<TState>` required members** (`CollectorResumeDefinition.cs:11-23`):
`VendorName`, `FlowName`, `TryLoadState : IReadOnlyDictionary<string,string> → (bool Ok, TState? State)`,
`LogResume`, `RunAsync : (TState, AdapterProgressContext, CancellationToken) → Task<int>`,
`BuildResultData`, `SuccessMessagePrefix`. Optional: `ClassifyFlowException`, `BuildFlowRetryPipeline`,
`ResilienceStrategy`, `RecoverAsync` (`:25-31`). When `ResilienceStrategy is null` the runner uses
`CollectorResumeLegacyExecutor`; otherwise `CollectorResumeStrategyExecutor`
(`CollectorResumeRunner.cs:115-117`) — all native collectors pass a strategy, so the strategy executor is
the live path.

**What `TState` looks like.** A per-vendor checkpoint-state record (see §8). It is *loaded from the
checkpoint dictionary* by `TryLoadState`, which the collector wires to `<Vendor>CheckpointHelper`. The
flow's `RunAsync` re-enters the same flow method (e.g. `CollectFindingsInternalAsync(..., resumeState:
state)`), so resume re-uses the exact forward-collection code path (Falcon `FalconCollector.cs:438-445`;
Qualys `QualysCollector.cs:269-275`; CortexXdr `CortexXdrCollector.cs:345`).

---

## 3. Session / transport

**Canonical pattern.** All session creation goes through the Shared `DefaultSessionProvider` (resolved in
the ctor) driven by the Shared `AdapterSessionLifecycle` static helper. `InitializeAsync` short-circuits if
`_sessionSpec == null` (the host probes adapters before config is applied), otherwise calls
`AdapterSessionLifecycle.InitializeAsync(_sessionProvider, _sessionSpec, _logger, ref _sessionHandle, ref
_session, ref _isInitialized, ct, afterCreateAsync: ..., correlationId: _currentPlatformEvent?.CorrelationId)`.
`ShutdownAsync`/`Dispose`/`DisposeAsync` delegate to the matching `AdapterSessionLifecycle` methods. Auth is
**not** hand-built per request: the configuration builder produces a `SessionSpec` whose `Auth` is an
`AuthSelection.*` value, and the http.package session applies it.

**Citations (≥2):**
- Falcon `InitializeAsync`: `FalconCollector.cs:306-334` (with an `afterCreateAsync` that builds a
  `FalconAccessProber` and probes Hosts access for fail-fast).
- Tenable `InitializeAsync`: `TenableIoCollector.cs:398-420` (`afterCreateAsync` builds an
  `AdapterHttpClient(sess, _logger)` → `_adapterHttp`).
- Qualys: `QualysCollector.cs:131-153` (same `AdapterHttpClient` pattern).
- Defender/CortexXdr use a no-op `afterCreateAsync` (`DefenderVmCollector.cs:202-216`,
  `CortexXdrCollector.cs:245-267`) and consume `_session` directly.
- Shared: `AdapterSessionLifecycle.cs:13-112`, `DefaultSessionProvider.cs:11-98`, `SessionSpec.cs:35-80`.

**Two transport-access shapes (variation, not contract divergence):**
- **`AdapterHttpClient` wrapper** — Tenable (`TenableIoCollector.cs:42,416`), Qualys
  (`QualysCollector.cs:44,149`) hold `AdapterHttpClient? _adapterHttp` built in `afterCreateAsync`.
- **Raw `IHttpSession`** — Falcon, Defender, CortexXdr pass `_session!` straight into the flow runner
  (Defender `DefenderVmCollector.cs:320`, CortexXdr `CortexXdrCollector.cs:115`).
Both are valid; both ultimately ride the same http.package `IHttpSession`.

**Auth building — canonical.** The per-vendor `<Vendor>CollectorConfigurationBuilder.Build(...)` returns
`(config, SessionSpec)`, and the `SessionSpec.Auth` is set to an `AuthSelection.*`:
- Falcon `BuildSessionSpec` (`FalconCollectorConfigurationBuilder.cs:279-304`): `Auth = new
  AuthSelection.OAuth2(new OAuth2AuthenticationConfiguration { ClientId, ClientSecret, TokenEndpoint })`,
  and the *same* `SessionSpec` carries `Timeout`, `Retry`, `RateLimiter`, `CircuitBreaker` from the typed
  config (`:291-294`).
- Defender (`DefenderVmCollectorConfigurationBuilder.cs:85-89`): `Auth = new AuthSelection.OAuth2(new
  OAuth2AuthenticationConfiguration{...})`.
`SessionSpec` (`SessionSpec.cs:35-80`) is "a minimal spec for building a configured HttpClient +
IHttpSession" exposing `Name`, `BaseAddress`, `Auth` (required), plus optional `Timeout`, `Retry`,
`RateLimiter`, `CircuitBreaker`, `ConfigureClient`, `Security`, `Telemetry`. **Secret *values* live in the
`AuthSelection`, sourced from dispatch credentials (§7); the spec carries only non-secret shape +
policy knobs.**

---

## 4. Egress + page numbering

**Canonical pattern.** Pages are published through the Shared static `CollectorNdjsonPublisher`:
`PublishAssetsPageAsync` / `PublishFindingsPageAsync` (string records) or the `…Utf8PageAsync` variants
(`ReadOnlyMemory<byte>`), each taking an explicit `int pageNumber`. The publisher owns the **mandatory
target-naming scheme** — `BuildMandatoryTargetPath(outputName, pageNumber)` only accepts the two file
names `findings`/`assets` and produces `"{name}_{page:D6}.json"` via `CollectorOutputDefaults.BuildPageTargetPath`,
which **requires `page >= 1`**. Upload-strategy selection (single vs multipart) is delegated to
`ResultsBatchPublisher`.

**Citations (≥2):**
- Shared publisher entry points: `CollectorNdjsonPublisher.cs:13-119`; mandatory naming guard
  `:121-137`; page-name format `CollectorOutputDefaults.cs:16-30` (`{name}_{page:D6}{DocumentSuffix}`,
  throws if `page <= 0`).
- Falcon assets publish: `FalconAssetsScrollRunner.cs:269` passes `pageNumber: counters.Page` to
  `PublishAssetsUtf8PageAsync`.
- Qualys findings publish: `QualysFindingsFlow.cs:113-114` (`int pageNumber = ++nextPageNumber;` →
  `_batchPublisher.PublishAsync(progressContext, readyBatch, pageNumber, ...)`).
- Defender assets publish: `DefenderVmAssetsFlow.cs:78-82` (passes `currentPage`).

**Where the page NUMBER comes from — the contract.** It is a **monotonic, per-stream, 1-indexed counter
persisted in the collector's checkpoint state** and re-seeded on resume. The field name is per-vendor:

| Collector / stream | Page-counter field in checkpoint state | Resume re-seed | Increment / pass-to-publisher |
|---|---|---|---|
| Falcon (base) | `Page` (`FalconCheckpointState.cs:17`, "1-indexed") | `counters.Page = resumeState.Page` (`FalconAssetsScrollRunner.cs:189`) | `counters.Page++` (`:100`) → `pageNumber: counters.Page` (`:269`) |
| Falcon findings assets-stage | `AssetsStagePage` (`FalconCheckpointState.cs:125`) | — | continues assets_*.json numbering |
| Qualys findings | `PublishedPageCount` (`QualysFindingsCheckpointState.cs:7`) | `nextPageNumber = resumeState?.PublishedPageCount ?? 0` (`QualysFindingsFlow.cs:79`) | `++nextPageNumber` (`:113`) |
| Defender assets | `Page` (`DefenderVmCheckpointState.cs:7`) | `pageNumber = resumeState?.Page ?? 0` (`DefenderVmAssetsFlow.cs:44`) | `currentPage = pageNumber + 1` → publish `currentPage` (`:77-82`) |
| Defender findings | `Page` (global), `AssetsPage`, `FindingsPage` (`DefenderVmFindingsCheckpointState.cs:37,39`) | `globalPage/assetsPage/findingsPage = resumeState?.{Page/AssetsPage/FindingsPage} ?? 0` (`DefenderVmFindingsFlow.cs:55-57`) | per-lane `outputPage = …+1` |
| Tenable findings phase-transition | `LastPublishedPage` (`TenableIoCollector.cs:359`) | — | snapshot only (`AdvancePage(0,0)`) |

So the field name varies (`Page` / `PublishedPageCount` / `AssetsPage` / `FindingsPage` /
`LastPublishedPage`) but the **invariant is the same**: a monotonic per-stream counter held in the
versioned checkpoint state, seeded from `resumeState` on resume, incremented to the next value, and passed
explicitly as `pageNumber`.

**`MaxBytesPerBatch` / `ThrottlingOptions` — caller slices, egress writes.** The collector's flow runner
accumulates records into a page-sized batch and decides when to flush (it "slices"); the Shared
publisher writes the already-formed page. The egress NDJSON sessions
(`DataPipeline/Egress/Ndjson/NdjsonBatchSession.cs`, `NdjsonUtf8BatchSession.cs`) and
`ThrottlingOptions` / `ThrottlingAdapterExecutionContext` (`DataPipeline/Egress/ThrottlingOptions.cs`,
`ThrottlingAdapterExecutionContext.cs`) apply byte/throughput limits at write time, but the page boundary
(and therefore the page number) is the caller's decision. Evidence: every native publish call passes a
caller-owned counter and a caller-built batch (`QualysFindingsFlow.cs:113-114`,
`DefenderVmAssetsFlow.cs:78-82`, `FalconAssetsScrollRunner.cs:269`); the publisher takes the page as a
parameter and never derives it. *(The exact bytes-per-batch / multipart threshold values live in
`BufferingOptions.cs` / `MultipartUploadOptions.cs`; not enumerated here as they are not part of the
caller-facing contract.)*

---

## 5. Resilience

**Canonical pattern.** Each collector supplies a per-vendor `ResilienceStrategy =
<Vendor>ResilienceStrategyFactory.Create(...)` into the bus definition (and the same factory into every
resume call). The factory builds an `AdapterResilienceStrategy` — an **ordered chain of
`IAdapterFailurePolicy`** evaluated first-match-wins (`AdapterResilienceStrategy.DecideAsync`,
`AdapterResilienceStrategy.cs:51-65`). The Shared `AdapterResilienceStrategy.CreateDefault(...)` defines
the **fixed policy order** and lets vendors inject only `vendorPolicies` / a `mappedFailurePolicy` /
backoff plans into the middle — they cannot reorder the chain.

**Citations (≥2):**
- Falcon: `ResilienceStrategy = FalconResilienceStrategyFactory.Create(_logger)`
  (`FalconCollector.cs:284`); same factory reused on resume (`:444,462`).
- Tenable: `ResilienceStrategy = ResilienceFactory.Create()` (`TenableIoCollector.cs:488`; alias to
  `TenableIoResilienceStrategyFactory` `:25`).
- Qualys `QualysCollector.cs:226`; Defender `DefenderVmCollector.cs:186`; CortexXdr
  `CortexXdrCollector.cs:237`.
- Shared chain definition: `AdapterResilienceStrategy.CreateDefault` (`AdapterResilienceStrategy.cs:20-49`).

**Fixed failure-policy order** (`AdapterResilienceStrategy.cs:26-47`): `UserCancellationPolicy` →
`ProgrammerBugPolicy.Definitive` → `ServerSuggestedRetryDelayPolicy` → `RetryableTransportFailurePolicy`
→ *[vendor policies]* → *[mapped failure policy]* → `ProgrammerBugPolicy.Ambiguous` →
`UnknownFlowFailurePolicy` → `FallbackFailurePolicy`. Vendors parameterize values/selectors only.

> **Note on the two distinct "resilience" layers.** The CollectorBase project doc references a fixed
> *transport*-pipeline order `Retry → RateLimiter → Timeout → CircuitBreaker`. That is the **http.package
> session** policy set, surfaced on `SessionSpec` as the optional `Retry`/`RateLimiter`/`Timeout`/
> `CircuitBreaker` options (`SessionSpec.cs:46-61`) and populated from the typed config in the
> configuration builder (Falcon `FalconCollectorConfigurationBuilder.cs:291-294`). It is **separate** from
> the *flow*-level `AdapterResilienceStrategy` failure-policy chain documented above. Native collectors
> consume both: the session pipeline for per-request transport resilience, the failure-policy chain for
> per-flow failure classification + recovery decisions.

**Failure classification.** Each collector supplies a `FlowExceptionClassifier =
<Vendor>FlowExceptionClassifier.TryClassify` (Falcon `FalconCollector.cs:282`; Tenable
`TenableIoCollector.cs:487`; Qualys `QualysCollector.cs:225`; Defender `DefenderVmCollector.cs:185`;
CortexXdr `CortexXdrCollector.cs:236`). Unhandled (unclassified) exceptions fall to the Shared
`AdapterFlowFailureHandling.ClassifyUnhandledException` (`AdapterBusEntrypointRunner.cs:225`).

**Recovery budget — fixed gate logic, values only.** The strategy executor loads the budget from
progress-context state, runs the flow, clears the budget on full success, and on failure runs the failure
decision + recovery handler (`AdapterBusStrategyFlowExecutor.cs:25-48,65-116`). The budget state is owned
by Shared `AdapterRecoveryBudget` with **fixed `_resilience.recovery.*` state keys** —
`ConsecutiveNoProgressCountKey`, `TotalDeferralCountKey`, `FirstBudgetedDeferAtUtcKey`,
`LastProgressCoordinateKey`, etc. (`AdapterRecoveryBudget.cs:14-21`) — and a fixed progress-coordinate
formula `"{page}:{items}:{findings}"` (`AdapterRecoveryBudget.cs:189-190`). Collectors do not implement
this; they only provide an optional `RecoveryHandler` (Falcon is the only one that does —
`FalconCollector.cs:285,445,463`).

**`FlowRetryPipelineBuilder`.** All five supply one; it builds a Polly `ResiliencePipeline` for in-process
flow retry. The default is the Shared `UnknownFlowRetryPolicy.CreatePipeline` (Tenable
`TenableIoCollector.cs:486`; Qualys `QualysCollector.cs:224`; Defender `DefenderVmCollector.cs:184`;
CortexXdr `CortexXdrCollector.cs:235`). **Variation:** Falcon overrides it with
`FalconFlowRetryPolicy.CreatePipeline` (`FalconCollector.cs:281`) specifically to *exclude* its
planned-yield exception from blind in-process retry so it reaches the resilience strategy as an unbudgeted
host-scheduled deferred wait (documented inline `:280-281`).

---

## 6. Events

**Canonical pattern.** Every collector implements `ICollectorEventSink` (Shared,
`Events/ICollectorEventSink.cs:9-14`) explicitly, forwarding both methods through the Shared
`AdapterInProcEventForwarder`:
```
void ICollectorEventSink.ReportBatchProduced(args)      => AdapterInProcEventForwarder.ReportBatchProduced(_logger, this, BatchProduced, args);
void ICollectorEventSink.ReportCheckpointAdvanced(args) => AdapterInProcEventForwarder.ReportCheckpointAdvanced(_logger, this, CheckpointAdvanced, args);
```
The collector also declares the 5 SDK in-proc events and routes Progress/Completed/Error through a
`CollectorInProcEventHub`.

**Citations (≥2):**
- Falcon: `ICollectorEventSink` impl `FalconCollector.cs:97-101`; 5 events `:64-70`; hub `:90-94`.
- Defender: impl `DefenderVmCollector.cs:75-79`; events `:48-52`; hub `:68-72`.
- Tenable: impl `TenableIoCollector.cs:77-81`; events `:53-57`.
- Qualys `QualysCollector.cs:77-81,50-54`; CortexXdr `CortexXdrCollector.cs:76-80,49-53`.
- Shared: `ICollectorEventSink.cs`, `AdapterInProcEventForwarder.cs:6-39`,
  `CollectorInProcEventHub.cs:9-76`, `BatchProducedEventArgs.cs:6-16`.

**The 5 SDK events** (declared on every collector, e.g. `FalconCollector.cs:64-70`):
`ProgressChanged (ProgressEventArgs)`, `OperationCompleted (CompletionEventArgs)`,
`ErrorOccurred (AdapterErrorEventArgs)`, `BatchProduced (BatchProducedEventArgs)`,
`CheckpointAdvanced (CheckpointAdvancedEventArgs)`.

**Contract caveat (important for the audit).** `ReportBatchProduced` / `ReportCheckpointAdvanced` and the
event forwarding are explicitly **best-effort, in-proc telemetry only** and "must never change the
orchestrator publishing semantics (StreamBatchRequest / CompletionRequest / ErrorRequest)"
(`ICollectorEventSink.cs:5-7`); the forwarder swallows handler exceptions (`AdapterInProcEventForwarder.cs:18-21,31-36`).
Falcon documents that `BatchProduced`/`CheckpointAdvanced` are "no longer raised — the orchestrator handles
progress/completion atomically" (`FalconCollector.cs:68-70`). **The real data path is the orchestrator's
`PublishAsync(StreamBatchRequest / CompletionRequest)`, not these events.** `CollectorInProcEventHub`
wraps `AdapterInProcEventRaiser` for Progress/Completed/Error (`CollectorInProcEventHub.cs:28-75`).

---

## 7. Credential / RUN-envelope ingestion

**Canonical pattern.** The `BuildConfiguration` delegate (per-vendor `ConfigurationMapper`) hydrates a flat
`Dictionary<string,string>` from the RUN envelope via the Shared
`RunPayloadCredentialHydrator.Hydrate(platformEvent.Payload.GetRawText(), config, endpointConfigKey:
...)`, then merges `platformEvent.Credentials`. Secret **values always come from dispatch** (the RUN
`action.credentials` blob — encrypted blob stored as `_encryptedCredentials` for the builder to decrypt via
`IEncryptionService`, or plain JSON expanded into the dict). The per-vendor `ConfigurationBuilder` then
decrypts/merges and emits the typed config + `SessionSpec` (auth). **Secrets are never read from static
config/YAML.**

**Citations (≥2):**
- Shared hydrator:
  `Shared/Events/CollectorEnvelopes/Logic/RunPayloadCredentialHydrator.cs:10,23` (reads
  `payload.action.credentials` + endpoint; encrypted → `config["_encryptedCredentials"]`; plain JSON →
  expanded).
- Falcon mapper: `FalconCollectorConfigurationMapper.cs:20`
  (`RunPayloadCredentialHydrator.Hydrate(..., endpointConfigKey: "apiEndpoint")`) + merge of
  `platformEvent.Credentials` (`:23-38`); builder decrypts `_encryptedCredentials` and resolves
  clientId/clientSecret from the merged dict, dispatch wins
  (`FalconCollectorConfigurationBuilder.cs:65-122`).
- Defender mapper: `DefenderVmCollectorConfigurationMapper.cs:17`
  (`endpointConfigKey: "baseUrl"`).
- Qualys builder decrypt/merge: `QualysCollectorConfigurationBuilder.cs:106-137`.
- CortexXdr / Tenable mappers also call the hydrator (same convention).

**Incremental floor (`baseDateUtc` / `lastRanAt`) — canonical path.** The RUN envelope field is
`payload.action.lastRanAt : DateTimeOffset?` (`Shared/Events/CollectorEnvelopes/Models/Run/CollectorRunAction.cs:22-23`).
The per-vendor `*PlatformEventRequestParser`:
1. first tries metadata carriers `GetAnyNullable(metadata, "baseDateUtc","baseDate","since","from","lastRanAt")`
   (Falcon `FalconPlatformEventRequestParser.cs:24`; Qualys `QualysPlatformEventRequestParser.cs:21-23`;
   Defender `DefenderVmPlatformEventRequestParser.cs:24-26`),
2. then, if still `DateTime.MinValue`, reads `action.LastRanAt.Value.UtcDateTime` from the payload
   (Falcon `:84-86`; Qualys `:63-66`; Defender `:80-82`),
3. then normalizes with `AdapterTimeDefaults.ResolveBaseDateUtcOrDefault(baseDateUtc)` — `MinValue` →
   `now − DefaultLookback` (`Shared/Time/AdapterTimeDefaults.cs:16-26`; Qualys applies it at parser exit
   `:38`; Falcon applies it in `FalconFlowRunPreparer.cs:24,50`; Tenable in
   `TenableIoCollector.cs:116,235`).
The value rides the `<Vendor>CollectorTriggerRequest.BaseDateUtc` (e.g.
`FalconCollectorTriggerRequest.cs:3-7`) into `CollectAssetsInternalAsync` /
`CollectFindingsInternalAsync` (Falcon `FalconCollector.cs:242-251`), and is persisted into the checkpoint
state as `BaseDateUtc` (`FalconCheckpointState.cs:58`) so resume re-applies the same floor.

---

## 8. Checkpoint contract

**Canonical pattern.** State is a per-vendor record (`<Vendor>CheckpointState` / per-flow subtypes) carrying
at minimum: `Flow`, the monotonic page counter (§4), per-vendor cursor/watermark/offset fields,
running totals, `CheckpointCreatedUtc` (drives the staleness gate), `BaseDateUtc`, and `IsDryRun`. It is
serialized to the `AdapterCheckpoint` string→string dictionary via `<Vendor>CheckpointHelper.Save*State`
and persisted onto the progress context with `progressContext.SetState(key, value)`. **The ordering
invariant: SetState (persist checkpoint state, including the page number) happens BEFORE
`progressContext.AdvancePage(...)`** — confirmed across all collectors. A defer/phase-transition snapshot
persists state with `AdvancePage(0, 0)` so it does **not** inflate page/item counts.

**Citations (≥2):**
- Falcon state record: `FalconCheckpointState.cs:7-85` (base) + `FalconAssetsCheckpointState` /
  `FalconFindingsCheckpointState` `:91-252`; resume staleness gate
  `FalconCheckpointResumePolicy.cs:36-39,59-62` (~23h `RecoveryParsingHelper.DefaultStaleThreshold`).
- Ordering — Falcon: `SetCursor` (`FalconAssetsCheckpointWriter.cs:52`) and state apply (`:63`) precede
  `progressContext.AdvancePage(...)` (`:67`).
- Ordering — Qualys: `SetState` loop (`QualysFindingsCheckpointWriter.cs:46-48`, state built with
  `PublishedPageCount = pageNumber` `:36`) precedes `AdvancePage(...)` (`:51`).
- Ordering — Defender: `SetState` loop (`DefenderVmAssetsFlow.cs:176-179`, state with `Page = pageNumber`)
  precedes `AdvancePage(...)` (`:90`).
- Tenable phase-transition snapshot: builds `TenableIoFindingsCheckpointState`, `SetState`s it, then
  `progressContext.AdvancePage(itemsInBatch: 0, findingsInBatch: 0)` — explicit no-op advance
  (`TenableIoCollector.cs:349-377`, esp. `:371-376`).

**`CanResumeFrom` guard chain.** `CheckpointAdapter.HasData(checkpoint)` → `CheckpointAdapter.GetData` →
`<Vendor>CheckpointHelper.CanResumeFrom(data, logger)`, which loads + validates the state and applies the
staleness gate (Falcon `FalconCollector.cs:385-395` → `FalconCheckpointHelper.cs:23-24` →
`FalconCheckpointResumePolicy.cs:20-62`; Tenable `TenableIoCollector.cs:506-516`; CortexXdr
`CortexXdrCollector.cs:284-293`). A stale or unparseable checkpoint returns `false` → the host starts a
fresh run rather than resuming wrong.

---

## Appendix — at-a-glance per-collector wiring

| Subsystem | Falcon | Tenable | Qualys | Defender | CortexXdr |
|---|---|---|---|---|---|
| Interfaces | Assets+Findings+Sink+Resumable | Assets+Findings(combined)+Sink+Resumable | **Findings only**+Sink+Resumable | Assets+Findings+Sink+Resumable | Assets+Findings+Sink+Resumable |
| Bus entry | `FalconCollector.cs:229-290` | `TenableIoCollector.cs:450-493` | `QualysCollector.cs:175-232` | `DefenderVmCollector.cs:147-192` | `CortexXdrCollector.cs:190-243` |
| Resume runner | `FalconResumeRunner` | `TenableIoResumeRunner` | `QualysResumeRunner` | `DefenderVmResumeRunner` (re-inits on cold adapter) | `CortexXdrResumeRunner` |
| Transport access | raw `IHttpSession` + AccessProber | `AdapterHttpClient` | `AdapterHttpClient` | raw `IHttpSession` | raw `IHttpSession` |
| Page-counter field | `Page` / `AssetsStagePage` | `LastPublishedPage` | `PublishedPageCount` | `Page`/`AssetsPage`/`FindingsPage` | per-flow state |
| `FlowRetryPipelineBuilder` | **`FalconFlowRetryPolicy`** (custom) | `UnknownFlowRetryPolicy` | `UnknownFlowRetryPolicy` | `UnknownFlowRetryPolicy` | `UnknownFlowRetryPolicy` |
| Optional bus delegates | `PartialSuccessResultBuilder`, `RecoveryHandler`, `OnFlowNameResolved` | — | `OnFlowNameResolved` | — | — |

**Bottom line for the audit.** The native contract is: *one* `DelegateCollectorBusEntrypointSource` →
`AdapterBusEntrypointRunner.RunAsync`; resume via `IResumableAdapter` → `CollectorResumeRunner` +
`CollectorResumeDefinition<TState>`; sessions via `DefaultSessionProvider` + `AdapterSessionLifecycle`
with `SessionSpec.Auth = AuthSelection.*`; egress via `CollectorNdjsonPublisher.*PageAsync` with a
caller-owned monotonic 1-indexed per-stream page counter held in checkpoint state; resilience via a
per-vendor `AdapterResilienceStrategy` whose policy order is fixed by `CreateDefault` plus the separate
http.package session pipeline; events are best-effort in-proc only (the orchestrator's `PublishAsync` is
the real path); credentials/incremental-floor come from the dispatch RUN envelope
(`RunPayloadCredentialHydrator` + `action.lastRanAt`), never from static config; and checkpoint state is
always persisted (`SetState`) BEFORE `AdvancePage`, with a staleness-gated `CanResumeFrom`.

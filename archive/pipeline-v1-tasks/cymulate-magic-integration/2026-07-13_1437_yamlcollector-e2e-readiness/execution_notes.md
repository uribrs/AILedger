# Execution Notes

## W4 — Engine drift + repo snapshots (main thread, complete)

### Repo snapshots (assessment baseline)
| Repo | Branch | HEAD |
|---|---|---|
| cymulate-magic-integration | master | 1d1c5c2 (2026-07-12, PR #36 qualys test-connection fix) |
| IntegrationServiceBus | dev | 6a9fcc0 (2026-07-12, PR #660 fix/progress-metadata-passthrough) |
| cymulate-integration-adapters | dev | 86b2029 (2026-07-13, PR #278 task/yaml_auth_fixes) |

### Engine drift (ADR-0003)
Compared `src/Platform/Platform.Integrations.Sdk` (magic, canonical) vs
`src/Cymulate.Integration.Adapters/Collectors/YamlCollector/Cymulate.Integration.Yaml.Engine`
(adapters), namespace-normalized diff.

**Direction of drift: adapters copy is strictly AHEAD. The "canonical" home is stale.**

Modules that exist ONLY in the adapters copy:
- `Workflow/` (WorkflowRunner, WorkflowCheckpoint) + `Models/WorkflowConfig.cs` — multi-stage
  collection sessions: ordered stages, `capture` of outputs, `poll` until condition, `for_each`
  fan-out; explicitly built for async export→poll→download (i.e., the tenable.io flow) and
  list→enrich→join. Absent from the magic copy entirely.
- `Models/CursorRecoveryConfig.cs`, `Pagination/CursorRecoverySnapshot.cs`,
  `Pagination/CursorRecoveryTracker.cs` — cursor recovery for resumability.
- `Retry/DelayResolver.cs`, `Retry/EngineFailureClassifier.cs`, `Retry/RetryDelayExceededException.cs`.
- `Schemas/integration.schema.json` embedded in the engine project.

Only in magic copy: `ServiceCollectionExtensions.cs` (DI wiring — host-specific, fine).

Substantive line drift in shared files (namespace-normalized, `<`/`>` lines):
- `IntegrationEngine.cs` — 889 lines
- `Models/ErrorHandlingConfig.cs` — 203
- `Auth/CustomHeaderAuthenticator.cs` — 77
- `Retry/RetryPolicyFactory.cs` — 53
- `OperationResult.cs` — 38, `IExecutionSink.cs` — 32, `Templating/TemplateContext.cs` — 29,
  `Pagination/IPaginator.cs` — 27, `Models/SuccessConfig.cs` — 24, `IIntegrationEngine.cs` — 20,
  `Models/AuthenticationConfig.cs` — 19; long tail of smaller diffs across every module.

Implication: every shared file differs; ADR-0003's "any engine change must be applied to both
copies" is NOT being honored — the ISB-route engine has evolved (workflow orchestration, cursor
recovery, richer retry/error handling) without back-porting to Platform.Integrations.Sdk. The
Direct route and ISB route no longer execute the same engine semantics; YAMLs authored against
the adapters engine (e.g. anything using `workflow:` stages) will not load/run on the Direct route.

## W3 — Magic repo: tenable YAML + engine surface + ADRs (complete)

- `integrations/tenable.yaml`: auth `custom_headers` (X-ApiKeys from secret `api_keys`), base_url
  cloud.tenable.com. Ops: test_connection (GET /assets/export/status), create/status/download for
  BOTH assets and vulns exports. Multi-record retrieval is NOT pagination — every op is
  `strategy: none`; orchestration is a top-level `workflow:` block (tenable.yaml:217-263): stages
  with `capture` (uuid, chunks), `poll` (status==FINISHED, 5s interval, 1h timeout, max 720 polls),
  `for_each` chunk fan-out, stage `topic: assets|findings`. Chunk download maps a root-level JSON
  array (no records_path).
- **Direct-route engine (Platform.Integrations.Sdk) CANNOT run this**: `IntegrationDefinition` has
  no `workflow` property; loader `.IgnoreUnmatchedProperties()` silently drops it
  (Loader/YamlIntegrationLoader.cs:39); engine is single-op only (IIntegrationEngine.cs:24-45);
  template scopes lack `stages`/`item` → `{{stages.*.output.uuid}}`/`{{item.chunk}}` resolve to
  EMPTY STRING (TemplateEngine.cs:60-67); schema gate `additionalProperties:false` has no
  `workflow` key → validation (when enabled) REJECTS the file (schemas/integration.schema.json:7-31).
- ADR expectations: 0001 Magic-side router Direct-vs-ISB NOT BUILT (0001:86-96). 0002: YAML inline
  in dispatch payload; adapter = IIntegrationAdapter<ICollectorCapability> + IResumableAdapter;
  results via ISB native pipeline (PublishAsync→S3, events, AdapterCheckpoint). Open items incl.
  "drop the worker". 0003 Proposed: dual engine home, hand-synced, decision TBD; NOTE ADR-0003
  declares the ADAPTERS copy canonical per adapters#221 ("declared the canonical copy", 0003:9-20).
- **YAML delivery gap**: no code in magic repo ships YAML to ISB or builds a dispatch payload.
  Web.Api executes Direct route in-process only; returns definitions as JSON not YAML.
  `Cymulate.Integrations.Application.ServiceBus` is a hollow stub yet STILL wired in AppHost
  (Program.cs:101-108) with stale comments — contradicts ADR-0002 "drop the worker".

## W2 — Adapters repo: YamlCollector activation + collection (complete)

- Adapter: `YamlCollectorAdapter : IIntegrationAdapter<ICollectorCapability>, IResumableAdapter`
  (YamlCollectorAdapter.cs:22); AdapterId "yaml-collector", PlatformType.YamlEngine, topics
  assets+findings. Selection/DI lives HOST-side (ISB) — nothing in this repo maps a dispatch to it.
  Native `TenableIoCollector` also exists (PlatformType.TenableIo, vendor "Tenable.io").
- Routing seam in-adapter: `useYaml` payload flag; YAML-miss with useYaml → returns Skipped +
  `fallbackToNative=true` so the bus re-routes to native (:118,:29,:331).
- Activation: ProcessAsync → YamlPlatformEventRequestParser → YamlCollectorTriggerRequest/
  YamlCollectorPayload {Yaml inline, IntegrationName, UseYaml, Operation, TopicOperations, Inputs,
  Config, Resume, MaxInProcessWaitSeconds}. YAML resolution: inline wins, else S3
  `{prefix}/{name}.yaml` (name = IntegrationName or normalized vendor). Parse with
  validateAgainstSchema:true. Credentials: PlatformEvent.Credentials decrypted
  (IEncryptionService, "YamlCollector") overlaid over payload.Config; base_url override via
  Config["base_url"]. Checkpoint read only on ResumeAsync (AdapterCheckpoint.AdapterState),
  gated by CanResumeFrom (24h age, strategy allow-list incl. "workflow").
- Execution: per-call `new IntegrationEngine` + InjectDefinition. Workflow mode when
  definition.Workflow.Stages present → WorkflowRunner implements create→poll→download:
  capture json_path (export_uuid), poll until body_json_path equals / status-code set with
  max_polls/timeout, for_each chunk fan-out with {{item.chunk}}. E2E-proven
  (WorkflowExportVendorE2ETests — WireMock export/status/chunks, structurally identical to
  TenableIoUrls). Records streamed page-by-page via IsbExecutionSink.PublishBatchAsync →
  context.PublishAsync(DataBatchRequest) → ISB-native S3 (`findings_NNNNNN.json`). Checkpoints:
  cursor+state set before AdvancePage; workflow checkpoints per stage + per fan-out item;
  cursor-recovery watermark persisted. Errors mapped to typed YamlFlowException
  (401/403 INVALID_CREDENTIALS non-retryable; 408/429/5xx retryable VENDOR_HTTP_ERROR);
  oversized vendor delay → ServerSuggestedRetryDelayException (capped 6h) for host-scheduled
  resume; publish failure → PUBLISH_FAILED, never silent.
- Completion: AdapterResult.SuccessResult + Data {records,pages,total,findings,assets,...};
  DONE event by Shared AdapterBusEntrypointRunner on ProcessAsync path, by sink on Resume path;
  FailAsync deliberate no-op (host publishes error).
- Tenable specifics: NO tenable YAML and no tenable YamlCollector test in this repo — definition
  must arrive inline or from S3 {prefix}/tenable-io.yaml (artifact lives outside repo).
- LIMITATION (confirmed in code): `opResult.RetryAfter` mid-workflow throws → a 429/Retry-After
  during Tenable export poll/download = hard YAML_WORKFLOW_FAILED, no scheduled resume
  (WorkflowRunner.cs:243-244). Tenable rate-limits export endpoints → real risk.
- Stale docstrings claim mid-workflow resume unsupported; code + E2E prove it IS supported
  (checkpoint per stage/item, resume skips published chunks). Docs need reconciling.
- Snapshot: dev @ 86b2029. Engine copy net8.0, IntegrationEngine.cs 1578 lines, module list
  matches W4 drift findings.

## W1 — ISB ingress + dispatch (complete)

- Ingress: RMQ `RabbitMqConsumerService` (autoAck:false, content-based message-type detection at
  DispatchMessageAsync:1588 — trigger-flow detected by topic+vendor+payload.flows) and HTTP
  `POST /api/v1/events/publish[/sync]` + `/events/stop`. Run DTO `AdapterRunMessage`
  {topic, vendor, correlationId, timestamp, payload{instanceOid, action{product, credentials
  (encrypted), lastRanAt, flows[], filter, [JsonExtensionData] yaml/integrationName/inputs},
  metadata}} (+ flat variant).
- Routing: vendor→PlatformType via enum EnumMember ("Tenable.io"→TenableIo; YamlEngine=98).
  TriggerFlowMapper.ResolveDefinitionSource: NamedYaml if integrationName OR
  (Collectors && Adapters:UseYaml && vendor!=""); InlineYaml if yaml present → optimistic route
  to YamlEngine. Host-side TryPrepareYamlEngineDispatchAsync (ProcessEventCommandHandler.cs:392,
  gated Adapters:UseYaml default FALSE): fetches YAML from S3, inlines payload["yaml"], sets
  ProductType=YamlEngine; miss → revert to native via vendorPlatformResolver; loop guard
  YamlFallbackToNative marker blocks re-routing after adapter-side fallback. Synthesizes
  inputs.start_time/end_time (lastRanAt or 24mo back → now).
- Dispatch: AdapterActivator loads external adapter assemblies from AdapterLoaderOptions
  .DiscoveryPath (Assembly.LoadFrom), registry keyed [PlatformType,Category,Version,ClientId];
  decrypts credentials host-side → SetConfiguration → InitializeAsync → ProcessAsync(PlatformEvent).
  PlatformEvent {ProductType, Topic=first flow, CorrelationId, ClientId, Metadata(Vendor, flows,
  lastRanAt, filter, instanceOid), Payload(JsonElement incl. yaml, integrationName injected)}.
  RetryCount>0 → ResumeAsync with AdapterCheckpoint (UnwrapResumableAdapter).
- Host pipeline: IAdapterExecutionContext.PublishAsync → S3AdapterDataPublisher, path
  {clientID}/{integrationSettingId}/{instanceId}; events to collectors.done/.progress/.error/
  .data-uploaded/.partial-done; guaranteed completion via completionTracker + outbox relay;
  per-page checkpoint persistence (ownership-guarded); distributed exec lock + heartbeat +
  durable stop flag; PartialWaitRequired → ScheduledWait + ScheduleReconcilerService resume;
  RMQ delay-queue retry + DLQ.
- Snapshot: dev @ 6a9fcc0. Gaps: adapters external (runtime registration not verifiable in-repo);
  no committed Collectors-category queue config (env-supplied); collector-run seam has no
  connection-test flow.

## Seam verification (main thread)

- S3 key: ISB `S3YamlDefinitionFetcher` → `{env}/yaml-files/{name}.yaml`, name = integrationName
  or vendor.Trim().ToLowerInvariant().Replace(' ','-') → "Tenable.io" → "tenable.io" →
  `{env}/yaml-files/tenable.io.yaml`. Adapter fallback normalizer is IDENTICAL
  (YamlCollectorAdapter.ResolveDefinitionName). Magic repo file is `integrations/tenable.yaml`
  (vendor: Tenable, display_name: Tenable.io) → NAME MISMATCH unless uploaded under derived name
  or backend supplies integrationName.
- NO uploader anywhere: grep "yaml-files" across all three repos → only readers (ISB fetcher,
  adapter S3 loader, local-runner tool, tests). No pipeline publishes the 275 YAMLs to S3.
- Schema: adapters engine embedded schema HAS `workflow` key (integration.schema.json:32,457);
  magic repo schema does NOT → magic's own gate would reject its own tenable.yaml.
- Credentials: host decrypts for adapter config (AdapterActivator.cs:71-89) AND adapter decrypts
  PlatformEvent.Credentials itself (BuildCredentials) — two channels, consistent, not a conflict.

## User-prompted follow-up: is tenable.yaml runnable? (auth contract check)

- tenable.yaml auth uses `value_field: api_keys` → requires a single credential key literally
  named `api_keys` holding the pre-composed "accessKey=…; secretKey=…" string.
- Platform provisions Tenable creds as discrete accessKey/secretKey — proven by native collector:
  TenableIoCollector.cs:383 (MissingConfiguration ["baseUrl","accessKey","secretKey"]);
  TenableIoCollectorConfigurationBuilder.cs:51-52 (aliases access_key/secret_key), :102-111
  (composes X-ApiKeys itself).
- Adapters-engine CustomHeaderAuthenticator validates up front: absent value_field key →
  AuthenticationException "Custom header credentials are missing: api_keys" → INVALID_CREDENTIALS
  (non-retryable) BEFORE any HTTP call.
- Engine already supports the right shape: composed `value` template with {{key}} placeholders
  (CustomHeaderAuthenticator doc comment literally gives the Tenable example
  `X-ApiKeys: accessKey={{accessKey}};secretKey={{secretKey}}`); likely landed in adapters
  HEAD 86b2029 "task/yaml_auth_fixes". tenable.yaml was not migrated.
- NEW FINDING #2b (High, bug): tenable.yaml auth contract mismatch vs platform credential shape;
  fix = switch to composed `value`. Caveat: exact backend blob key names inferred from native
  collector contract (labeled inference, not observed live payload).

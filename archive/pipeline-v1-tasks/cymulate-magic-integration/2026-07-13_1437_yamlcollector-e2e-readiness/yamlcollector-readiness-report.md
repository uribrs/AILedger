# YamlCollector End-to-End Readiness Assessment — tenable.io trace

**Snapshot:** magic `master@1d1c5c2` (2026-07-12) · ISB `dev@6a9fcc0` (2026-07-12) · adapters `dev@86b2029` (2026-07-13). Read-only analysis; all claims cite source.

## Executive verdict

**The execution core (ISB dispatch → YamlCollectorAdapter → workflow engine → S3/events) is built, coherent, and E2E-tested — genuinely close to ready. What is NOT ready is everything around it:** no pipeline delivers the 275 YAMLs to the S3 location ISB reads from, the derived S3 name for a tenable run does not match the authored file (`tenable.yaml`), the YAML's auth block demands a credential key (`api_keys`) the platform doesn't provision (Finding #2b), the feature is dark by default (`Adapters:UseYaml=false`), the Magic-side router promised by ADR-0001 does not exist, and the two engine copies have drifted so far apart that the Direct route cannot execute `tenable.yaml` at all. A tenable.io run today would silently fall back to the native TenableIoCollector — collection still happens, but not through YAML; and even if the YAML were delivered, auth would fail before the first vendor call.

---

## Phase A — Backend sends a run message

The backend publishes a **trigger-flow JSON message** to an ISB RabbitMQ input queue (queues are config-driven, category `Collectors`; default metadata `SourceQueue="collectors.run"`), or POSTs it to `POST /api/v1/events/publish` (`EventsController.cs:72`; sync variant `:295`; cancel via `/events/stop` `:987`).

Shape — `AdapterRunMessage` (`Domain/.../Messaging/AdapterRunMessage.cs:11`):

```json
{
  "topic": "<collector topic>",
  "vendor": "Tenable.io",
  "correlationId": "<24-hex>",
  "timestamp": 1760000000000,
  "payload": {
    "instanceOid": "…",
    "action": {
      "product": "Tenable.io",
      "credentials": "<encrypted blob>",
      "lastRanAt": "…",
      "flows": ["assets", "findings"],
      "filter": {},
      "yaml | integrationName | inputs": "(optional, via JsonExtensionData)"
    },
    "metadata": { "clientId": "…" }
  }
}
```

*(The exact vendor string `"Tenable.io"` is inferred from ISB's `PlatformType` EnumMember — the backend repo that emits this message is outside the assessed scope.)*

A flat variant exists. Message type is detected **by payload content, not queue/routing-key** — `TriggerFlowMapper.IsTriggerFlowMessage` requires `topic` + `vendor` + `flows[]` (`Infrastructure.Core/Messaging/TriggerFlowMapper.cs:53`; dispatch order `RabbitMqConsumerService.cs:1588`).

## Phase B — What ISB does and what it hands to the adapter

1. **Consume** (`RabbitMqConsumerService`, manual-ack) → `ProcessEventCommand` → `ProcessEventCommandHandler`.
2. **Map** → `PlatformEvent` (`TriggerFlowMapper.TryMapToPlatformEvent:186`): `ProductType` (vendor→`PlatformType` enum: `"Tenable.io"`→`TenableIo`), `Topic` = first flow, `CorrelationId`, `ClientId`, `Metadata` (Vendor, flows, lastRanAt, filter, instanceOid), `Payload` (JsonElement incl. any `yaml`/`integrationName`/`inputs`).
3. **YAML routing decision** — two cooperating pieces:
   - Mapper: `DefinitionSource = NamedYaml` when `integrationName` present **or** (`category==Collectors` ∧ `Adapters:UseYaml` ∧ vendor non-empty); `InlineYaml` when `yaml` present → optimistically routes to `PlatformType.YamlEngine` (`RegisteredAdapterPlatformResolver.cs:43-86`).
   - Host: `TryPrepareYamlEngineDispatchAsync` (`ProcessEventCommandHandler.cs:392`) — **gated on `Adapters:UseYaml` (default `false`)**. Fetches the YAML from S3 `{env}/yaml-files/{name}.yaml` (`S3YamlDefinitionFetcher`), where `name` = `integrationName` or vendor normalized `lowercase, spaces→hyphens` → `"Tenable.io"` → **`tenable.io`**. Found → inlines `payload["yaml"]`, sets `ProductType=YamlEngine`, synthesizes `inputs.start_time/end_time` (lastRanAt or 24 months back → now). Miss → reverts to the native collector (logged, non-fatal). Loop guard: a run that already fell back carries `YamlFallbackToNative` and is never re-routed.
4. **Activate** — `AdapterActivator` (`AdapterActivator.cs:144`): adapters are **external assemblies** loaded from `AdapterLoaderOptions.DiscoveryPath` (`AdapterLoader.cs:62`), registry keyed `[PlatformType, Category, Version, ClientId]`. Host decrypts credentials → `SetConfiguration` → `InitializeAsync` → **`adapter.ProcessAsync(PlatformEvent)`** (`ProcessEventCommandHandler.cs:971`). On RMQ redelivery (`RetryCount>0`) it calls `ResumeAsync(platformEvent, AdapterCheckpoint)` instead (`:963-1002`).
5. **Host services during the run**: distributed execution lock + heartbeat + durable stop-flag polling (`:142,:862`), per-page checkpoint persistence (ownership-guarded, `AdapterExecutionContext.cs:97-145`), guaranteed completion via completion-tracker + outbox relay (`:641-667`), delay-queue retry + DLQ, `PartialWaitRequired` → `ScheduledWait` + `ScheduleReconcilerService` resume (`:780`).

**Answer to "ISB does something and passes the run message — what is it?"**: ISB maps the run message to a `PlatformEvent`, *inlines the vendor YAML into the payload host-side* (so the sandboxed adapter never touches S3 for it), rewrites `ProductType` to `YamlEngine`, and invokes the dynamically-loaded `YamlCollectorAdapter.ProcessAsync` in-process, holding the lock/heartbeat/checkpoint/event machinery around it.

## Phase C — YamlCollector before collection starts

`YamlCollectorAdapter : IIntegrationAdapter<ICollectorCapability>, IResumableAdapter` (`YamlCollectorAdapter.cs:22`), `AdapterId="yaml-collector"`, `PlatformType.YamlEngine`, topics `assets|findings`.

`ProcessAsync` (`:161`):
1. **Parse dispatch** → `YamlCollectorPayload` {`Yaml`, `IntegrationName`, `UseYaml`, `Operation`, `TopicOperations`, `Inputs`, `Config`, `Resume`, `MaxInProcessWaitSeconds`}; vendor from `Metadata["Vendor"]`, topic from `PlatformEvent.Topic`.
2. **Resolve YAML** (`YamlOperationRunner.cs:426`): inline `payload.Yaml` wins; else adapter-side S3 fallback `{prefix}/{name}.yaml` (same normalizer as ISB). Miss + `useYaml` → returns `Skipped` + `fallbackToNative=true` (bus re-dispatches to native TenableIoCollector); miss without `useYaml` → hard `YAML_NOT_FOUND`.
3. **Parse + validate** — `YamlIntegrationLoader.LoadFromString(validateAgainstSchema:true)` against the engine's **embedded schema, which includes `workflow`** (`Schemas/integration.schema.json:32,457`); failure → `INVALID_PAYLOAD`.
4. **Credentials** — decrypts `PlatformEvent.Credentials` (`IEncryptionService.DecryptConfiguration`), overlays non-secret `payload.Config` (credentials win); `base_url` override rides in `Config["base_url"]`.
5. **Checkpoint** — fresh `ProcessAsync` always starts clean (`resumeState:null`, `:189`). Resume happens only via `ResumeAsync` (`:215`) reading `AdapterCheckpoint.AdapterState`, gated by `CanResumeFrom` (`:262-286`): version match, ≤24 h old, strategy in allow-list `cursor|link_header|offset|page_number|workflow`.
6. Fresh **per-call `IntegrationEngine`** + `InjectDefinition` (`YamlOperationRunner.cs:106-109`) — no shared state, matching the Direct route's convention.

## Phase D — The tenable.io collection itself

`tenable.yaml` (magic repo) defines auth `custom_headers` (`X-ApiKeys` from secret `api_keys`), ops `test_connection`, `create_assets_export`, `get_assets_export_status`, `download_assets_chunk` (+ the vulns triple), **no pagination anywhere** — orchestration is the top-level `workflow:` block (`tenable.yaml:217-263`).

Execution (`YamlOperationRunner.ExecuteFlowAsync:69` → `WorkflowRunner`, `Engine/Workflow/WorkflowRunner.cs:37`):

1. **Stage `create_*_export`** — `POST /vulns/export` (or `/assets/export`), `capture` grabs `export_uuid` via json_path into `{{stages.<name>.output.uuid}}`.
2. **Stage `get_*_export_status`** — `GET …/{{uuid}}/status`, `poll.until body_json_path: status == FINISHED`, `interval_ms:5000`, `timeout_ms:3600000`, `max_polls:720`; captures `chunks_available` → `{{stages.<name>.output.chunks}}`.
3. **Stage `download_*_chunk`** — `for_each` over the chunk array, `{{item.chunk}}` templated into the path; response is a root-level JSON array mapped to normalized asset/finding fields; each stage tagged `topic: assets|findings` so records land in the right flow.
4. **Records → host**: streamed page-by-page through `IsbExecutionSink.PublishBatchAsync` → `context.PublishAsync(DataBatchRequest)` → ISB uploads `findings_NNNNNN.json` / `assets_NNNNNN.json` to S3 at `{clientId}/{integrationSettingId}/{instanceId}/` and emits data-uploaded + progress events per batch. Large stages stream via NDJSON temp file.
5. **Checkpoints**: cursor+state written **before** page advance (snapshot always points at the next unit); workflow checkpoints after every stage and every fan-out chunk; resume skips completed stages and already-published chunks — proven by `WorkflowExportVendorE2ETests.MidWorkflowCrash_ResumesPastPublishedChunk_NoRefetch`.
6. **Errors**: engine result → typed `YamlFlowException` (`YamlOperationRunner.cs:399-416`): 401/403 `INVALID_CREDENTIALS` (non-retryable), 408/429/5xx `VENDOR_HTTP_ERROR` (retryable), other 4xx `VENDOR_REJECTED`; publish failure `PUBLISH_FAILED` (never silent). Per-op YAML retry: exponential backoff, `max_retries:5`, honors `Retry-After`.
7. **Completion**: `AdapterResult.SuccessResult` + `{records, pages, total, findings, assets…}`; DONE event published by the shared `AdapterBusEntrypointRunner` (ProcessAsync path) or by the sink (Resume path); failures → host publishes `AdapterErrorEvent` + failure completion; checkpoint flushed + deleted on success.

The E2E suite exercises exactly this shape (WireMock `POST /exports` → status `RUNNING→FINISHED` → chunk downloads), structurally identical to the native `TenableIoUrls` (`/vulns/export` → `…/status` → `…/chunks/{id}`), plus 429, cursor-resume, and OAuth2 variants. **No tenable-named YAML or test exists in the adapters repo** — the definition must arrive inline (ISB) or from S3.

---

## Findings (severity-ranked)

| # | Sev | Type | Finding | Evidence |
|---|-----|------|---------|----------|
| 1 | **High** | Gap | **No YAML delivery pipeline.** Nothing publishes the 275 magic-repo YAMLs to S3 `{env}/yaml-files/` — all three repos contain only readers. Without a manual upload, every YAML run silently falls back to native. | grep `yaml-files` across repos → ISB fetcher, adapter loader, tests only |
| 2 | **High** | Gap/Bug | **Definition-name mismatch for tenable.** Both ISB and the adapter derive the S3 name from the run's vendor string via `lowercase, spaces→hyphens` (dots preserved). The vendor string `"Tenable.io"` is an *inference from ISB's `PlatformType` EnumMember* (the backend repo is out of scope) — but the finding is robust either way: `"Tenable.io"`→`tenable.io.yaml` and `"Tenable"`→`tenable.yaml` both fail, because no uploader exists (Finding #1) and the authored file is `tenable.yaml`. The fallback is **silent** (logged INFO). | `ProcessEventCommandHandler.cs:439-443`; `PlatformType.cs:58`; `YamlCollectorAdapter.ResolveDefinitionName`; `integrations/tenable.yaml:2` |
| 2b | **High** | Bug | **tenable.yaml auth contract mismatch — not runnable against platform-provisioned credentials.** The YAML declares `value_field: api_keys`, requiring a single credential literally named `api_keys` pre-composed as `accessKey=…; secretKey=…`. The platform provisions Tenable credentials as discrete `accessKey`/`secretKey` (native collector contract: `TenableIoCollector.cs:383`, builder aliases `access_key`/`secret_key` and composes `X-ApiKeys` itself). The engine's `CustomHeaderAuthenticator` validates up front → `AuthenticationException "Custom header credentials are missing: api_keys"` → `INVALID_CREDENTIALS` (non-retryable) before any HTTP call. The engine already supports the correct shape — a composed `value` template (`X-ApiKeys: accessKey={{accessKey}}; secretKey={{secretKey}}`, its doc comment's own example, landed with adapters `task/yaml_auth_fixes`) — but `tenable.yaml` was never migrated. *Caveat: exact backend blob key names inferred from the native collector's contract.* | `integrations/tenable.yaml:11-24`; adapters `Auth/CustomHeaderAuthenticator.cs` (missing-key validation + composed-value support); `TenableIoCollectorConfigurationBuilder.cs:51-111` |
| 3 | **High** | Drift | **Engine copies have materially diverged; Direct route cannot run tenable.yaml.** Adapters copy has `Workflow/`, cursor recovery, failure classifier, workflow-aware schema; magic copy has none (`IntegrationEngine.cs` differs ~889 normalized lines). Magic loader silently drops `workflow:` (`IgnoreUnmatchedProperties`), `{{stages.*}}`/`{{item.*}}` resolve to empty string, and magic's own schema gate would reject the file. ADR-0003's "apply changes to both copies" is not being honored. | W4 diff; `Loader/YamlIntegrationLoader.cs:39`; `TemplateEngine.cs:60-67`; `schemas/integration.schema.json:7-31` |
| 4 | **High** | Gap | **Magic-side router (ADR-0001) not built.** Nothing in the magic repo selects Direct vs ISB or ships YAML anywhere; today the "ISB route" is triggered entirely by backend→ISB + the `Adapters:UseYaml` flag. "Magic is the orchestrator" is direction, not code. | W3 §4; ADR-0001:86-96 |
| 5 | **Medium-High** | Limitation | **Vendor `Retry-After` beyond the in-process cap mid-workflow is a hard failure**, not a host-scheduled resume: `WorkflowRunner` throws on `opResult.RetryAfter` (`:243-244`) → `YAML_WORKFLOW_FAILED`. Plain-op runs get `ServerSuggestedRetryDelayException` → `PartialWait`; workflow runs don't. Tenable rate-limits its export endpoints, so this is a real tenable failure mode (generic RMQ retry + checkpoint can still recover, but the vendor-suggested delay is not honored). | `WorkflowRunner.cs:243-244`; contrast `YamlOperationRunner.cs:376-385` |
| 6 | **Medium** | Config | **Feature dark by default**: `Adapters:UseYaml` defaults `false` in code and is entirely absent from committed config (so nothing overrides the default); no committed config binds a `Collectors`-category input queue (env-supplied). Production rollout state is not verifiable from code. | `ProcessEventCommandHandler.cs:59`; `UseYaml` absent from ISB `appsettings.json` |
| 7 | **Medium** | Debt | **ADR-0002 cleanup incomplete in magic repo**: hollow `Application.ServiceBus` scaffold still exists and is still wired in AppHost with stale "consumes work.dispatched / reads Mongo yaml-store" comments; retired `contracts/*.yaml` still present. | magic `AppHost/Program.cs:102-110`; ServiceBus `Program.cs` stub |
| 8 | **Medium** | Inconsistency | **Magic repo can't validate its own tenable.yaml** — its schema (`additionalProperties:false`, no `workflow` key) rejects the file when validation is enabled; the workflow-aware schema lives only in the adapters engine. | `schemas/integration.schema.json:7-31` vs adapters `Schemas/integration.schema.json:32,457` |
| 9 | **Low** | Docs | Stale docstrings claim mid-workflow resume is unsupported; code + E2E prove the opposite (per-stage/per-chunk checkpoints, resume skips published chunks). Misleading for maintainers. | `WorkflowRunner.cs:32-33` vs `:52-122` + E2E |
| 10 | **Low** | Coverage | No tenable-named YamlCollector E2E; mitigated by the structurally-identical generic `WorkflowExportVendorE2ETests`. | adapters E2E suite |

### Working as intended (verified)

- ISB ingress/host pipeline: manual-ack consume, content-detection, exec lock + heartbeat + durable stop, per-page checkpoints, outbox-guaranteed completion, delay-queue retry + DLQ, partial-wait scheduling.
- Adapter activation: inline-YAML-first resolution, schema validation, credential decryption + config overlay, `base_url` override, fallback-to-native with loop guard.
- Workflow engine: create→poll→download fully expressible and executed; capture/poll/for_each; checkpoint ordering invariant (state before advance); typed error taxonomy; publish failures never silent.
- ISB↔adapter seam is consistent: same payload contract, same name normalizer, same checkpoint envelope, host-side S3 fetch rationale (isolated adapter never needs AWS creds for YAML).

### Assumption outcomes

- A2 VALIDATED (backend → ISB trigger-flow convention; no Magic router involved).
- A3 VALIDATED (YAML inline in dispatch payload, inlined host-side from S3).
- A4 VALIDATED (native TenableIoCollector + YamlCollector both exist; `Adapters:UseYaml` + S3-hit decide, silent native fallback).
- A5 VALIDATED (snapshot recorded above).

---

## What "ready" requires (shortest path)

1. Build/declare the YAML publishing step (magic `integrations/` → S3 `{env}/yaml-files/`) with a naming contract; rename/alias `tenable.yaml` → `tenable.io.yaml` or have the backend send `integrationName`.
1b. Fix `tenable.yaml` auth to the composed-`value` form (`X-ApiKeys: accessKey={{accessKey}}; secretKey={{secretKey}}`) so platform-provisioned discrete credentials work — and audit the other 274 YAMLs for the same pre-composed-secret pattern.
2. Make the fallback observable: the silent YAML-miss → native fallback should emit a metric/event, or misconfigurations will hide indefinitely.
3. Decide ADR-0003 now — the adapters copy is the de-facto canonical engine; back-port or retire the magic copy (Direct route is already broken for workflow vendors).
4. Honor `Retry-After` inside workflow stages (map to the existing PartialWait machinery).
5. Finish ADR-0002 cleanup in magic (unwire the ServiceBus scaffold) and align the magic schema with the workflow key.

---

## Flow diagram (mermaid source)

```mermaid
sequenceDiagram
    autonumber
    participant BE as Cymulate Backend
    participant RMQ as RabbitMQ (collectors.run)
    participant ISB as ISB Host<br/>(ProcessEventCommandHandler)
    participant S3Y as S3 {env}/yaml-files/
    participant YC as YamlCollectorAdapter<br/>(external assembly)
    participant ENG as Yaml Engine<br/>(WorkflowRunner)
    participant TEN as cloud.tenable.com
    participant S3D as S3 collector data
    participant EVT as collectors.done/.progress/.error

    BE->>RMQ: AdapterRunMessage {vendor:"Tenable.io", flows, credentials(enc), lastRanAt}
    RMQ->>ISB: consume (manual-ack, content-detected trigger-flow)
    ISB->>ISB: TriggerFlowMapper → PlatformEvent (Vendor→PlatformType)
    alt Adapters:UseYaml = true (default FALSE)
        ISB->>S3Y: TryFetch "tenable.io.yaml" (vendor normalized)
        alt YAML found
            ISB->>ISB: inline payload["yaml"], ProductType=YamlEngine,<br/>synthesize inputs.start/end_time
        else miss (today: file is "tenable.yaml" → MISS)
            ISB->>ISB: revert to native TenableIoCollector (silent, INFO log)
        end
    end
    ISB->>ISB: acquire exec lock, start heartbeat, decrypt credentials
    ISB->>YC: ProcessAsync(PlatformEvent)  [ResumeAsync + AdapterCheckpoint on retry]
    YC->>YC: parse YamlCollectorPayload; YAML inline-first (S3 fallback);<br/>schema-validate; merge credentials+config; new IntegrationEngine
    alt YAML unresolved + useYaml
        YC-->>ISB: Skipped + fallbackToNative → re-dispatch native (loop-guarded)
    end
    YC->>ENG: ExecuteFlowAsync (workflow mode: stages present)
    ENG->>TEN: POST /vulns/export (X-ApiKeys) → capture export_uuid
    loop poll ≤720× / 1h, every 5s
        ENG->>TEN: GET /vulns/export/{uuid}/status
    end
    loop for_each chunk
        ENG->>TEN: GET /vulns/export/{uuid}/chunks/{n}
        ENG->>YC: mapped records (root-array mapping)
        YC->>ISB: PublishAsync(DataBatchRequest findings_NNNNNN.json)
        ISB->>S3D: upload {clientId}/{settingId}/{instanceId}/…
        ISB->>EVT: data-uploaded + progress
        YC->>ISB: OnCheckpoint (per stage + per chunk)
    end
    Note over ENG: same 3 stages repeat for assets export
    alt success
        YC-->>ISB: AdapterResult.Success {records,total,findings,assets}
        ISB->>EVT: AdapterCompletionEvent DONE (outbox-guaranteed)
        ISB->>ISB: flush + delete checkpoint
    else typed failure (INVALID_CREDENTIALS / VENDOR_HTTP_ERROR / PUBLISH_FAILED)
        ISB->>EVT: AdapterErrorEvent + failure completion
        Note over ISB: retryable → RMQ delay-queue redelivery → ResumeAsync(checkpoint)
    else Retry-After > in-process cap DURING workflow
        YC-->>ISB: YAML_WORKFLOW_FAILED (hard) — Finding #5
    end
```

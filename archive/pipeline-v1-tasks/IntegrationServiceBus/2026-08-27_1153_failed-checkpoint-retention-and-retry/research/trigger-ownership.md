# Trigger Ownership (W4)

**Headline: the trigger owner WAS found.** Collections are published by
`cymulate-integrations/apps/integration-server` (`CollectionSchedulerService`), a NestJS cron
that polls Mongo for pending run instances. The run's `correlationId` is **caller-supplied and
equals the Mongo `_id` of the `CybiClientIntegrationInstance` row** — a durable, backend-held
primary key. Every re-run today mints a NEW instance row and therefore a NEW correlationId.

---

## 1. ISB inbound surface (queues, consumers, message shape, correlationId origin)

### Queues

Configured, not hard-coded. The consumer queue list is bound from
`messaging:rabbit_mq:consumer:queues`; the checked-in local sample is the authoritative shape:

- `IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/env.local.docker.json` →
  `integration-service-bus/messaging/rabbit_mq/consumer/queues`:
  - `{ name: "collectors.run", enabled: true, category: "Collectors", exchange: "collectors.run",
     routing_key: "#", response_queue: "collectors.done" }`
  - `{ name: "collectors-events", ... }` (E2E fixture queue only, per its inline `//` note)
- publisher side, same file: `collectors_progress_queue: collectors.progress`,
  `collectors_results_queue: collectors.done`, `collectors_error_queue: collectors.error`,
  `output_queues: [{ name: "collectors.done", source_queue: "collectors.run" }, { siemrules.done ... }]`
- default when unset: `collectors.run` —
  `Infrastructure/…Infrastructure.Core/Messaging/TriggerFlowMapper.cs:270` and `:403`
  (`["SourceQueue"] = sourceQueue ?? "collectors.run"`).
- Tenant fan-out naming (`collectors.run.tenant-router`, `collectors.run.tenant-<id>`,
  `collectors.run.fallback`):
  `Infrastructure/…Infrastructure.RabbitMQ/Options/RabbitMqTenantRoutingConfig.cs:57,64,71`.
- DLX: `notification.dlq`, suffix `-dlq` (`env.local.docker.json`, consumer section).

### Consumer classes

- `Infrastructure/…Infrastructure.RabbitMQ/RabbitMqConsumerService.cs` — the broker-side consumer.
- `Applications/Cymulate.IntegrationServiceBus.Application/Messaging/IsbPlatformEventDispatcher.cs:39`
  `DispatchAsync(MessageDelivery, CancellationToken)` — the message-shape router, in order:
  1. `TriggerFlowMapper.IsTriggerFlowMessage` → `ProcessTriggerFlowAsync` (**this is the collector-run path**)
  2. `MitigationActionMapper` → IOC/IOA
  3. `WrappedAdapterMessageMapper` → queries
  4. fallback `ProcessPlatformEventAsync`
  (`IsbPlatformEventDispatcher.cs:41-62`)
- `Infrastructure/…Infrastructure.Kafka/KafkaConsumerService.cs` and
  `Infrastructure/…Infrastructure.Events/DotNetEventConsumerService.cs` exist but are not the
  collector trigger path.

### Inbound wire type — `AdapterRunMessage`

`Domain/Cymulate.IntegrationServiceBus.Domain/Messaging/AdapterRunMessage.cs:11`

| field | JSON name | line |
| --- | --- | --- |
| `Topic` | `topic` | :16-17 |
| `Vendor` | `vendor` | :22-23 |
| `CorrelationId` | `correlationId` | :28-29 |
| `Timestamp` | `timestamp` (unix ms) | :34-35 |
| `Payload` | `payload` → `AdapterRunPayload` | :40-41 |

`AdapterRunPayload` (`:47`): `instanceOid` (:52), `action` → `AdapterRunAction` (:58),
`metadata` → `MessageMetadata` (:64).
`AdapterRunAction` (`:71`): `product` (:76), `credentials` (:82, encrypted blob),
`lastRanAt` (:88), `flows[]` (:94), `filter` (:100), plus `[JsonExtensionData]` (:106) —
so extra payload keys survive.
A **flat** variant is also accepted: `AdapterRunMessageFlat` (:115) / `AdapterRunPayloadFlat` (:151),
where `flows`/`filter`/`lastRanAt`/`credentials`/`metadata` sit directly under `payload` with no
`action` wrapper. Detection of either form: `TriggerFlowMapper.IsTriggerFlowMessage` requires
`topic` + `vendor` + `payload` with a `flows` array either nested or flat
(`TriggerFlowMapper.cs:48-80`).

`MessageMetadata` (`Domain/…/Messaging/MessageMetadata.cs:10`): `instanceOid` (:15),
`instanceId` (:21), `clientID` (:27), `clientIntegrationId` (:33), `clientIntegrationFlowId` (:39),
`integrationSettingId` (:45), `integrationSettingFlowId` (:51), `storageUrl` (:57),
`[JsonExtensionData]` (:64).

### Where the production JSON in the brief comes from

The `{"Topic":…,"EventId":…,"CorrelationId":…,"Metadata":{"SourceQueue":"collectors.run",…},
"RetryCount":1,"ProductType":2}` object is **ISB's internal normalized `PlatformEvent`**, not what
the publisher sends. `PlatformEvent` is defined outside this repo:
`IntegrationInfra/src/Cymulate.Integration.Client/Models/PlatformEvent.cs:10` —
`EventId`(:15) `OccurredAt`(:20) `ActionId`(:25) `ClientId`(:30) `Topic`(:35) `Channel`(:40)
`ProductType`(:45) `Timestamp`(:50) `CorrelationId`(:55) `TenantId`(:60) `Payload`(:65)
`Metadata`(:70) `IntegrationName`(:75) `Endpoint`(:80) `HttpMethod`(:85) `Headers`(:90)
`Credentials`(:95) `RetryCount`(:100) `LastError`(:105).
`Metadata.SourceQueue` / `AdapterCategory` / `Vendor` are stamped by
`TriggerFlowMapper.BuildMetadata` (`TriggerFlowMapper.cs:262-272`), and `RetryCount` comes from the
broker delivery (`IsbPlatformEventDispatcher.cs:69,141,198` — `platformEvent.RetryCount = delivery.RetryCount`).

### correlationId origin — DECISIVE

**Caller-supplied.** ISB only mints one as a fallback:

```
TriggerFlowMapper.cs:246-248
CorrelationId = !string.IsNullOrEmpty(message.CorrelationId)
    ? message.CorrelationId
    : Guid.NewGuid().ToString();
```
(identically at `TriggerFlowMapper.cs:379-381` for the flat form).

And the caller sets it to the Mongo `_id` of the run instance:

```
cymulate-integrations/libs/collectors-helpers/src/lib/collectors-helpers.service.ts:213
correlationId: String(instance?._id),
```

That is why the production sample's `CorrelationId` (`6a8f00dd66c86fb72e4ef3d1`) is a 24-hex
ObjectId, not a GUID. The consumer side confirms the same identity in reverse:
`cymulate-integrations/apps/integration-connectors-manager/src/app/modules/connectors-manager/services/connector-manager.service.ts:689`
looks the instance up as `{ _id: data?.correlationId || data?.payload?.metadata?.instanceOid }`.

**Consequence for the feature: a backend can always name a checkpoint, because the checkpoint key
is a row id it already owns.**

---

## 2. ISB HTTP surface acting on a run

All under `Hosts/Cymulate.IntegrationServiceBus.API/Controllers/`, route base
`api/v{version:apiVersion}/[controller]`.

`EventsController.cs:19`:
- `POST api/v1/Events/publish` (`:62`, `PublishEvent` `:73`) — async; a Collectors-category topic is
  queued for background processing (`:127-130`).
- `POST api/v1/Events/publish/sync` (`:286`, `:296`) — same, synchronous
  (`ProcessCollectorEventAsync` at `:349`).
- `POST api/v1/Events/publish/debug-mq-payload` (`:231`).
- **`POST api/v1/Events/stop` (`:979`, `StopRun` `:988`)** — the one endpoint that already acts on a
  caller-supplied `correlationId`. Its own doc comment (`:966-978`) says: three layers, all keyed on
  correlationId — (1) durable Stop flag via `IStopRequestRepository.RecordAsync` (`:1015`),
  (2) fanout `AdapterStopMessage` broadcast (`:1029-1044`), (3)
  `checkpointRepository.DeleteByCorrelationIdAsync(request.CorrelationId, …)` (`:1055-1056`).
  Returns 202 regardless of whether a run was found. **This is the closest structural precedent for
  a resume endpoint: same key, same repositories, opposite direction.**

Correlation on the publish endpoints is also caller-supplied with a fallback:
`EventsController.cs:1445-1446` — `request.CorrelationId ?? request.ActionId ?? Guid.NewGuid().ToString()`.

Other controllers do not act on a run: `AdaptersController.cs` (load/unload/upload/delete/reload/
status/discover, plus `POST .../connection/test`, `.../collectors/connection/test`,
`.../ioc/connection/test` at `:406,:428,:450` — these carry a `request.CorrelationId` through at
`:561,:572,:591` but start a connection test, not a collection),
`ConfigurationController.cs`, `PlatformController.cs`, `HealthController.cs`.
`Hosts/Cymulate.IntegrationServiceBus.API.Query` and `…API.SiemRules` have **no** `Controllers/`
directory (verified by `ls`) — they are consumer-only hosts.

---

## 3. Trigger-owning repo — FOUND

**`cymulate-integrations`**, app `integration-server`, module `collection-scheduler`.

Publish site:
`cymulate-integrations/apps/integration-server/src/app/modules/collection-scheduler/services/collection-scheduler.service.ts:1094-1096`

```ts
await new RmqRecordBuilderWithSender(this.queue, action)
  .setTenantID(tenantId)
  .emit(topic);
```

`topic` is `DEFAULT_TOPIC = 'collectors.run'` (`:50`, returned by `getConfig()` at `:543`).
The `action` it emits is built at `:404` and `:1050` by
`collectorsHelpers.prepareCollectorEnvelope({ integration, flow, instance, instanceModel })`
→ `cymulate-integrations/libs/collectors-helpers/src/lib/collectors-helpers.service.ts:193-230`,
which returns exactly the flat `AdapterRunMessage` ISB expects:
`topic` (`'collector'`, or `'collector.handshake'` for a dry run — `:211`), `vendor` (`:212`),
`correlationId` (`:213`), `payload.{credentials,lastRanAt,flows,filter,metadata}` (`:214-227`),
`timestamp` (`:228`).

Two other call sites of the same envelope builder exist:
`cymulate-integrations/libs/auto-remediation-core/src/lib/auto-remediation.service.ts` and the
helpers lib itself (found by `grep -rln prepareCollectorEnvelope`). The scheduler is the collector one.

Note the topic mismatch that is resolved downstream: the publisher sends `topic: "collector"`, while
the `PlatformEvent` ISB builds carries `Topic = primaryFlow` (`TriggerFlowMapper.cs:242`, i.e.
`"CollectFindings"` — the first entry of `payload.flows`). Both appear in the brief's sample because
the sample is the post-mapping event.

---

## 4. Scheduling

**Upstream of ISB, not inside it.**

- `collection-scheduler.service.ts:67` — `@Cron('*/10 * * * * *')` on `tick()`. Every 10 seconds.
- Gated by env: `INTEGRATION_COLLECTION_SCHEDULER_ENABLED` (default **false**),
  `INTEGRATION_COLLECTION_SCHEDULER_BATCH_LIMIT` — `getConfig()` at `:535-547`.
- It is a **poller, not a calendar**: each tick aggregates `CybiClientIntegrationInstance` rows that
  are `status: Pending` with `startedAt <= now`, per tenant (`:122-130`, `:192-223`), then calls
  `processCandidate` (`:741`) → build envelope → emit.
- The calendar lives in the data: after emitting, `scheduleNextInstance` (`:694-739`) creates the
  *next* instance row with `id: randomUUID()` (`:716`), `startedAt: nextRunDate` from
  `setScheduleDate(flow.scheduleLoop, …, interval)` (`:707-711`), `status: Pending`, `scheduled: true`.
- Per-product concurrency guard in the scheduler: one run per `clientID:integrationSettingId`
  (`:199-208`).

ISB has its own internal timers, but they are **recovery**, not collection scheduling:
`Applications/…Application/Services/CheckpointRecoveryHandler.cs:26-35` — "shared by the startup
hosted service and the periodic Quartz job".

---

## 5. Identity plumbing and which ids the backend holds

All six identifiers originate in Mongo documents owned by the platform backend, and are copied
verbatim into `payload.metadata` at
`collectors-helpers.service.ts:217-225`, then into `PlatformEvent.Metadata` by
`TriggerFlowMapper.BuildMetadata` (`TriggerFlowMapper.cs:296+`).

| id | source | owner | can the backend name a past run with it? |
| --- | --- | --- | --- |
| `clientID` | `instance.clientID` → `collectors-helpers.service.ts:219` | tenant/client doc | tenant scope only, not a run |
| `instanceId` | `instance.id`, a **uuid** minted at row creation (`Admin/.../exposureAnalyticsIntegrations.controller.js:790` `id: uuidv4()`; EA `client-integration-instance.repository.ts:84` `id: StringUtil.randomUUID()`; scheduler `:716` `randomUUID()`) | `CybiClientIntegrationInstance` | **yes — names the run**, but is NOT the ISB key |
| **`correlationId`** (not in `metadata`; sits at envelope root) | `String(instance._id)` → `collectors-helpers.service.ts:213` | `CybiClientIntegrationInstance._id` | **YES — this is the ISB checkpoint key** |
| `clientIntegrationId` | `instance.clientIntegrationId` (`:220`) | `CybiClientIntegration.id` | names the connector, not the run |
| `integrationSettingId` | `instance.integrationSettingId` (`:222`) | `CybiIntegrationSetting.id` (product type) | names the product type |
| `clientIntegrationFlowId` | `instance.clientIntegrationFlowId` (`:221`) | `CybiClientIntegrationFlow.id` | names the flow |
| `integrationSettingFlowId` | `instance.integrationSettingFlowId` (`:223`) | setting's flow entry | names the flow type |
| `storageUrl` | derived: `${envPrefix}/raw-data/${clientID}/${integrationSettingId}/${instance.id}` — `collectors-helpers.service.ts:207-209`; `envPrefix` from `getShortEnvName` (`:247-258`) | derived | locates the S3 output of that run |

**Answer to "which would a backend hold to name a previously-failed run":** `instance._id`
(= correlationId) and `instance.id` (the uuid). They are two columns of the same Mongo row, so the
backend holding one holds the other. `correlationId` is the one ISB keys checkpoints on; `instance.id`
is the one the UI and `storageUrl` use. A retry API should take `correlationId`, exactly as
`POST /Events/stop` does.

Note `instanceOid` (`AdapterRunPayload.InstanceOid`, `AdapterRunMessage.cs:52`) is a third alias for
the same ObjectId — `connector-manager.service.ts:689` treats `correlationId` and
`payload.metadata.instanceOid` as interchangeable lookups on `_id`.

---

## 6. Existing retry/replay precedent

Four distinct precedents exist; only one is inside ISB.

**a. ISB-internal automatic recovery (the real precedent for resume).**
`Applications/…Application/Services/CheckpointRecoveryHandler.cs`. `RecoverAsync` (`:78`) pulls
`checkpointRepository.GetRecoverableAsync(StaleClaimThreshold, ExpectedTenantId, …)` (`:85`),
claims each row distributedly (`:295-320`), acquires a capacity slot (`:329`), sets
`platformEvent.RetryCount = Math.Max(platformEvent.RetryCount, 1)` (`:352-353`) and dispatches
fire-and-forget (`:365+`). Crucially it **rehydrates the stored `PlatformEvent` off the checkpoint
row** — `:241` logs "no PlatformEvent stored (pre-migration checkpoint)" as the skip case, `:254`
"failed to deserialize PlatformEvent". So the mechanism a future operator-initiated resume needs
(stored event + claim + dispatch) already exists; only the *entry point* is missing.
`ProcessEventCommandHandler.cs:1117` states the design rule explicitly: "The resume decision is made
on CHECKPOINT PRESENCE, never on `PlatformEvent.RetryCount`" (see also `:1167-1171`).

**b. ISB operator HTTP action on one correlationId.** `POST api/v1/Events/stop` — §2 above.
Called from Admin: `Admin/Application/app/controllers/exposureAnalyticsIntegrations.controller.js:882`
(`const stopUrl = \`${isbBaseUrl}/api/v1/Events/stop\``, POSTed at `:884` with
`{ correlationId, topic: 'collectors', reason: 'Stopped by admin' }`), routed at
`Admin/Application/app/routes/client.routes.js:151`.
The comment at `:873-876` is worth quoting: *"The collection is registered in ISB under the
correlationId it was started with — `String(instance._id)` (see collectors-helpers
prepareCollectorEnvelope) — not the uuid `id`."* Admin already knows the mapping.

**c. Admin "Run Now" / "collect since".**
`Admin/Application/app/controllers/exposureAnalyticsIntegrations.controller.js:702`
`runClientIntegrationFlow`, routed at `routes/client.routes.js:150` and
`routes/connectors.routes.js:20`. It creates a brand-new instance doc (`:783-796`) with
`triggerType: 'collect_now'`, `triggerSource: 'admin'` (`:796-797`) and an optional
`collectFrom` lookback override (`:798`, consumed at
`collectors-helpers.service.ts:137-143`). Concurrency-guarded at `:747-761`.

**d. Product "run flow" / "rerun flow".**
`cymulate-exposure-analytics/apps/server/src/app/integration/controllers/integration.controller.ts:352`
`POST clients/integrations/flows/run` → `ClientIntegrationInstanceService.run`
(`services/client-integration-instance/client-integration-instance.service.ts:24`) →
`ClientIntegrationInstanceRepository.scheduleInstance`
(`repositories/client-integration-instance/client-integration-instance.repository.ts:67`).
Also `PATCH rerun-flow/:id` (`integration.controller.ts:340`) →
`ClientIntegrationFlowService.rerun` (`services/client-integration-flow/client-integration-flow.service.ts:195`),
which is a thin wrapper over `update({...})` that recomputes `scheduledStartDate` (`:207-225`) —
i.e. it re-schedules the *flow*, it does not resume a run.

**None of a–d resumes a specific failed run on operator demand.** (a) is automatic and
correlationId-preserving; (b)–(d) are start/stop of new work.

---

## 7. Does a re-trigger reuse the original correlationId?

**Two different answers, and this is the crux of the feature.**

**Internal ISB recovery — YES, it reuses it.** `CheckpointRecoveryHandler` never constructs a new
event: it deserializes the `PlatformEvent` persisted on the checkpoint row
(`CheckpointRecoveryHandler.cs:241,254,262` are the three failure branches of that deserialization)
and dispatches that object as-is. There is no `Guid.NewGuid()` or `CorrelationId =` assignment
anywhere in the file — every `CorrelationId` reference (lines 146–589) is a read of
`checkpoint.CorrelationId` or `evt.CorrelationId` for logging/claiming. The only mutation before
dispatch is `RetryCount` (`:353`). So checkpoint lookups keyed on correlationId still resolve.

**Every operator/product re-trigger — NO, it mints a new one.** Each path creates a *new Mongo
document*, and correlationId is `String(instance._id)`, so a new `_id` means a new correlationId:

- Admin Run Now: `new CybiClientIntegrationInstance(instancePayload)` + `.save()` —
  `exposureAnalyticsIntegrations.controller.js:800-801`. `instancePayload` (`:783-799`) sets
  `id: uuidv4()` and no `_id`, so Mongo mints one.
- EA product run: `this.clientIntegrationInstanceModel.create({ id: StringUtil.randomUUID(), … })` —
  `client-integration-instance.repository.ts:83-95`.
- Scheduler's own next-occurrence: `instanceModel.create(newInstance)` after
  `delete newInstance._id` (`collection-scheduler.service.ts:714-731`) — the delete of `_id` at `:725`
  is explicit and deliberate.
- Broker-level redelivery is the one exception on the trigger side: the same message body is
  redelivered with the same `correlationId`, only `RetryCount` changes
  (`IsbPlatformEventDispatcher.cs:69,141,198`). `ProcessEventCommandHandler.cs:1123` notes this
  differs from the Rabbit-requeue case, "re-parsed into a fresh EventId".

**Implication for the design:** a retained checkpoint is keyed on the *original* correlationId, and
no existing operator-facing re-trigger path can reach it — a "Run Now" after a failure produces a
run ISB sees as brand new. A resume feature therefore cannot be built on the existing Run Now flow;
it needs an endpoint that takes the original correlationId directly (the `POST /Events/stop` shape),
and the caller that has it is Admin, which already resolves `String(instance._id)` for stop.

Also note `POST /Events/stop` **deletes** checkpoints for the correlationId
(`EventsController.cs:1055-1056`). A retention-and-retry design must decide whether an operator Stop
should still destroy the resumable checkpoint.

---

## Contradictions / gaps

- **Queue names are configuration, not code.** `config.local.json` and the deployed Secrets-Manager
  payload are not in any repo (`IntegrationServiceBus/CLAUDE.md`, Configuration section). I read
  `env.local.docker.json` and the `?? "collectors.run"` defaults in `TriggerFlowMapper.cs:270,403`.
  Production could in principle enable additional inbound queues. Confidence is nonetheless high:
  the production sample's `Metadata.SourceQueue` is literally `collectors.run`.
- **Two topic vocabularies coexist** — publisher `topic: "collector"` vs event `Topic: "CollectFindings"`
  (first of `payload.flows`). Not a contradiction, but a naming trap for anyone matching on `Topic`.
- **`auto-remediation.service.ts` also calls `prepareCollectorEnvelope`.** I did not read it; it may be
  a second publisher onto a different topic. It does not change the correlationId derivation, which
  lives in the shared helper.
- I did not verify which repo owns the `collectors.done` / `collectors.progress` consumer end beyond
  `connector-manager.service.ts` (out of scope for this task).
- `IntegrationsDomainDocs` and `IntegrationsDataFlow` were listed as candidates. I did not need
  `IntegrationsDataFlow`; I listed `IntegrationsDomainDocs` (16 README files) but answered from source
  instead, per the "prefer reading source" rule. If a written contract for the trigger exists there it
  is unread — flag if a doc-level citation is wanted.
- `cymulate-magic-integration` and `cymulate-integration-parsers` produced no trigger publish sites in
  the `collectors.run` / `CollectFindings` sweeps.

## Speculation (explicitly labelled)

- **SPECULATION:** the natural home for an operator resume is `EventsController` as
  `POST api/v1/Events/resume` taking `{ correlationId, reason }`, mirroring `StopRun`
  (`EventsController.cs:988`) and delegating to the already-existing claim-and-dispatch path in
  `CheckpointRecoveryHandler.TryDispatchAsync`. I did not verify that `TryDispatchAsync` is reachable
  from a request scope or that `ICheckpointRecoveryHandler` exposes a single-checkpoint entry point —
  only `RecoverAsync` and `ResumeScheduledWaitsAsync` were confirmed public
  (`CheckpointRecoveryHandler.cs:78,115`). W1/W2 own that surface.
- **SPECULATION:** the caller would be Admin
  (`exposureAnalyticsIntegrations.controller.js`), since it is the only code on this machine that
  already computes `String(instance._id)` as an ISB correlationId and holds `serviceBusInternalServerURL`
  (`:877-880`). Not verified against any product requirement.
- **SPECULATION:** `triggerType` / `triggerSource` on the instance doc
  (`exposureAnalyticsIntegrations.controller.js:796-797`) look like the right place to record a
  `'resume'` trigger for audit, but they are set with `strict:false`-style ad-hoc fields in Admin and
  I did not confirm they are declared on the shared `cySharedDBModels` schema.

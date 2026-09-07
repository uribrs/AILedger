# ISB Checkpoint Lifecycle (W1)

Scope: `/Users/user/Dev/IntegrationServiceBus`, branch `dev` @ `b5e7ba22`. All paths below are relative
to `src/Cymulate.IntegrationServiceBus/`. Every claim carries a `path:line`; anything not read from
source is labelled `SPECULATION:`.

`ReadMEs/evaluate_query_flow.md` was checked and does **not** bear on this scope — it covers the
query-evaluation path (`EvaluateQueryCommandHandler` / `QueryEvaluator` / `query_integration_*` tables),
which is a different state machine with its own status enum and its own recovery sweep. The adapter
checkpoint (`adapter_checkpoints`) has no authoritative doc in the repo; the durable rationale lives in
XML remarks on the members cited below.

Two `ICheckpointRepository` implementations exist and both are production code:
`Infrastructure.Postgres/Persistence/CheckpointRepository.cs:22` (deployed hosts, registered at
`Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.Postgres/DependencyInjection.cs:40`) and
`Infrastructure.Core/Services/InMemoryCheckpointRepository.cs:17` (Console host / fallback, registered
`TryAddSingleton` at `Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.Core/DependencyInjection.cs:165`).
Any change to the store contract must land in both — the in-memory one deliberately mirrors the
Postgres refusal split (`InMemoryCheckpointRepository.cs:91-104`).

---

## 1. Write path

**Who builds the row.** Exactly one production method constructs a `CheckpointEntry` for the store
during a run: `AdapterExecutionContext.SnapshotCheckpointEntry`
(`Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.Core/Services/AdapterExecutionContext.cs:990-1019`).
It is stated as the only such path in its own remarks
(`AdapterExecutionContext.cs:987-988`). It copies page/items/findings/sequence/cursor/lastId, serializes
`ctx.AdapterState` to `AdapterStateJson` (`AdapterExecutionContext.cs:992-996`), carries
`PlatformEventJson` and `ClaimedByInstance = _executionOwnerInstanceId`
(`AdapterExecutionContext.cs:1011-1012`), and stamps `UpdatedAtUtc = DateTime.UtcNow`
(`AdapterExecutionContext.cs:1017`).

It **never sets `Status`**, so every row it writes carries the property default
`CheckpointStatus.Idle` (`Domain/Cymulate.IntegrationServiceBus.Domain/Models/CheckpointEntry.cs:102`).

**When it is written.** The adapter's `OnCheckpoint` callback, wired in
`AdapterExecutionContext.CreateProgressContext` (`AdapterExecutionContext.cs:141-189`). The snapshot is
taken synchronously, stored in `_latestCheckpointEntry` (`:143-144`), and the DB write is offloaded to
`Task.Run` and **chained onto the previous write** so page order is preserved
(`:145-152`). Refusals are recorded and classified inside that chain (`:155-176`), and
`ActOnRefusal` runs there deliberately so the flush barriers cannot complete before the claim-lost
callback has fired (`:171-175`).

There is a second write entry point: `AdapterExecutionContext.PersistLatestCheckpointSnapshotAsync`
(`AdapterExecutionContext.cs:917-976`) — a synchronous repair write used by host lifecycle paths before
they flip status or clean up (called from `EnsureLatestCheckpointPersistedAsync`, see §2).

**Row creation / lock.** The run's row is first inserted by
`ICheckpointRepository.TryAcquireExecutionLockAsync`, called from
`Applications/Cymulate.IntegrationServiceBus.Application/Commands/ProcessEventCommandHandler.cs:140-141`
with an entry that carries only key fields + `PlatformEventJson`
(`ProcessEventCommandHandler.cs:128-135`). The SQL is
`CheckpointRepository.cs:594-618`; it does not mention the `status` column at all, so a freshly-inserted
row takes the DB default `'Idle'`
(`Infrastructure.Postgres/Migrations/20260526144503_AddCheckpointStatusAndScheduledResume.cs:20-25`,
`Infrastructure.Postgres/Persistence/CheckpointDbContext.cs:87-91`). Progress fields are **not** reset on
conflict, which is what preserves progress for resume (`CheckpointRepository.cs:593`).

**Guards on the write.** `CheckpointRepository.UpsertAsync` (`CheckpointRepository.cs:24-243`) is one
`INSERT ... ON CONFLICT DO UPDATE` wrapped in a data-modifying CTE that also returns the state the write
was attempted against (`:56-127`). The `DO UPDATE ... WHERE` guards (`:97-109`) are, verbatim:

```sql
WHERE adapter_checkpoints.status <> 'ScheduledWait'
  AND adapter_checkpoints.current_page <= EXCLUDED.current_page
  AND NOT EXISTS (
      SELECT 1
      FROM adapter_stop_requests sr
      WHERE sr.correlation_id = EXCLUDED.correlation_id
  )
  AND (
      (EXCLUDED.claimed_by_instance IS NOT NULL
       AND adapter_checkpoints.claimed_by_instance = EXCLUDED.claimed_by_instance)
      OR (EXCLUDED.claimed_by_instance IS NULL
          AND adapter_checkpoints.claimed_by_instance IS NULL)
  )
```

So: **not parked**, **monotonic page**, **no stop request**, **same owner**. The INSERT arm carries only
the stop-request guard (`:76-80`). Refusals are named by `ClassifyFromSnapshot`
(`CheckpointRepository.cs:281-356`), ordered by severity: `StopRequested` → row-missing `Unspecified` →
owner mismatch (`ClaimLost`, or `Unspecified`-orphaned when the row is owned by nobody, `:319-336`) →
`ParkedInScheduledWait` → `PageOrder` → `Unspecified`.

**What stamps `updated_at_utc`.**

| writer | value | citation |
| --- | --- | --- |
| `UpsertAsync` INSERT and UPDATE arms | server-side `@now = DateTime.UtcNow` captured in the repo, **not** the caller's `entry.UpdatedAtUtc` | `CheckpointRepository.cs:29`, `:75`, `:96`, `:169` |
| `TryAcquireExecutionLockAsync` | `now` (both insert and conflict-update) | `CheckpointRepository.cs:582`, `:608`, `:614` |
| `TransitionFromScheduledWaitAsync` | `now` | `CheckpointRepository.cs:707`, `:721` |
| `TransitionToScheduledWaitAsync` | `nowUtc` | `CheckpointRepository.cs:746`, `:753` |
| `TryClaimAsync` / `RenewClaimAsync` / `ReleaseClaimAsync` / `ReleaseAllClaimsForInstanceAsync` | **do not touch `updated_at_utc`** — only `claimed_*` | `CheckpointRepository.cs:493-502`, `:559-568`, `:528-537`, `:641-646` |
| in-memory store | `DateTime.UtcNow` on transitions; keeps the caller's `UpdatedAtUtc` on upsert | `InMemoryCheckpointRepository.cs:311`, `:348` |

That last row is load-bearing for retention design: **claiming or heart-beating a row does not refresh
its TTL age.** Only a real write (upsert / lock / status transition) does. The recovery handler's
remarks say exactly this and rely on it (`Applications/.../Services/CheckpointRecoveryHandler.cs:694-697`).

---

## 2. Deletion paths (incl. failure)

Nine production call sites delete checkpoint rows. Ordered by relevance:

**(a) Normal completion — INCLUDING a failing adapter result.**
`ProcessEventCommandHandler.Handle` → `CompleteExecutionAsync` (`ProcessEventCommandHandler.cs:298-300`)
→ `FlushAndCleanupCheckpointAsync` (`:814-815`) → `checkpointRepository.DeleteAsync`
(`:1228-1232`).

`CompleteExecutionAsync` calls `FlushAndCleanupCheckpointAsync` as its **first statement**
(`ProcessEventCommandHandler.cs:814`), before it ever inspects `result.Success` — that inspection is
only reached at `:831`, and only decides which log line is emitted. `FlushAndCleanupCheckpointAsync`
itself is unconditional: it awaits the write chain, returns early **only** if a checkpoint write was
refused (`:1222-1223`), and otherwise deletes (`:1226-1239`). It takes no `AdapterResult` parameter at
all (`:1213-1217`).

**CONFIRMED: a run whose adapter returns a failing `AdapterResult` deletes its checkpoint, via
`Handle:298 → CompleteExecutionAsync:814 → FlushAndCleanupCheckpointAsync:1228 → DeleteAsync`.**
The only failing results that escape this are the ones that return before `CompleteExecutionAsync` is
reached: claim-loss / orphan (`:238-270`), external cancellation (`:274-281`), `PartialWaitRequired`
(`:284-288`), and yaml→native fallback (`:292-296`).

**(b) Adapter not found.** `HandleAdapterNotFoundAsync` → `TryDeleteCheckpointAsync`
(`ProcessEventCommandHandler.cs:790` → `:1093-1106` → `DeleteAsync`). Publishes a failed done
(`:796-797`).

**(c) Unhandled exception.** `HandleUnhandledExceptionAsync` → `TryDeleteCheckpointAsync`
(`ProcessEventCommandHandler.cs:880`). Publishes a failed done (`:889-890`).

**(d) Stop requested — early guard.** `Handle` (`ProcessEventCommandHandler.cs:93`) →
`TryDeleteCheckpointsByCorrelationIdAsync` → `DeleteByCorrelationIdAsync` (all tenants/platforms/
categories), after publishing a cancelled done (`:88-89`).

**(e) Stop requested — cancellation catches.** `ProcessEventCommandHandler.cs:320` (external CTS) and
`:354` (heartbeat CTS), both `DeleteByCorrelationIdAsync`.

**(f) Stop requested — heartbeat poll.** `RunHeartbeatAsync` (`ProcessEventCommandHandler.cs:1043`),
`DeleteByCorrelationIdAsync`.

**(g) Stop requested — refused checkpoint write.** `HandleRejectedCheckpointWriteAsync`
(`ProcessEventCommandHandler.cs:1393`), `DeleteByCorrelationIdAsync`.

**(h) Stop API.** `EventsController.StopRun` step 3
(`Hosts/Cymulate.IntegrationServiceBus.API/Controllers/EventsController.cs:1055-1056`),
`DeleteByCorrelationIdAsync`.

**(i) Unservable-run termination.** `CheckpointRecoveryHandler.EndUnservableRunAsync`
(`Applications/.../Services/CheckpointRecoveryHandler.cs:867-868`), `DeleteAsync`, after publishing a
terminal failed done (`:851`) and only past `UnservableTerminalAge` (`:817-819`).

**(j) TTL / stop-aware sweep.** `CheckpointCleanupJob.Execute` →
`DeleteStoppedCheckpointsAsync` (`Infrastructure.Core/Jobs/CheckpointCleanupJob.cs:49`) and
`DeleteExpiredAsync` (`:58`). See §5.

**Paths that deliberately do NOT delete** (the run stays recoverable):
claim-lost / superseded (`ProcessEventCommandHandler.cs:267-269`, `:370-372`, `:1415-1420` — claim left
in place on purpose), orphaned / unclassified refusal → `HandBackForRecoveryAsync`
(`:1472-1501`, releases the claim only), checkpoint-persistence failure →
`HandleCheckpointPersistenceFailureAsync` (`:902-928`, releases claim only), and instance shutdown
(`:332`).

---

## 3. `CheckpointStatus` values and their production write sites

Enum: `Domain/Cymulate.IntegrationServiceBus.Domain/Enums/CheckpointStatus.cs:6-22` —
`InFlight` (`:9`), `Idle` (`:12`), `ScheduledWait` (`:18`), `Failed` (`:21`, doc-commented
"Terminal non-retryable failure").

| value | production write sites |
| --- | --- |
| `Idle` | CLR default on the entity (`CheckpointEntry.cs:102`); EF default + sentinel (`CheckpointDbContext.cs:87-91`); column default `'Idle'` (`20260526144503_AddCheckpointStatusAndScheduledResume.cs:20-25`); `UpsertAsync` binds `@status` from `entry.Status.ToString()` (`CheckpointRepository.cs:36`, `:163`) — and every production entry is `Idle` because `SnapshotCheckpointEntry` never sets it (`AdapterExecutionContext.cs:998-1018`) and neither does the lock entry (`ProcessEventCommandHandler.cs:128-135`); `TransitionFromScheduledWaitAsync` writes `status = 'Idle'` literally (`CheckpointRepository.cs:719`), in-memory equivalent `InMemoryCheckpointRepository.cs:349` |
| `ScheduledWait` | `TransitionToScheduledWaitAsync` (`CheckpointRepository.cs:751`), in-memory `InMemoryCheckpointRepository.cs:309`. Sole caller: `ProcessEventCommandHandler.HandlePartialWaitAsync` (`:956-958`) |
| `InFlight` | **none** |
| `Failed` | **none** |

Grep evidence: the only production `CheckpointStatus.` references outside tests are the four
`ScheduledWait` comparisons, the two `Idle` defaults, and the two in-memory assignments listed above —
no production code names `CheckpointStatus.Failed` or `CheckpointStatus.InFlight` anywhere
(`ProcessEventCommandHandler.cs:111`, `CheckpointRepository.cs:339`, `:447`, `:670`, `:687`,
`CheckpointDbContext.cs:90-91`, `InMemoryCheckpointRepository.cs:106`, `:155`, `:272`, `:285`, `:309`,
`:324`, `:349`, `CheckpointEntry.cs:102`).

**CONFIRMED: `Failed` is dead in production, and so is `InFlight`.** The column is `text` with no CHECK
constraint (`20260526144503_...cs:20-25`), so a new or existing value can be written with no schema
change — but see §4/§5 for what reading code does with an unknown value.

---

## 4. Recovery selection predicate

`CheckpointRepository.GetRecoverableAsync` (`CheckpointRepository.cs:658-675`), verbatim:

```csharp
var staleBeforeUtc = DateTime.UtcNow - staleThreshold;

var query = context.Checkpoints
    .AsNoTracking()
    .Where(c => c.Status != CheckpointStatus.ScheduledWait
                && (c.ClaimedByInstance == null || c.ClaimedAtUtc < staleBeforeUtc))
    .Where(CheckpointPartition.OwnedByPartitionPredicate(tenantId));
```

In-memory equivalent: `InMemoryCheckpointRepository.cs:272` (`Status != ScheduledWait` plus the same
claim test).

The partition predicate is `Domain/.../Models/CheckpointPartition.cs:25-30`: a dedicated pod
(`TENANT_ID` set) takes `TenantId == tenantId`; a shared pod takes `null / "" / "default" / "none"`.
Caller: `CheckpointRecoveryHandler.RecoverAsync` (`CheckpointRecoveryHandler.cs:85`), with
`StaleClaimThreshold` (default 10 min, `ConfigurationKeys.cs:346-348`) and the static
`ExpectedTenantId` from the `TENANT_ID` env var (`CheckpointRecoveryHandler.cs:56-57`). Sweep cadence:
`CheckpointRecoveryJob.DefaultIntervalMinutes = 5` (`Applications/.../Jobs/CheckpointRecoveryJob.cs:18`).

**CONFIRMED — belief holds: the predicate filters only on `Status != ScheduledWait` plus claim
staleness (plus the tenant partition, which the belief omitted).**

**Would an unclaimed retained row be picked up? Yes — unconditionally.** `ClaimedByInstance == null`
satisfies the claim half immediately, and any status other than `ScheduledWait` — `Idle` today, `Failed`
if it were ever written — satisfies the status half. Post-selection, `TryDispatchAsync` would then
skip it only for reasons unrelated to its status: no `PlatformEventJson` (`CheckpointRecoveryHandler.cs:238-244`),
unparseable event (`:246-265`), already recovering in-process (`:270-277`), the collector answering
"I cannot read this state" via `ICheckpointStateCompatibility` (`:283-287`), or a lost claim race
(`:313-320`). None of those is a retention marker. A retained failed row left `Idle` and unclaimed is
re-dispatched on the next sweep, within 5 minutes.

Note also that the failure branch of a recovery dispatch **releases the claim** when the dispatched
command does not succeed (`CheckpointRecoveryHandler.cs:422-436`), so a retained row would be re-offered
on every subsequent sweep.

---

## 5. Cleanup selection predicate

`CheckpointRepository.DeleteExpiredAsync` (`CheckpointRepository.cs:441-449`), verbatim:

```csharp
return await context.Checkpoints
    .Where(c => c.UpdatedAtUtc < cutoffUtc && c.Status != CheckpointStatus.ScheduledWait)
    .ExecuteDeleteAsync(cancellationToken);
```

In-memory equivalent: `InMemoryCheckpointRepository.cs:152-163` (same predicate).

Driver: `CheckpointCleanupJob` (`Infrastructure.Core/Jobs/CheckpointCleanupJob.cs:32-86`). It reads
`TtlHours` from the Quartz job data map (`:38-40`) and computes `cutoff = DateTime.UtcNow.AddHours(-ttlHours)`
(`:42`). The job data is populated at wiring time from the config key
`Checkpoint:TtlHours` (`Infrastructure.Core/DependencyInjection.cs:275-280`, `:290-292`); the constant is
`ConfigurationKeys.Checkpoint.TtlHours = "Checkpoint:TtlHours"`
(`Domain/.../Constants/ConfigurationKeys.cs:342`) with
`DefaultTtlHours = 24` (`ConfigurationKeys.cs:357`, aliased at `CheckpointCleanupJob.cs:25`).

Job cadence: `Checkpoint:CleanupIntervalMinutes` (`ConfigurationKeys.cs:343`), default 30
(`CheckpointCleanupJob.cs:19`, wired `DependencyInjection.cs:282-300`).

**Statuses excluded: `ScheduledWait` only.** The same job additionally deletes every row whose
correlation id has a stop request, with **no age condition at all**
(`CheckpointCleanupJob.cs:49` → `DeleteStoppedCheckpointsAsync`, `CheckpointRepository.cs:768-782`) —
so a stopped correlation's row is removed on the next 30-minute tick regardless of retention intent.

**CONFIRMED — belief holds: one cutoff (`Checkpoint:TtlHours`, default 24) applied to everything except
`ScheduledWait`.** Two additions the belief did not state: the same cutoff is also applied to the
stop-request table in the same job (`CheckpointCleanupJob.cs:73`), and the stop-aware delete runs
ahead of it with no cutoff.

---

## 6. Values derived from the TTL config key

`Checkpoint:TtlHours` (default 24) feeds exactly three consumers:

1. **Checkpoint deletion horizon.** `DependencyInjection.cs:275-280` → job data →
   `CheckpointCleanupJob.cs:38-42` → `DeleteExpiredAsync` cutoff (`:58`).
2. **Stop-request row deletion horizon.** The *same* cutoff variable is passed to
   `stopRequestRepository.DeleteExpiredAsync` (`CheckpointCleanupJob.cs:73`), with the stated rationale
   at `:71-72`.
3. **`CheckpointRecoveryHandler.CheckpointTtl`** (`CheckpointRecoveryHandler.cs:706-707`), read from the
   same key with the same default, feeding **`UnservableTerminalAge`**
   (`CheckpointRecoveryHandler.cs:728-736`):

```csharp
var terminal = CheckpointTtl * 0.75;
var escalation = UnservableEscalationAge;   // == StaleClaimThreshold * 6
return terminal > escalation ? terminal : escalation;
```

**PARTIALLY CONTRADICTS the stated belief.** `UnservableTerminalAge` is not simply `CheckpointTtl * 0.75`
(18h at defaults) — it is `max(CheckpointTtl * 0.75, StaleClaimThreshold * 6)`
(`CheckpointRecoveryHandler.cs:732-734`), with `UnservableEscalationAge = StaleClaimThreshold * 6`
(`:699`, 1h at defaults). The floor exists so that raising the stale threshold cannot end runs before
they have been reported unservable (`:721-726`). At default config the `0.75` term wins, so the belief's
number is right and its formula is not — and the formula is what matters if TTL is lowered or the stale
threshold raised.

`UnservableTerminalAge` gates `EndUnservableRunAsync` (`:817-819`), which publishes the terminal failed
done and deletes the row. `UnservableEscalationAge` gates the repeating Error log
(`EscalateIfNobodyCanServe`, `:760-784`).

**Raising `Checkpoint:TtlHours` would move all three at once:** rows survive longer, *stop-request rows
survive longer* (so a stopped correlation stays un-writable and un-resumable for longer —
`CheckpointRepository.cs:76-80`, `:99-103`), and the unservable-run terminal bound slides later, delaying
the failed done that tells the platform an unreadable run ended. That coupling is the reason the constant
is shared rather than duplicated (`ConfigurationKeys.cs:351-356`, `CheckpointCleanupJob.cs:21-24`) — and
it is why "keep failed checkpoints for a week" must not be implemented by raising this key.

---

## 7. Resume entry point

`ProcessEventCommandHandler.ExecuteWithResumeAsync` (`ProcessEventCommandHandler.cs:1141-1208`).

The decision, in order:
1. Unwrap decorators to find an `IResumableAdapter`; if there is none, plain
   `ProcessAsync` (`:1149-1151`).
2. `checkpointRepository.GetAsync(tenantId, correlationId, productType, category)` (`:1153-1158`).
3. **If the row is null → `ProcessAsync` (fresh)** (`:1160-1161`).
4. `MapToAdapterCheckpoint(checkpoint)` (`:1163`, mapping at `:1716-1740`).
5. `resumable.CanResumeFrom(adapterCheckpoint)` — the **collector** decides (`:1165`). True →
   `resumable.ResumeAsync(platformEvent, adapterCheckpoint, ct)` (`:1179`). False → logged
   "declined resume" and `ProcessAsync` fresh (`:1203-1207`).

**What is passed to the adapter**: the SDK `AdapterCheckpoint` record built at
`ProcessEventCommandHandler.cs:1725-1739` — `CurrentPage`, `ProcessedItems`, `ProcessedFindings`,
`CurrentSequenceId`, `CursorToken`, `LastProcessedId`, `AdapterState` (deserialized to
`Dictionary<string,string>`, `:1718-1723`), `CreatedAtUtc`, and the four checkpoint-kind/reason/batch
fields.

**Is it sensitive to how the run was triggered? No.** `RetryCount` is read only for a log line
(`:1169-1171`), and the remarks state explicitly that resume is decided on checkpoint presence and never
on `RetryCount` (`:1117`), with the 2026-08-11 incident that forced the change recorded at `:1129-1134`.
The recovery handler's `platformEvent.RetryCount = Math.Max(RetryCount, 1)`
(`CheckpointRecoveryHandler.cs:353`) is documented as vestigial (`ProcessEventCommandHandler.cs:1136-1139`).

**CONFIRMED — belief holds.** One consequence worth stating for §10: because the decision is *only*
"is there a row, and does the collector accept it", **re-publishing the original platform event for a
correlation id whose row still exists already produces a resume today**, with no new code — provided no
stop request exists for that correlation id (`ProcessEventCommandHandler.cs:80-98`) and the row is not
parked (`:111-117`).

---

## 8. Existing servicing surface (endpoints + consumers)

### HTTP — `EventsController`, route template `api/v{version:apiVersion}/[controller]`
(`Hosts/Cymulate.IntegrationServiceBus.API/Controllers/EventsController.cs:19`)

| route | method | acts on correlationId | repository calls |
| --- | --- | --- | --- |
| `POST .../Events/publish` | `PublishEvent` (`:73`) | no (mints/accepts one in the body) | none directly — queues for background processing (`:80-99`) |
| `POST .../Events/publish/debug-mq-payload` | `:231` | no | none |
| `POST .../Events/publish/sync` | `:286` | no | none |
| `POST .../Events/stop` | `StopRun` (`:988`) | **yes** — `StopRunRequest.CorrelationId` (`:995`) | `IStopRequestRepository.RecordAsync` (`:1015`), `IStopBroadcaster.PublishAsync` (`:1041`), `ICheckpointRepository.DeleteByCorrelationIdAsync` (`:1055-1056`) |
| `GET .../Events/{platformType}/topics` | `:1089` | no | none |
| `DELETE .../Events/{platformType}/iocs` | `:1125` | no | none |
| `POST .../Events/{platformType}/iocs/list` | `:1160` | no | none |
| `GET .../Events/journal` | `:1205` | no (filter/paging) | `IEventJournal.GetEntries` |
| `GET .../Events/journal/{correlationId}` | `GetJournalEntry` (`:1251`) | **yes** | `eventJournal.GetByCorrelationId` (`:1253`) — in-process journal, not the checkpoint store |

Other controllers in the host: `AdaptersController`, `ConfigurationController`, `HealthController`,
`PlatformController` (`Hosts/Cymulate.IntegrationServiceBus.API/Controllers/`). None of them touches
`ICheckpointRepository` (grep for `ICheckpointRepository` across `Hosts/` returns only
`EventsController.StopRun`'s `[FromServices]` parameter at `:992` and two comment lines in
`API.SiemRules/Program.cs:31`, `:36`).

**`POST /Events/stop` is the only endpoint that acts on the checkpoint store, and it is the shape a
retry trigger would mirror**: correlation-id keyed, `[FromServices] ICheckpointRepository`,
fire-and-forget 202 (`:1069-1079`), with a typed request/response pair
(`Hosts/Cymulate.IntegrationServiceBus.API/Models/StopRunRequest.cs`).

### Inbound message consumers

- **Work queues** → `RabbitMqConsumerService`
  (`Infrastructure.RabbitMQ/RabbitMqConsumerService.cs`), queue set from configuration
  (`:111-121`), each delivery handed to `IMessageDispatcher`.
- **`IsbPlatformEventDispatcher`** (`Applications/.../Messaging/IsbPlatformEventDispatcher.cs:39-62`)
  probes the body in a fixed order — trigger-flow (`:42`), mitigation (`:49`), wrapped adapter (`:55`),
  plain platform event (`:61`) — and sends `ProcessEventCommand` through the mediator
  (`:85-86`). This is the path any re-dispatched run travels; it is also the path
  `CheckpointRecoveryHandler` short-circuits by sending `ProcessEventCommand` directly
  (`CheckpointRecoveryHandler.cs:390`).
- **Stop fanout** → `RabbitMqStopBroadcastService` (`Infrastructure.RabbitMQ/RabbitMqStopBroadcastService.cs:28`),
  one auto-delete queue per pod bound to the stop exchange (`:176-197`), handler calls
  `IRunningExecutionRegistry.Cancel(correlationId)` (`:228`). Correlation-id keyed, no repository work.
- **Query pod** → `QueryIntegrationDispatcher`
  (`Hosts/Cymulate.IntegrationServiceBus.API.Query/Messaging/QueryIntegrationDispatcher.cs`) and
  **SIEM-rules pod** → `SiemRulesDispatcher`
  (`Hosts/Cymulate.IntegrationServiceBus.API.SiemRules/Messaging/SiemRulesDispatcher.cs`). Neither
  handles adapter checkpoints (`IsbPlatformEventDispatcher.cs:30-31` states the query split explicitly).

There is **no existing inbound message type that services a checkpoint by correlation id** other than
stop. A retry trigger is a new message type or a new endpoint either way.

---

## 9. `ICheckpointRepository` surface

`Domain/Cymulate.IntegrationServiceBus.Domain/Interfaces/ICheckpointRepository.cs:12-195`. Key is
`(TenantId, CorrelationId, PlatformType, Category)` (`:8-10`).

| # | signature | line |
| --- | --- | --- |
| 1 | `Task<CheckpointWriteResult> UpsertAsync(CheckpointEntry entry, CancellationToken ct = default)` | `:31` |
| 2 | `Task<CheckpointEntry?> GetAsync(string tenantId, string correlationId, PlatformType platformType, AdapterCategory category, CancellationToken ct = default)` | `:36` |
| 3 | `Task DeleteAsync(string tenantId, string correlationId, PlatformType platformType, AdapterCategory category, CancellationToken ct = default)` | `:41` |
| 4 | `Task<int> DeleteByCorrelationIdAsync(string correlationId, CancellationToken ct = default)` | `:49` |
| 5 | `Task<int> DeleteExpiredAsync(DateTime cutoffUtc, CancellationToken ct = default)` | `:55` |
| 6 | `Task<IReadOnlyList<CheckpointEntry>> GetAllAsync(CancellationToken ct = default)` | `:60` |
| 7 | `Task<bool> TryClaimAsync(string tenantId, string correlationId, PlatformType platformType, AdapterCategory category, string instanceId, TimeSpan staleThreshold, CancellationToken ct = default)` | `:68-75` |
| 8 | `Task ReleaseClaimAsync(string tenantId, string correlationId, PlatformType platformType, AdapterCategory category, string instanceId, CancellationToken ct = default)` | `:82-88` |
| 9 | `Task<bool> RenewClaimAsync(string tenantId, string correlationId, PlatformType platformType, AdapterCategory category, string instanceId, CancellationToken ct = default)` | `:95-101` |
| 10 | `Task<bool> TryAcquireExecutionLockAsync(CheckpointEntry entry, string instanceId, TimeSpan staleThreshold, CancellationToken ct = default)` | `:109-113` |
| 11 | `Task<int> ReleaseAllClaimsForInstanceAsync(string instanceId, CancellationToken ct = default)` | `:121` |
| 12 | `Task<IReadOnlyList<CheckpointEntry>> GetRecoverableAsync(TimeSpan staleThreshold, string? tenantId = null, CancellationToken ct = default)` | `:129-132` |
| 13 | `Task<IReadOnlyList<CheckpointEntry>> GetScheduledWaitsDueByAsync(DateTime horizonUtc, string? tenantId = null, CancellationToken ct = default)` | `:143-146` |
| 14 | `Task<bool> TransitionFromScheduledWaitAsync(string tenantId, string correlationId, PlatformType platformType, AdapterCategory category, string instanceId, CancellationToken ct = default)` | `:163-169` |
| 15 | `Task<bool> TransitionToScheduledWaitAsync(string tenantId, string correlationId, PlatformType platformType, AdapterCategory category, string instanceId, DateTime scheduledResumeAtUtc, CancellationToken ct = default)` | `:181-188` |
| 16 | `Task<int> DeleteStoppedCheckpointsAsync(CancellationToken ct = default)` | `:195` |

**Which already claim a specific row by key?**

- **`TryClaimAsync` (#7)** — claims *this exact key* for an instance, succeeding only if unclaimed or
  stale (`CheckpointRepository.cs:493-502`). This is precisely the "claim a named checkpoint" primitive
  an operator-initiated resume needs, and it is already used that way twice: by the sweep
  (`CheckpointRecoveryHandler.cs:295-302`) and by `EndUnservableRunAsync` (`:824-831`).
- **`TransitionFromScheduledWaitAsync` (#14)** — CAS that flips status *and* hands the claim in one
  statement (`CheckpointRepository.cs:716-727`). This is the shape to copy for a
  "retained → resumable, claimed by me" transition: it is the existing precedent for electing exactly
  one resumer across replicas without ever leaving the row owned by nobody (`ICheckpointRepository.cs:154-161`).
- **`TryAcquireExecutionLockAsync` (#10)** — claims by key too, but inserts if absent and reclaims when
  stale or same-instance (`CheckpointRepository.cs:594-618`); it is the *execution* lock, taken by the
  command handler, not an out-of-band claim.

There is **no** `GetByCorrelationIdAsync` returning all rows for a correlation id across tenant/platform/
category — only the bulk delete (#4) works that way. A resume request that carries only a correlation id
(which is what every other servicing surface takes, §8) therefore has no read to resolve it to a key
today. `GetAllAsync` (#6) exists but is a full-table scan.

---

## 10. Minimal service-hub change

ISB **holds** checkpoint state and **services** a request against a named checkpoint. It does not judge
retryability, classify failures, or decide whether a resume is advisable. Everything below is stated
in those terms: no code chooses to retain, only *records what the caller said*, and no code chooses to
resume, only *services a resume that was asked for*.

The four requirements decompose into five changes. Ordered by dependency.

**(a) Retain a failed run's checkpoint instead of deleting it.**
File: `Applications/Cymulate.IntegrationServiceBus.Application/Commands/ProcessEventCommandHandler.cs`.
Nature: `CompleteExecutionAsync` (`:805-865`) currently calls `FlushAndCleanupCheckpointAsync` (`:814`)
before it has looked at `result`. The change is to pass the outcome down — either the `AdapterResult`
itself or a boolean already computed by the caller — and, on the non-success arm, flush the write chain
and **stop before the `DeleteAsync` at `:1228`**, marking the row retained (see (b)) instead. The refusal
early-return at `:1222-1223` must stay ahead of it unchanged: a refused write means this execution does
not own the row and must not write a retention marker onto it either.
Scope note: this is the *only* delete that needs to change. Paths (b)-(h) in §2 are adapter-not-found,
unhandled exception and stop — the first two never produced adapter state worth resuming from, and stop
is an explicit human decision to end the run. Leaving them deleting is the smaller change and the
correct one.

**(b) Keep the recovery sweep from re-dispatching a retained row.**
Files: `Domain/.../Enums/CheckpointStatus.cs`, `Infrastructure.Postgres/Persistence/CheckpointRepository.cs`,
`Infrastructure.Core/Services/InMemoryCheckpointRepository.cs`.
Nature: the sweep's only status exclusion is `ScheduledWait` (`CheckpointRepository.cs:670`,
`InMemoryCheckpointRepository.cs:272`), so a retained row left `Idle` is re-dispatched within 5 minutes
(§4). Reuse the already-declared, never-written `CheckpointStatus.Failed`
(`CheckpointStatus.cs:21`) as the retained marker — no enum change, no schema change (the column is
plain `text` with no CHECK constraint, `20260526144503_...cs:20-25`) — and widen the exclusion in both
stores to `Status != ScheduledWait && Status != Failed`. The writer is a new surgical transition
modelled on `TransitionToScheduledWaitAsync` (`CheckpointRepository.cs:732-762`): owner-guarded, touching
only `status`, the retention deadline column from (c), and `updated_at_utc`, so the adapter-flushed
page/cursor/`adapter_state` survive the flip. `UpsertAsync`'s `DO UPDATE` guard (`:97`) should exclude
`Failed` the same way it excludes `ScheduledWait`, so a late write from a dead execution cannot silently
un-retire a retained row.
Also required in the same change: `ProcessEventCommandHandler.Handle`'s early guard reads only
`Status == ScheduledWait` (`:111-117`); a redelivery meeting a retained row must not start a fresh
execution over it.

**(c) Expire it on a separate, longer clock.**
Files: `Infrastructure.Postgres/Persistence/CheckpointRepository.cs` (+ a migration under
`Infrastructure.Postgres/Migrations/`), `Infrastructure.Core/Jobs/CheckpointCleanupJob.cs`,
`Domain/.../Constants/ConfigurationKeys.cs`, `Infrastructure.Core/DependencyInjection.cs`,
`Infrastructure.Core/Services/InMemoryCheckpointRepository.cs`.
Nature: **do not raise `Checkpoint:TtlHours`.** Three things move together off that key (§6), including
the stop-request retention and the unservable-run terminal bound; a week-long value there would keep a
stopped run un-resumable for a week and delay every terminal failed done to five and a half days. Add a
second key — `Checkpoint:FailedRetentionHours`, default 168 — beside
`ConfigurationKeys.Checkpoint.TtlHours` (`ConfigurationKeys.cs:342-357`), wire it into the cleanup job's
data map next to `TtlHoursKey` (`DependencyInjection.cs:275-292`), and give the job a second cutoff
(`CheckpointCleanupJob.cs:42`). `DeleteExpiredAsync` (`CheckpointRepository.cs:441-449`) then needs
either a second parameter or a sibling method so the predicate becomes "`Failed` rows past the long
cutoff, everything except `ScheduledWait` and `Failed` past the short one". Preferred storage for the
deadline: an explicit nullable `retain_until_utc` column rather than deriving from `updated_at_utc` —
`updated_at_utc` is what §1 shows is refreshed by writes and what §6 shows the unservable bound is
measured from, and overloading it makes the two clocks interfere.
Note the interaction with `DeleteStoppedCheckpointsAsync` (`CheckpointRepository.cs:768-782`): it deletes
by stop-request join with no age or status test, so a retained row for a correlation that is later
stopped is removed on the next 30-minute tick. That is the correct precedence (a human ended the run) and
needs no change — but it should be stated in the decision, not discovered later.

**(d) Let an external caller ask for a resume of a named checkpoint.**
Files: `Hosts/Cymulate.IntegrationServiceBus.API/Controllers/EventsController.cs`, plus a request/response
pair under `Hosts/Cymulate.IntegrationServiceBus.API/Models/` alongside `StopRunRequest.cs`.
Nature: a new `POST api/v{version}/Events/resume`, built exactly like `StopRun` (`:988-1080`) —
correlation-id keyed body, `[FromServices] ICheckpointRepository`, 202 Accepted, fire-and-forget. It
holds no judgment: it looks the row up, and if a retained row exists it flips it back to resumable and
lets the existing machinery run. Two implementation choices, both already precedented:
- *Preferred*: a new CAS repository method modelled on `TransitionFromScheduledWaitAsync`
  (`CheckpointRepository.cs:694-730`) — `Failed → Idle`, clear the retention deadline, take the claim in
  the same statement — followed by a direct dispatch. This is the existing pattern for electing one
  resumer across replicas and never leaving a row owned by nobody
  (`ICheckpointRepository.cs:154-161`), and the dispatch it feeds already exists:
  `CheckpointRecoveryHandler.TryDispatchAsync(..., alreadyClaimed: true)`
  (`CheckpointRecoveryHandler.cs:170-171`) rehydrates the `PlatformEvent` from `platform_event_json`
  and sends `ProcessEventCommand` (`:249`, `:390`). §7 then does the rest with no change:
  `ExecuteWithResumeAsync` resumes on row presence, and the *collector* — not ISB — makes the final
  readability call via `CanResumeFrom` (`ProcessEventCommandHandler.cs:1165`).
- *Cheaper but weaker*: flip `Failed → Idle` and let the 5-minute sweep pick it up (§4). No dispatch
  code at all, at the cost of up to 5 minutes' latency and of the resume being indistinguishable from
  crash recovery in the logs.

**(e) Resolve a correlation id to a checkpoint key.**
File: `Domain/.../Interfaces/ICheckpointRepository.cs` + both implementations.
Nature: every servicing surface in §8 is correlation-id keyed, but the repository has no read that maps
a correlation id to the `(TenantId, CorrelationId, PlatformType, Category)` rows it names — only
`DeleteByCorrelationIdAsync` (`:49`) works that way, and `GetAllAsync` (`:60`) is a table scan. Add
`Task<IReadOnlyList<CheckpointEntry>> GetByCorrelationIdAsync(string correlationId, CancellationToken)`.
Without it the resume endpoint must make the caller supply tenant, platform and category, which no other
endpoint requires. **SPECULATION:** this query has no covering index — the unique index is
`(tenant_id, correlation_id, platform_type, category)`
(`CheckpointDbContext.cs:106-108`), and whether Postgres will use it for a `correlation_id`-only
predicate depends on the planner; I did not run `EXPLAIN`. If it does not, a single-column index on
`correlation_id` is a one-line migration.

**What is deliberately NOT in scope**, per the service-hub constraint: nothing here inspects
`result.ErrorCode`, counts attempts, or decides whether a failure is worth retrying. (a) records the
outcome the adapter reported; (d) services a request someone else made. The readability judgment stays
where it already is — `IResumableAdapter.CanResumeFrom` (`ProcessEventCommandHandler.cs:1165`) and
`ICheckpointStateCompatibility` (`CheckpointRecoveryHandler.cs:283`, `:477`) — both owned by the
collector.

**One thing the decision must settle before implementation** (an ambiguity, not a blocker): a run whose
checkpoint is retained still publishes its failed done today
(`CompleteExecutionAsync:819-823` → `PublishCompletionEventAsync` / `WriteToOutboxAsync`). If the
platform treats a failed done as terminal, a later operator-initiated resume publishes a *second* done
for the same correlation id. That is a wire-contract question for the backend, not an ISB decision — and
`CLAUDE.md`'s wire-contract rule ("adding an enum value, renaming a field, or changing a status string is
a coordinated change") means it has to be called out rather than assumed either way. The precedent is
that ISB already refuses to double-publish in the superseded case
(`ProcessEventCommandHandler.cs:261-269`).

---

## Contradictions found against stated beliefs

**1. `UnservableTerminalAge` is not `CheckpointTtl * 0.75`.** It is
`max(CheckpointTtl * 0.75, StaleClaimThreshold * 6)` (`CheckpointRecoveryHandler.cs:728-736`). At default
config (24h TTL, 10min stale threshold) the first term wins and the belief's 18h figure is right, so
nothing observable differs today — but the floor changes the answer under any deployment that lowers TTL
or raises the stale threshold, and the floor exists precisely so a raised stale threshold cannot end runs
early (`:721-726`). Reported because §6 asks what a TTL change would move, and under the belief's formula
the answer would be wrong for the non-default case.

**2. `CheckpointStatus.InFlight` is also dead, not just `Failed`.** The belief named only `Failed`. No
production code writes either (§3). This is a small expansion, and it matters: it means the enum has two
unused slots and the retained-state design can reuse one without an enum change.

**3. `GetRecoverableAsync` has a third filter the belief omitted: the tenant partition.**
`CheckpointPartition.OwnedByPartitionPredicate(tenantId)` (`CheckpointRepository.cs:672`,
`Domain/.../Models/CheckpointPartition.cs:25-30`), driven by the `TENANT_ID` env var
(`CheckpointRecoveryHandler.cs:56-57`). A retained row belonging to a dedicated tenant is only ever seen
by that tenant's pods. Relevant to any "resume a named checkpoint" design: the pod that services the
request may not be a pod that would ever sweep the row.

**4. `Checkpoint:TtlHours` drives one more thing than the belief implies.** It is not only the checkpoint
deletion horizon: the same cutoff deletes the **stop-request** rows in the same job
(`CheckpointCleanupJob.cs:73`), and it feeds the unservable-run terminal bound (§6). This is a genuine
trap for the retention design — raising that key to a week would keep every stopped correlation blocked
for a week.

**5. `CheckpointCleanupJob` deletes more than the TTL predicate.** `DeleteStoppedCheckpointsAsync`
(`CheckpointCleanupJob.cs:49`, `CheckpointRepository.cs:768-782`) removes every row whose correlation id
has a stop request, with **no age and no status condition** — `ScheduledWait` included. The belief framed
cleanup as one cutoff with one exclusion; there are two deletes, and the stop-aware one ignores both.

Beliefs **confirmed** without qualification: the failed-run delete chain
(`CompleteExecutionAsync:814 → FlushAndCleanupCheckpointAsync:1228 → DeleteAsync`, running regardless of
`result.Success`); `CheckpointStatus.Failed` never written in production; `DeleteExpiredAsync` excluding
only `ScheduledWait` under `Checkpoint:TtlHours` default 24; resume decided on checkpoint presence in
`ExecuteWithResumeAsync` with `RetryCount` logged but not consulted; `adapter_state` being a flat
`Dictionary<string,string>` (`AdapterExecutionContext.cs:992-996`,
`ProcessEventCommandHandler.cs:1698`, `:1721`, `CheckpointRecoveryHandler.cs:972`) serialized into an
existing `jsonb` column (`CheckpointDbContext.cs:70-71`) — so retaining it a week carries no
schema-migration cost for that field.

---

## Speculation (explicitly labelled)

- **SPECULATION:** whether Postgres will use
  `ix_adapter_checkpoints_tenant_correlation_platform_category`
  (`CheckpointDbContext.cs:106-108`) for a `correlation_id`-only predicate. `tenant_id` is the leading
  column, so a lookup by correlation id alone may not use it. I did not run `EXPLAIN` against a real
  database. If it does not, the change in §10(e) needs a single-column index.
- **SPECULATION:** the storage cost of a week's retention. I did not measure row counts or the typical
  size of `adapter_state_json` / `platform_event_json` in any environment. Both are `jsonb` and
  `platform_event_json` holds a whole serialized `PlatformEvent` including credentials-bearing
  structures (`ProcessEventCommandHandler.cs:134`), which is worth a look before committing to 7×
  retention.
- **SPECULATION:** how the platform/backend reacts to a second `AdapterDoneMessage` for a correlation id
  that already received a failed one. Nothing in this repo answers it — the consumer is another service.
  Flagged in §10 as the one thing to settle before implementation.
- **SPECULATION:** whether any operational tooling outside this repo reads `adapter_checkpoints.status`
  and would be surprised by `'Failed'` appearing in it for the first time. The column has no CHECK
  constraint, so nothing in this repo would reject it.

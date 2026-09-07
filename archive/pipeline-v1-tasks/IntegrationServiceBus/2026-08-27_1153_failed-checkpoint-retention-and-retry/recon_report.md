# Recon Report — Failed-Checkpoint Retention and Operator-Initiated Retry

Base ref: `b5e7ba22d0262be3406a5eef66425bae0a86dd47`. Four repos, read-only.
Worker sources: `research/isb-checkpoint-lifecycle.md` (W1),
`research/collector-resume-landscape.md` (W2), `research/infra-staleness-and-contracts.md` (W3),
`research/trigger-ownership.md` (W4).

## 0. Headline

**The feature splits into two phases with wildly different costs, and the split is not where it
was assumed to be.**

- **Phase 1 — retention + resume endpoint — is ISB-only and ships independently.** No package
  release, no cross-repo coordination, no consumer change. This was assumed to be blocked on a
  wire-contract release; it is not (§5).
- **Phase 2 — making a week-old checkpoint actually resumable — is a genuine three-repo change**
  costing 1 Infra overload + 24 mechanical collector edits + 16 collector artifact re-releases,
  and it is gated on a one-argument interface signature, not on the threshold constant (§3).

Phase 1 alone delivers retained, inspectable state and working resume for runs still inside their
collector's own age gate. That gate is **not uniformly 23h**: YAML vendors measure *total run age*
against a separate, runtime-overridable 24h bound (`YamlAdapter.cs:450`, `:88`), so a long-running
YAML collection can be refused while a same-age Falcon run is accepted. Phase 2 is separately
decidable and should be justified on evidence that an age gate is what is actually blocking real
retries.

## 1. ISB checkpoint lifecycle

- **Write.** `AdapterExecutionContext` upserts the row on page advance
  (`AdapterExecutionContext.cs:155`) and a final snapshot (`:925`); `adapter_state` is a flat
  `Dictionary<string,string>` (`:992-996`) into a `jsonb` column (`CheckpointDbContext.cs:70-71`).
  Retaining it costs no schema migration for that field.
- **Delete on failure — confirmed.** `CompleteExecutionAsync` (`ProcessEventCommandHandler.cs:805`)
  calls `FlushAndCleanupCheckpointAsync` at `:814` *before* it inspects `result`, reaching
  `DeleteAsync` at `:1228` regardless of `result.Success`. This is the single delete that must
  change. The other delete paths (adapter-not-found `:790`, unhandled exception `:880`,
  stop-requested) are correctly left alone.
- **`CheckpointStatus`.** Four values; **two are dead** — `Failed` *and* `InFlight` have no
  production write site. Only `ScheduledWait` is ever written. The column is plain `text` with no
  CHECK constraint (`20260526144503_*.cs:20-25`), so reusing `Failed` needs no enum or schema change.
- **Recovery selection.** `Status != ScheduledWait` **and** claim-staleness **and** a third filter
  omitted from every prior statement: a tenant partition,
  `CheckpointPartition.OwnedByPartitionPredicate(tenantId)` (`CheckpointRepository.cs:672`), driven
  by the `TENANT_ID` env var (`CheckpointRecoveryHandler.cs:56-57`). A retained row for a dedicated
  tenant is only visible to that tenant's pods.
- **Cleanup — two deletes, not one.** `DeleteExpiredAsync` (age cutoff, excludes only
  `ScheduledWait`) *and* `DeleteStoppedCheckpointsAsync` (`CheckpointRepository.cs:768-782`), which
  deletes by stop-request join with **no age and no status test** — `ScheduledWait` included.
- **Resume entry — confirmed.** `ExecuteWithResumeAsync` decides on checkpoint presence;
  `RetryCount` is logged, not consulted (`ProcessEventCommandHandler.cs:1116` and its remarks).

## 2. Collector resume landscape

- **Denominator: 18 of 18 collectors are resume-capable.** No have-nots. The ledger's prior claim
  that Tenable.io has no resume path is **refuted** — it has a full implementation
  (`TenableIoCollector.cs:34`, `Recovery/` helper + runner, correlated-format gate at
  `TenableIoCorrelatedCheckpoint.cs:81`). AgentService is not in this repo.
- **`CanResumeFrom` is NOT blob-only.** Three collectors read ISB's flat columns:
  - `YamlAdapter.cs:450` gates staleness on flat `CreatedAtUtc` — so for **every YAML vendor** the
    age gate measures total run age, not time since last progress.
  - `FalconCollector.cs:521` reads `CurrentPage`/`ProcessedItems`/`ProcessedFindings` and on that
    basis either throws (failing the run, causing the host to delete the row, `:513-517`) or records
    partial completion.
  - `DummyCollector.cs:249-255` returns true on `CurrentPage > 1` alone.
  The blob-only claim holds for the other 15.
- **Decline taxonomy exists but does not escape as a value.** `FalconResumeDecline`
  (`FalconCheckpointResumePolicy.cs:25-39`) separates damaged (NoFlow/UnknownFlow/LoadFailed) from
  merely-old (Stale); it and its façade overload are `internal`. Its *consequence* escapes as a
  thrown exception or a partial-completion done payload.
- **Vendor-handle dependence is per-flow.** Falcon **findings** persists no vendor cursor
  (`FalconCheckpointSerializer.cs:44-72`) and walks a frozen key list — but depends on the run's own
  S3 staging manifest, whose absence on a progressed checkpoint is a hard failure
  (`FalconFindingsFlow.cs:563-573`). Falcon **assets** *does* persist and resume onto a CrowdStrike
  `after` cursor (`FalconAssetsScrollRunner.cs:200`). A blanket "no cursors" claim is wrong.
- **`ICheckpointStateCompatibility`: zero implementations.** The ISB pre-claim probe is inert today.

## 3. Staleness policy and the real obstacle

- 23h at `RecoveryParsingHelper.cs:13`. **24 production call sites across 16 collectors, 0 overrides.**
- **The helper is not the obstacle:** `IsCheckpointStale(DateTime, TimeSpan? threshold = null)`
  (`:37`) already accepts an override.
- **The obstacle is the interface signature.** `IResumableAdapter.CanResumeFrom(AdapterCheckpoint)`
  takes one argument — no `PlatformEvent`, no context. ISB calls it at
  `ProcessEventCommandHandler.cs:1165`, *before* `ResumeAsync` (`:1179`), which does get the event.
  So metadata cannot carry a per-request bound without a breaking Client-package signature change
  across **18 implementors** — 8 declaring `public bool CanResumeFrom(...)` and 10 more as explicit
  interface implementations (`bool IResumableAdapter.CanResumeFrom(...)`: CloudGuard `:251`,
  ServiceNowCmdb `:252`, CortexXdr `:288`, MicrosoftEntraId `:254`, Taegis `:248`, DefenderForCloud
  `:250`, Qualys `:238`, Guardicore `:248`, SentinelOne `:255`, DefenderVm `:235`). A grep for the
  `public` form alone sees 8 and understates the blast radius by 125%; the explicit form is the
  canonical shape in this fleet.
- **One route needs no contract change:** `AdapterCheckpoint.AdapterState` is an open
  `IReadOnlyDictionary<string,string>` that ISB fully owns at `MapToAdapterCheckpoint`
  (`:1715-1737`). ISB can inject a bound as a state key. Cost: 1 Infra overload + 24 mechanical
  adapter edits + 16 collector re-releases + ISB. Order Infra → adapters → ISB; each half-landed
  state is inert.
- **YAML vendors already differ:** YamlAdapter has a *second* staleness mechanism with its own 24h
  default that **is** runtime-overridable — a precedent worth reading before designing.
- **Three unrelated things are called "stale threshold":** the 23h resume bound;
  ISB's `StaleClaimThresholdMinutes` (default 10, `ConfigurationKeys.cs:346-348`); and
  `Query:Recovery:StaleThresholdMinutes` (15/5, `:588-650`). Do not conflate them.

## 4. Trigger ownership and the retry path

- **Owner found.** `cymulate-integrations` →
  `apps/integration-server/.../collection-scheduler.service.ts:1094-1096` emits to `collectors.run`.
  `@Cron('*/10 * * * * *')` at `:67`, env-gated, a Mongo poller over `CybiClientIntegrationInstance`
  rows. No scheduler inside ISB.
- **correlationId is caller-supplied and durable** — `String(instance._id)`
  (`collectors-helpers.service.ts:213`). ISB only mints a fallback (`TriggerFlowMapper.cs:246-248`).
  **The backend can always name a checkpoint; the key is a column it owns.**
- **But every operator re-trigger mints a NEW correlationId**, because each creates a new Mongo
  instance doc — Admin Run Now (`:800`), EA run (`client-integration-instance.repository.ts:83`),
  scheduler next-occurrence (`:725`, explicit `delete newInstance._id`). **No existing re-trigger can
  reach a retained checkpoint.** The retry must be a new endpoint taking the original correlationId.
- **The template already exists.** `POST api/v1/Events/stop` (`EventsController.cs:979-1056`) is
  operator-initiated action on a caller-supplied correlationId — durable flag, fanout, repository
  call — and Admin already calls it resolving `String(instance._id)`
  (`exposureAnalyticsIntegrations.controller.js:882`). Same key, same repositories, opposite direction.

## 5. Wire contract — the assumed blocker is not one

- **`AdapterDoneMessage` is ISB source, not package surface:**
  `Domain/.../Messaging/AdapterDoneMessage.cs:10`. Absent from IntegrationInfra source and from both
  restored Client package DLLs. The both-directions drift check (R3) found no drift between Client
  1.2.0-preview.0 and source for these types.
- **Adding an optional field: ISB ships independently.** No pack, no publish, no version pin. The
  real consumer is on this machine — `connector-manager.controller.ts:29`
  (`@EventPattern('collectors.done')`), taking `@Payload() data: any` with no runtime validation, so
  it ignores unknown fields.
- **Capability already built and dropped — check before adding anything:**
  - `AdapterPartialDoneMessage` carries `scheduledResumeAtUtc`, `resumeAfterSeconds`, `waitReason`,
    `processedSoFar` (`AdapterPartialDoneMessage.cs:53-71`). Published to `collectors.partial-done`,
    **off by default**, comment says "no service consumes" (`AdapterEventReportingOptions.cs:12,20,22`).
  - `AdapterPartialCompletionMetadata` (`IntegrationInfra/.../AdapterPartialCompletionMetadata.cs:9`)
    has `watermarkUtc`, `completedThroughUtc`, `coverageKnown`, `reason`. **Falcon produces it today**
    (`FalconResilienceStrategyFactory.cs:45`) and the ISB-hosted path drops it — the only envelope
    with a slot (`Reporting/AdapterDonePayload.cs:25`) has no production caller.
- ISB emits a 4th status `"cancelled"` (`ProcessEventCommandHandler.cs:1971`) that its own doc
  comment (`:37`) omits and `AdapterRunStatus` cannot express; the JS consumer models all four.

## 6. Minimal ISB change under the service-hub constraint

Five changes, dependency-ordered. None inspects an error code, counts attempts, or judges
retryability. (a) records what the adapter reported; (d) services a request someone else made.

- **(a) Retain.** `ProcessEventCommandHandler.CompleteExecutionAsync` — pass the outcome into
  `FlushAndCleanupCheckpointAsync` and, on the non-success arm, stop before `DeleteAsync` (`:1228`),
  marking retained instead. The refusal early-return (`:1222-1223`) must stay ahead of it.
  **F2 — the unhandled-exception delete destroys the retained row on the retry attempt.** Falcon
  *deliberately throws* from `CanResumeFrom` when the host's flat counters show durable progress but
  the blob names no recognised flow; its own comment says so in terms — "This throw fails the run and
  the host then DELETES the checkpoint row" (`FalconCollector.cs:507-512`) — and ISB does exactly that
  at `ProcessEventCommandHandler.cs:880`. So an operator resume of a retained Falcon row with a
  damaged blob **destroys the checkpoint on the very attempt it was retained for**, leaving only a log
  line. Retention is not safe until this path is decided: either `:880` must not delete a row marked
  retained, or the collector must stop signalling unreadable state by throwing. This is the single
  most important unresolved item in this report.

- **(b) Hide from the sweep.** Reuse dead `CheckpointStatus.Failed` as the retained marker; widen the
  exclusion to `Status != ScheduledWait && Status != Failed` in both stores
  (`CheckpointRepository.cs:670`, `InMemoryCheckpointRepository.cs:272`). Writer is a surgical
  owner-guarded transition modelled on `TransitionToScheduledWaitAsync` (`:732-762`) so the flushed
  page/cursor/state survive the flip. `UpsertAsync`'s `DO UPDATE` guard (`:97`) must exclude `Failed`
  so a late write cannot un-retire a row. `Handle`'s early guard (`:111-117`) reads only
  `ScheduledWait` and must also cover `Failed`.
- **(c) Separate clock. DO NOT raise `Checkpoint:TtlHours`** — three things move off that key,
  including stop-request retention (`CheckpointCleanupJob.cs:73`) and the unservable terminal bound.
  A week there would keep stopped runs blocked for a week and delay terminal failed dones to 5.5 days.
  Add `Checkpoint:FailedRetentionHours` (default 168) and a second cutoff in the cleanup job. Prefer
  an explicit nullable `retain_until_utc` column over overloading `updated_at_utc`, which writes
  refresh and the unservable bound measures from.
- **(d) Resume endpoint.** `POST api/v{version}/Events/resume`, built exactly like `StopRun`
  (`:988-1080`). Preferred: a CAS method modelled on `TransitionFromScheduledWaitAsync` (`:694-730`)
  flipping `Failed → Idle` and taking the claim in one statement, then dispatching via the existing
  `CheckpointRecoveryHandler.TryDispatchAsync(..., alreadyClaimed: true)` (`:170-171`), which already
  rehydrates the `PlatformEvent` from `platform_event_json`.
- **(e) A read that does not exist.** No repository method maps a correlation id to its checkpoint
  rows — only `DeleteByCorrelationIdAsync` and a table-scanning `GetAllAsync`. Add
  `GetByCorrelationIdAsync`. **SPECULATION:** the unique index is
  `(tenant_id, correlation_id, platform_type, category)`; whether the planner uses it for a
  correlation-id-only predicate was not tested with `EXPLAIN`.

## 7. Per-repo obligation split and sequencing

| phase | repo | obligation | ships independently? |
|---|---|---|---|
| 1 | IntegrationServiceBus | (a)–(e) above | **yes** — no package, no consumer change |
| 1 | cymulate-integrations | call the new resume endpoint from Admin/EA, passing the original `instance._id` | yes, after ISB |
| 2 | IntegrationInfra | `IsCheckpointStale` overload / bound-carrying convention | first |
| 2 | cymulate-integration-adapters | 24 mechanical call-site edits across 16 collectors | after Infra |
| 2 | cymulate-integration-adapters | 16 collector artifact re-releases | after edits |
| 2 | IntegrationServiceBus | inject the bound at `MapToAdapterCheckpoint` (`:1715-1737`) | last |

Phase 2's half-landed states are inert, so it can land incrementally.

## 8. Open questions the decision must settle

1. **Double done.** A retained run still publishes its failed done
   (`CompleteExecutionAsync:819-823`). A later resume publishes a second done for the same
   correlation id. The JS consumer is tolerant (`any`, no validation), but the *semantics* are a
   backend question. Precedent: ISB already refuses to double-publish when superseded (`:261-269`).
2. **Stop precedence.** `DeleteStoppedCheckpointsAsync` removes a retained row on the next 30-minute
   tick if the correlation is later stopped. W1 judges this correct (a human ended the run) and
   needing no change — but it should be decided, not discovered.
3. **Is the 23h gate actually what blocks real retries?** W3's caution: the bound's documented
   rationale is vendor cursor/export expiry, and lengthening it does not make an expired vendor
   export resumable. Confirm from the "is stale" warnings at the 24 sites before paying phase 2's cost.
4. **Tenant partition.** A resume request may land on a pod that would never sweep that row
   (`CheckpointRecoveryHandler.cs:56-57`). The endpoint's dispatch path must account for it.
5. **Does the Falcon staging manifest survive a week? (unknown, and it decides the flagship case.)**
   The findings flow persists no vendor cursor but depends on the run's own S3 staging manifest, whose
   absence on a progressed checkpoint is a hard failure (`FalconFindingsFlow.cs:563-573`). W2 could not
   determine the S3 lifecycle policy on that prefix and labelled it `SPECULATION:`. If those objects
   expire before a week, the retained checkpoint is unresumable regardless of every other change in
   this report. **Check the bucket lifecycle rule before committing to a one-week retention figure.**
6. **The delete-on-throw collision (F2 above)** — restated here because it blocks Phase 1, not Phase 2.

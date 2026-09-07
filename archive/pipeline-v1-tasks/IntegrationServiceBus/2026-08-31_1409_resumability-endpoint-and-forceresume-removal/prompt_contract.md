# Prompt Contract

## Role

You are a senior engineer working across three Cymulate repositories: IntegrationServiceBus (C#/.NET),
Admin (Node/Fastify + EJS), and cymulate-integrations (NestJS/TypeScript).

## Goal

Make the Resume affordance and the resume execution decision answer to one predicate, and delete the
`forceResume` marker.

- Admin's Resume button is drawn from an authoritative ISB answer, not a Mongo heuristic.
- ISB gate 4 resolves resume-vs-fresh from that same evaluator, in-process.
- `forceResume` no longer exists in any of the three repositories.

## Context

`correlationId` is `String(instance._id)` — the Mongo ObjectId of the collection-run instance. It is
the join key across all three repos and the only key Admin can construct.

Today Admin decides button visibility client-side from three inputs: instance status, a page-frozen
`connectionMode` boolean, and a hardcoded `RESUME_RETENTION_DAYS = 7` compared against `endedAt`.
ISB independently decides resume-vs-fresh in gate 4 of `ExecuteWithResumeAsync`:
`if (forceResume || resumable.CanResumeFrom(adapterCheckpoint))`. The two are unrelated code and
disagree — ISB's retention horizon is a runtime config key clamped 1-30 days, and for YAML vendors
the effective window is the adapter's own `_maxCheckpointAge`, not either number.

`forceResume`'s only function is overriding an adapter declination. When the adapter agrees it is a
no-op. It is a bare root-level JSON marker read by `AdapterRunMessage.ReadForceResume`, deliberately
not a bound contract property, and deliberately kept out of `PlatformEvent.Metadata` because the
recovery sweep rehydrates that blob and would replay the marker indefinitely.

Removing it is safe **only** because the evaluator includes `CanResumeFrom`. Without that, a declined
checkpoint would render a button, the click would hit gate 5, the refusal would re-mark the row
`Failed` with `updated_at_utc = now`, and the 7-day retention clock would restart — the condition
that renders the button renewing itself indefinitely.

## Constraints

See `constraints.md`. The load-bearing ones:

* Correlation-only lookup. Admin cannot compute `platform_type` or `category`.
* `clientId` is unindexed jsonb — post-filter over correlation-matched rows, never a WHERE clause.
* The evaluator passes the **full mapped `AdapterCheckpoint`**; `CanResumeFrom` reads flat columns and is time-dependent.
* The endpoint is batch. Admin renders unbounded rows and refreshes every 60 seconds.
* ISB never HTTP-calls itself.
* No new Infrastructure reference from an application project.
* New ISB code names its types — no `var`. Write far fewer comments than feel warranted.
* Do not commit, push, or open PRs.

## Scope — eight items, do not expand

1. **ISB evaluator.** Application layer, over `ICheckpointRepository` plus the adapter registry. Returns `resumable`, `reason` (`NO_CHECKPOINT`, `NO_PROGRESS`, `ADAPTER_DECLINED`, `IN_FLIGHT`, `STOPPED`), `page`, `checkpointAgeHours`. Inputs: the checkpoint row (present, `HoldsResumableProgress`, not claimed, not stopped) and the collector's `CanResumeFrom` verdict.
2. **ISB endpoint.** Batch, in `EventsController`, accepting a list of correlationIds. CorrelationId resolution and DI style from `POST stop` (`EventsController.cs:994, :997, :1057`); response shape from `GET journal/{correlationId}` (`:1247`). Proposed `POST /api/v1/Events/resumability`.
3. **ISB gate 4.** Point `ExecuteWithResumeAsync` (~`:1281`) at the evaluator in-process.
4. **ISB deletion.** Remove `AdapterRunMessage.ReadForceResume`, `ProcessEventCommand.ForceResume`, and the three call sites — `EventsController.cs:601, :611, :783, :785`; `IsbPlatformEventDispatcher.cs:200, :228`.
5. **ISB gate 5.** Keep it. Stop renewing the retention clock on that path — `MarkFailedAsync` sets `updated_at_utc = now`, so a decline currently pushes the 7-day expiry out.
6. **ISB index.** Index `correlation_id`. The unique index leads with `tenant_id`, so a correlation-only batch lookup is a sequential scan today.
7. **Admin.** Replace `canResumeFailed` (`exposure_analytics_client_integration_details.ejs:714-728` and the status-cell renderer ~`:1170`) with the endpoint result. Delete `RESUME_RETENTION_DAYS` and `RESUME_IS_DIRECT`. Keep the nine server-side refusals (`exposureAnalyticsIntegrations.controller.js:1080-1131`). Reuse the existing ISB HTTP call pattern and `process.cymulate.config.serviceBusInternalServerURL` from `controller.js:1023-1043`.
8. **cymulate-integrations.** Fresh branch off `master`. Carry only: the cancel-sweep guard `startedAt: { $gt: new Date() }` replacing `forceResume: { $ne: true }` in `cancelPendingInstances` (`connector-manager.service.ts:456-466`), and the `alreadyScheduled` slot-idempotency check (`collection-scheduler.service.ts:815-825`). Introduce `forceResume` nowhere.

## Blocking prerequisite

**S0 blocks S1-S8.** Confirm whether `ResumeAsync` seeds its progress counter from
`AdapterCheckpoint.CurrentSequenceId`. `AdapterProgressContext.NextSequenceId()` is defined in the
`Cymulate.Integration.Client` package, outside all three repos; per-vendor `ResumeAsync`
implementations are also outside. ISB's half is already correct — `MapToAdapterCheckpoint` sets
`CurrentSequenceId` from `entry.SequenceId` (`ProcessEventCommandHandler.cs:1898`).

The BE drops any progress message at or below the failed run's high-water mark
(`batchingStats.lastSequenceId: {$lt: sequenceId}`, `connector-manager.service.ts:180-186`). If any
adapter builds a fresh progress context on resume, progress silently freezes until it climbs past the
old mark. Route to `technical-researcher` if it cannot be settled from package source on disk.

## Success Criteria

* An ISB evaluator exists in the application layer, is called by both the endpoint and gate 4, and has no duplicate of its predicate anywhere.
* The batch endpoint returns a per-correlationId verdict with a reason, and answers correlation-only.
* `grep -ri forceresume` returns nothing in IntegrationServiceBus `src/`, nothing in Admin, and nothing in cymulate-integrations.
* Admin's Resume button visibility comes from the endpoint. `RESUME_RETENTION_DAYS` and `RESUME_IS_DIRECT` are gone. The nine server-side refusals remain.
* A declined checkpoint renders no button, and a refusal does not push the retention expiry forward.
* `correlation_id` is indexed.
* cymulate-integrations' branch is off `master`, contains the `startedAt` cancel-sweep guard and the slot-idempotency check, and introduces `forceResume` nowhere.
* S0 is answered with a citation, or recorded as NEVER-TESTED with its consequence stated.
* The design documentation exists in this task directory: three-repo current state, why `forceResume` is removable, the shared-evaluator design, the livelock it prevents.
* ISB solution builds; ISB unit tests run. Known-good exceptions per `CLAUDE.md`: `NU1900` on every project, `DockerUnavailableException` when Docker is down, one pre-existing `Application.UnitTests` failure.

## Execution Rules

* Do not assume missing data. Respect constraints strictly.
* Diagnose before changing. If a fix causes more errors than it resolves, revert and report.
* Do not re-litigate the design. It is signed off.
* Do not build or test after every edit — build once at the end of a repo's changes.
* Stop and surface if S0 cannot be answered, if a constraint would have to be violated, or if the evaluator cannot reach the adapter registry without a new Infrastructure reference.

## Output Format

* Code changes in the three repositories, uncommitted.
* `execution_notes.md` appended as work lands.
* Design documentation written into this task directory.
* A final summary naming what changed per repo, what was left out, and any accepted risk.

## Stop Conditions

* Goal achieved and success criteria met.
* S0 unanswerable from source and research.
* A required change would violate a signed-off constraint.
* Required data missing.

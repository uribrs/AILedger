# Constraints

## Identity and keys

* `correlationId` is `String(instance._id)`, the Mongo ObjectId of the collection-run instance — `collectors-helpers.service.ts:324`. It is the only join key all three repos share.
* Admin cannot compute `platform_type` or `category`. Any lookup it makes must be correlation-only.
* The checkpoint table is Postgres `adapter_checkpoints`, `UNIQUE (tenant_id, correlation_id, platform_type, category)`.
* `tenant_id` is a pod partition from the `TENANT_ID` env var, usually `""`. It is not the customer.
* `clientId` exists only inside the `platform_event_json` jsonb blob and is not indexed. Any clientId check must be a post-filter over correlation-matched rows, never a WHERE clause.
* Correlation-only lookup has precedent: `DeleteByCorrelationIdAsync` is a bare `Where(c => c.CorrelationId == correlationId)` — `CheckpointRepository.cs:512-523`.

## Layering

* `Application.Query` and the application projects must not gain a new Infrastructure project reference. Declare ports in the application layer and implement outward.
* ISB must never HTTP-call itself. Gate 4 calls the evaluator in-process.
* New ISB code names its types. No `var`.
* Write far fewer comments than feel warranted. Rationale belongs in the PR description, not the file.

## The evaluator's inputs

* `CanResumeFrom` is time-dependent. `YamlAdapter.CanResumeFrom` compares `DateTime.UtcNow - checkpoint.CreatedAtUtc` against `_maxCheckpointAge` — `YamlAdapter.cs:453-462`. This is decisive for every YAML vendor.
* Therefore the evaluator must pass the **full mapped `AdapterCheckpoint`**, flat columns included — not just the `adapter_state` blob. `MapToAdapterCheckpoint` already does this (`ProcessEventCommandHandler.cs:1898`).
* The collector fleet is not uniform: 26 `CanResumeFrom` implementations, 16 files referencing `IsCheckpointStale`. Do not assume one shape.
* Therefore the render-time answer decays with wall-clock time and is **advisory**; only the dispatch-time answer is authoritative. The design must tolerate divergence, not prevent it.

## Batch shape

* `GET .../instances` in Admin returns every instance of every flow with no `.limit()` — `controller.js:424`.
* The instances table auto-refreshes every 60 seconds and re-renders every row, not just the visible ten.
* The endpoint must therefore be batch. A single-correlationId endpoint is the wrong shape.

## Must not change

* Gate 5 (`RESUME_DECLINED_RETAINED_CHECKPOINT`, `ProcessEventCommandHandler.cs:1324`) stays. `storageUrl` derives from `instance.id`, which the revive does not change, so a silent fall-through to a fresh collection would write a full result set into an S3 prefix already holding a partial one.
* The nine server-side refusals in `exposureAnalyticsIntegrations.controller.js:1080-1131` stay. They guard the write.
* `forceResume` must not be introduced anywhere in cymulate-integrations — not the envelope spread (`collectors-helpers.service.ts:330`), not the clone strip (`collection-scheduler.service.ts:843`), not the scheduler log line.

## Branches and working agreement

* ISB: branch off `dev`. PR targets `dev`.
* Admin: branch off `origin/master`. Admin has no `dev` branch.
* cymulate-integrations: fresh branch off `master`. The existing `feat/operator-resume-failed-collection` (007c487f) was falsely merged — information only, never a base.
* Do not commit. Do not push. Do not open PRs.

## Out of scope

* Admin's `?tid=` query parameter not reaching ISB's `TenantIdMiddleware` (header-only, no query fallback). Record it; do not fix it here.
* ISB has no authentication anywhere in `src/`; `UseAuthentication` is commented out at `Startup.cs:253`. The new read endpoint inherits that posture. Deliberate acceptance, not a fix target.

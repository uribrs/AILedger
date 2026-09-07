# Assumptions

All entries are OPEN. This skill holds no evidence and may not write any other status.

## Prior Art

Tags: `integration-service-bus`, `checkpoint-resume`, `falcon`, `cursor-expiry`, `sdk-client`.
Matched 7 relevant rows, none superseded or retracted; all triaged (under the cap of 10).

- OPEN — An expired vendor cursor surfaces as a classifiable HTTP failure, so cursor expiry is
  observable to the collector. source: lessons.md#L-882455c1 (researcher, 2026-08-18)
- OPEN — The Falcon findings flow reaches OnTerminalSnapshotWithoutPublishedPage when a Phase 2
  deferral snapshots its position without advancing the page. source: lessons.md#L-dae004f6
  (verifier, 2026-08-26)
- OPEN — `Cymulate.Integration.Client` is a superset of the SDK and only adds features, so a
  contract change there is additive. source: lessons.md#L-96bb76cc (executor, 2026-08-02)
- OPEN — A collector named in this work has a checkpoint/resume path at all. Two prior rows
  found collectors that have none (Tenable.io, AgentService). source: lessons.md#L-57acd253
  (verifier, 2026-08-19), lessons.md#L-1e94a68a (researcher, 2026-06-12)

## Task assumptions

- A1 OPEN — A failed collector run currently deletes its checkpoint: `CompleteExecutionAsync`
  runs regardless of `result.Success` and reaches `FlushAndCleanupCheckpointAsync` →
  `DeleteAsync`.
- A2 OPEN — `CheckpointStatus.Failed` exists in the Domain enum and is written by no
  production code path.
- A3 OPEN — `GetRecoverableAsync` filters only on `Status != ScheduledWait` plus claim
  staleness, so a retained unclaimed row would be re-dispatched by the recovery sweep.
- A4 OPEN — `DeleteExpiredAsync` applies a single cutoff (`Checkpoint:TtlHours`, default 24)
  to every status except `ScheduledWait`.
- A5 OPEN — `UnservableTerminalAge` derives from `CheckpointTtl * 0.75`, so raising
  `Checkpoint:TtlHours` also moves the recovery sweep's give-up bound.
- A6 OPEN — Resume is decided on checkpoint presence in `ExecuteWithResumeAsync`, not on
  `RetryCount` or on how the run was triggered.
- A7 OPEN — Collector `CanResumeFrom` consumes only the `adapter_state` blob
  (`IReadOnlyDictionary<string,string>`) and never ISB's flat columns.
- A8 OPEN — `RecoveryParsingHelper.DefaultStaleThreshold` is 23h in IntegrationInfra and every
  collector call site uses the default with no override, so a week-old checkpoint is declined
  fleet-wide and silently restarts fresh.
- A9 OPEN — `ICheckpointStateCompatibility` has zero implementations in the adapters repo, so
  the pre-claim probe is inert today.
- A10 OPEN — `FalconResumeDecline` distinguishes a damaged blob (NoFlow / UnknownFlow /
  LoadFailed) from a merely-old one (Stale), and this taxonomy is internal to Falcon.
- A11 OPEN — The checkpoint-helper pattern (`*CheckpointHelper` + staleness test) is uniform
  across collectors, so a per-request staleness bound has one shape to change.
- A12 OPEN — A backend repo present on this machine owns the collection trigger and would own
  the retry request.
- A13 OPEN — `AdapterDoneMessage` is the failure-reporting wire contract the backend consumes,
  and it lives in `Cymulate.Integration.Client` outside this repo.
- A14 OPEN — ISB can service a resume request without classifying failures, i.e. the
  service-hub constraint is satisfiable with retention plus a trigger and no new judgment.
- A15 OPEN — `adapter_state` is written and read as a flat string dictionary, so retaining it
  for a week carries no schema-migration cost within ISB.


---

# Disposition (verifier-1, 2026-08-27)

Terminal. Written by the verifier pass; no other skill may rewrite these.
Full citations in `review/verifier-1.md` §2.

| id | status | actor |
|----|--------|-------|
| A1 | VALIDATED | verifier |
| A2 | VALIDATED (broader — `InFlight` is dead too) | verifier |
| A3 | VALIDATED as to consequence; "only" is wrong — a third tenant-partition filter exists | verifier |
| A4 | VALIDATED but incomplete — the same job also deletes stopped rows with no age/status test | verifier |
| A5 | **REJECTED** — it is `max(CheckpointTtl*0.75, StaleClaimThreshold*6)`, not a plain 0.75 multiple | verifier |
| A6 | VALIDATED | verifier |
| A7 | **REJECTED** — YamlAdapter, Falcon and DummyCollector all read ISB's flat columns | verifier |
| A8 | VALIDATED as to the constant and 24/0 counts; "silently restarts fresh" is not universal | verifier |
| A9 | VALIDATED — one hit, and it is a comment | verifier |
| A10 | VALIDATED | recon |
| A11 | **REJECTED** — 7 deviations; editing the helper layer misses every YAML vendor | verifier |
| A12 | VALIDATED | verifier |
| A13 | **REJECTED as to location** — `AdapterDoneMessage` is ISB source, not the Client package; consumption half validated | verifier |
| A14 | VALIDATED as a property of today's code; the designed endpoint is unbuilt | verifier |
| A15 | VALIDATED for that field only; §6(c) still proposes a new column | verifier |
| L-882455c1 | VALIDATED narrowly (Falcon assets only); other vendors not evidenced | verifier |
| L-dae004f6 | NEVER-TESTED — symbol never examined; out of the recon's path | verifier |
| L-96bb76cc | NEVER-TESTED — type-set diff found no drift, but did not reproduce the ledger's file-level premise | verifier |
| L-57acd253 / L-1e94a68a | **REJECTED** for Tenable.io (it has a full resume path); NEVER-TESTED for AgentService (absent from the repo) | verifier |

## Must not be re-assumed without new evidence

- That ISB's flat columns are inert bookkeeping the collector ignores (A7).
- That editing the shared checkpoint helper reaches the whole fleet (A11).
- That adding a field to `AdapterDoneMessage` costs a package release (A13).
- That `UnservableTerminalAge` is a plain 0.75 multiple of the TTL (A5).
- That Tenable.io lacks a resume path (L-57acd253).
- That a retained row is visible to every pod (A3).

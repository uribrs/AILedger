# Constraints

- ISB-only. No change to IntegrationInfra, cymulate-integration-adapters, or any backend repo.
- No `CheckpointStatus` enum value is added, and no existing value gains a new writer. Retention is
  expressed solely by `retain_until_utc` being non-null.
- Do not raise `Checkpoint:TtlHours`. It also bounds stop-request deletion and feeds
  `UnservableTerminalAge`; the retention clock is a separate key.
- ISB is a service hub, not a decision hub: record what the adapter reported. No error-code
  inspection, no attempt counting, no judgement about whether a run is worth retrying.
- The retention stamp is written only where the adapter actually ran and returned a failing result.
  Adapter-not-found and stop-requested keep deleting unchanged.
- Both `ICheckpointRepository` implementations stay behaviourally identical — every predicate change
  lands in Postgres and InMemory together.
- New code names its types. No `var`.
- Far fewer comments than feel warranted. No `<remarks>` narration.
- Do not commit, push, or open a PR.
- Build and test once at the end, not per edit.
- Migration follows the existing `Infrastructure.Postgres/Migrations/` naming and is additive only —
  a nullable column, no backfill, no data movement.

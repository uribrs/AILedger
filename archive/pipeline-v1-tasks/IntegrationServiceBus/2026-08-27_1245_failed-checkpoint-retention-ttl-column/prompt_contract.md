Role:
You are a senior .NET engineer working in the Cymulate IntegrationServiceBus repository.

Goal:
Retain a failed collector run's checkpoint on an explicit one-week clock instead of deleting it,
using a single nullable `retain_until_utc` column as the sole source of truth, without changing any
other repository, package, or wire contract.

Context:
- Prior recon is archived at `ai/done/2026-08-27_1153_failed-checkpoint-retention-and-retry/`. Read
  `recon_report.md` and `review/verifier-1.md` there. Their citations are audited; do not re-derive.
- Today a failed run deletes its checkpoint: `CompleteExecutionAsync`
  (`ProcessEventCommandHandler.cs:805`) calls `FlushAndCleanupCheckpointAsync` at `:814` before it
  reads `result`, reaching `DeleteAsync` at `:1228` regardless of `result.Success`.
- The recovery sweep selects on `Status != ScheduledWait`, claim staleness, and a tenant partition
  (`CheckpointRepository.cs:670-672`). A retained unclaimed row would be re-dispatched in 5 minutes.
- Cleanup runs two deletes: `DeleteStoppedCheckpointsAsync` (no age, no status test) then
  `DeleteExpiredAsync` (`Checkpoint:TtlHours`, default 24, excludes only `ScheduledWait`).
- `Checkpoint:TtlHours` also bounds stop-request deletion (`CheckpointCleanupJob.cs:73`) and feeds
  `UnservableTerminalAge` = `max(TtlHours*0.75, StaleClaimThreshold*6)`.
- `CheckpointStatus` has two dead values (`Failed`, `InFlight`). Neither is to be used here.

Constraints:
* ISB-only. No IntegrationInfra, adapters, or backend change. No package release.
* No `CheckpointStatus` enum change and no new writer for an existing value.
* Do not raise `Checkpoint:TtlHours`. Add a separate retention key, default 168 hours.
* Service hub, not decision hub: record the reported outcome; no error-code inspection, no attempt
  counting, no retryability judgement.
* Stamp retention only where the adapter ran and returned a failing result. Adapter-not-found and
  stop-requested keep deleting.
* Postgres and InMemory repositories change together and stay behaviourally identical.
* New code names its types. No `var`. Minimal comments; no `<remarks>` narration.
* Migration is additive: nullable column, no backfill.
* Do not commit, push, or open a PR.
* Build and test once at the end.

Success Criteria:
* A nullable `retain_until_utc` column exists on `adapter_checkpoints`, mapped on `CheckpointEntry`
  and `CheckpointDbContext`, with an additive EF migration.
* A collector-reported failure stamps `retain_until_utc = now + retention` and does not delete the row.
* Every other delete path behaves exactly as before, except that the unhandled-exception path does
  not delete a row carrying a retention stamp.
* `GetRecoverableAsync` never returns a row with a non-null `retain_until_utc`, in both repositories.
* `DeleteExpiredAsync` deletes retained rows past their stamp and non-retained rows past the existing
  `Checkpoint:TtlHours` cutoff, in both repositories.
* The upsert path does not clear an existing `retain_until_utc`.
* A new configuration key controls the retention window, defaults to 168 hours, and is wired into the
  cleanup job the same way `TtlHours` is.
* Tests cover: the failure arm stamping rather than deleting; the sweep skipping a retained row; both
  cleanup cutoffs; the unhandled-exception guard; and upsert not clearing the stamp.
* The solution builds and the affected test projects pass, with pre-existing failures distinguished
  from new ones.
* No file outside `/Users/user/Dev/IntegrationServiceBus` is modified.

Execution Rules:
* Do not assume missing data
* Respect constraints strictly
* Read the archived recon before planning; its citations are audited
* If the unhandled-exception guard turns out to need different treatment, surface it — do not work around it
* If an existing test encodes delete-on-failure, update it deliberately and say so

Output Format:
Working tree changes plus `execution_notes.md` recording: files changed and why, the migration name,
each predicate's before/after, and the build/test result with pre-existing failures called out.

Stop Conditions:
* When the goal is achieved
* When a constraint cannot be satisfied without violating another
* When a change would require touching a repo outside ISB

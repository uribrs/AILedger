Role:
You are a senior .NET engineer working in the Cymulate IntegrationServiceBus repository.

Goal:
Stop deleting a collector-reported failed run's checkpoint. Mark the row terminal instead, release its
claim, strip its credentials, hide it from the recovery sweep, and let the cleanup job delete it once
it is older than the retention window. No new column, no migration.

Context:
- The working tree is clean at `d53e347d`. A previous attempt at this feature was rejected as
  over-built and reverted; a patch of it is at
  `/private/tmp/claude-501/-Users-user-Dev-IntegrationServiceBus/bf1a2369-ae81-459c-bafb-7d13557a3cec/scratchpad/retain-until-utc-column-version.patch`
  for reference only. **Do not re-apply it.**
- Audited prior recon: `ai/done/2026-08-27_1153_failed-checkpoint-retention-and-retry/recon_report.md`.
  The rejected attempt's reviews are at `ai/done/2026-08-27_1245_failed-checkpoint-retention-ttl-column/review/`
  and their findings on the claim release, the sweep-vs-cleanup tick race and the credential exposure
  still apply. Read these; do not re-derive them.
- Today `CompleteExecutionAsync` calls `FlushAndCleanupCheckpointAsync` before it reads `result`,
  reaching `DeleteAsync` regardless of success.
- `CheckpointStatus` has `InFlight`, `Idle`, `ScheduledWait`, `Failed`. Only `ScheduledWait` is ever
  written. The column is plain text, no CHECK constraint.
- `GetRecoverableAsync` selects on status, claim staleness and a tenant partition.
- `CheckpointCleanupJob` runs `DeleteStoppedCheckpointsAsync` first, then `DeleteExpiredAsync`, and
  also deletes stop-request rows off `Checkpoint:TtlHours`.
- Recovery sweeps every 5 minutes; cleanup every 30.

Constraints:
* ISB-only; no other repo, no package release.
* No new column, no migration, no new `CheckpointStatus` member.
* Service hub, not decision hub: no error-code inspection, no attempt counting, no retryability judgement.
* Key on the reported terminal status, never `!result.Success`.
* The refused-write early return keeps precedence over the new branch.
* `HandleAdapterNotFoundAsync` keeps deleting unconditionally.
* Claim release and credential strip happen in the same write as the status change.
* Derive the credentials JSON key from `nameof`, never a literal.
* Both repository implementations change together and stay behaviourally identical.
* `Checkpoint:TtlHours` keeps deleting stop-request rows unchanged.
* Names types, no `var`; minimal comments; no commit, push or PR; build and test once at the end.

Success Criteria:
* A collector-reported failed done marks the row `Failed`, releases the claim, strips credentials, and
  does not delete the row.
* Every other outcome deletes exactly as before — success, skipped, cancelled, adapter-not-found,
  unhandled exception, stop-requested.
* `GetRecoverableAsync` never returns a `Failed` row, in both stores.
* `DeleteExpiredAsync` deletes a `Failed` row older than the retention window and leaves everything
  else alone, in both stores.
* Stop-request row deletion is unchanged.
* The retention window is configurable with a sane default and is clamped against a nonsensical value.
* Tests cover: the status write and its two side effects; each non-failure outcome still deleting;
  the sweep exclusion; the cleanup predicate; stop still winning; and that the stripped payload keeps
  everything except credentials.
* Solution builds; affected test projects pass, with Docker-gated tests reported as not-run rather
  than green.
* No file outside `/Users/user/Dev/IntegrationServiceBus` is modified, and no new migration exists.

Execution Rules:
* Do not assume missing data
* Respect constraints strictly
* Read the prior artifacts before planning; their citations are audited
* Surface, do not work around, any contradiction with the settled design
* If an existing test encodes delete-on-failure, update it deliberately and say so

Output Format:
Working tree changes plus `execution_notes.md` recording files changed, each predicate before/after,
the credential-key derivation, and build/test results with Docker-gated projects called out.

Stop Conditions:
* When the goal is achieved
* When a constraint cannot be satisfied without violating another
* When the change would require a migration or a new column

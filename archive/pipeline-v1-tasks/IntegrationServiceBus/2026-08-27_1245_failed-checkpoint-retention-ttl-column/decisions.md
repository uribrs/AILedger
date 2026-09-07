# Decisions

- Retention is expressed by one nullable column, not by a status marker. The operator replaced an
  earlier status+column proposal with this; one field cannot disagree with itself.
- `retain_until_utc` is stamped as an absolute deadline at failure time, not derived from
  `updated_at_utc`. Writes refresh `updated_at_utc` and `UnservableTerminalAge` measures from it, so
  overloading it makes two clocks interfere.
- The retention window is configurable under a new key defaulting to 168 hours, so operations can
  shorten it without a deploy.
- The unhandled-exception delete is guarded in this change rather than deferred, because a collector
  that signals unreadable state by throwing would otherwise destroy the row on the retry attempt.
- Proceeding on unverified: stamping retention on the failure arm of `CompleteExecutionAsync` reaches
  every collector-reported failure and no other outcome. If wrong: some failures retain nothing, or a
  non-failure outcome retains a row that should have been deleted.
- Proceeding on unverified: no existing query or index assumes `adapter_checkpoints` has no nullable
  timestamp beyond `claimed_at_utc` / `scheduled_resume_at_utc`. If wrong: a predicate elsewhere
  silently changes meaning.

## Drift recorded at planning time (recon-driven)

- **Superseded:** the contract's "new key wired into the cleanup job the same way `TtlHours` is."
  Recon showed the retention deadline is stamped absolutely at failure time, so the job only
  compares — `CheckpointCleanupJob.cs` and `DependencyInjection.cs` are untouched. Key becomes
  `Checkpoint:FailedRetentionDays` (default 7), read in the Application layer through the
  `IConfiguration` already injected at `ProcessEventCommandHandler.cs:34`. Simpler, and it removes
  a second place the horizon could be configured inconsistently.
- **Clarified:** `:880` (unhandled exception) stamps nothing — an exception is not the collector
  reporting a failed status. It only gains a guard so it cannot delete an already-stamped row.
- **Added to scope:** the two doc comments at `CheckpointRecoveryHandler.cs:692-698` and
  `CheckpointCleanupJob.cs:21-25` assert the cleanup and give-up horizons are one number, which stops
  being true for retained rows. Updated in this change rather than left knowingly stale.
- **Planned drift, mid-execution:** `DeleteExpiredAsync`'s frozen ternary predicate is superseded by
  the equivalent OR form. W1 confirmed EF translates the ternary to
  `CASE WHEN retain_until_utc IS NULL THEN updated_at_utc < @cutoff ELSE retain_until_utc <= @now END`,
  which cannot use `ix_adapter_checkpoints_updated_at_utc`. Since this feature makes the table retain
  rows it previously deleted, pessimising the cleanup scan is the wrong trade for form-fidelity.
  The OR form is semantically identical and indexable on the common path.
- **Approved beyond original scope:** two InMemory guards W1 added — a field copy in
  `TransitionFromScheduledWaitAsync` and a preserve-on-upsert guard in `UpsertAsync`. Both defend
  R1's invariant in the Console/dev host, where Postgres preserves it for free by column omission.
- **Approved mid-execution (W2):** when the `GetAsync` read inside
  `TryDeleteCheckpointUnlessRetainedAsync` throws, the delete is skipped rather than falling through.
  This diverges from today's behaviour, which deleted regardless. Rationale: the costs are asymmetric
  — skipping leaves an unretained row to the ordinary 24h TTL, whereas falling through can destroy a
  hold, which is the exact failure the guard exists to prevent. Fails toward preserving.
  `HandleAdapterNotFoundAsync` is unaffected; it still calls the unconditional
  `TryDeleteCheckpointAsync`.

## Reversal after blind code review

- **REVERSED — the F1 repair was wrong.** I instructed W1 to preserve a retention hold across
  `TryAcquireExecutionLockAsync`. An independent code review with no knowledge of that decision showed
  the opposite is correct: a hold means "this run is dead, keep its state"; a takeover means the run is
  live again, so the hold must clear. Preserving it made the row invisible to `GetRecoverableAsync`
  permanently — a resumed run would have had no recovery behind it, and `updated_at_utc` would also
  keep it out of `DeleteExpiredAsync`. Two tests pinned the wrong behaviour because I asked for them.
  The distinction that matters: an ordinary progress write to a dead row preserves the hold; a
  *takeover* of a dead row clears it.
- **B1 was a regression this change introduced.** The delete that previously ran on failure took the
  claim with it. Retaining the row left it claimed and freshly heartbeated, so a `TransientFailure`
  redelivery would be denied by the execution lock, burn its retry budget on an error code not
  classified as non-transient, and dead-letter. The claim is now released in the same UPDATE that
  stamps the hold.
- **Accepted, not fixed:** I2 (a crashed leg stamps no hold — an exception is not the collector
  reporting a failed status, per the operator's own scoping) and I5 (during a rolling deploy, old pods
  run a sweep with no retention predicate and will offer held rows). I5 is an operational property of
  the deploy, not something this change can fix in code.

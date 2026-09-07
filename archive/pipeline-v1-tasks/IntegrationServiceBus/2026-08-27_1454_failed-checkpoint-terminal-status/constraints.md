# Constraints

- ISB-only. No other repo, no package release.
- **No new column and no migration.** The status column already exists and is persisted.
- No new `CheckpointStatus` member; `Failed` is already declared.
- Service hub, not decision hub: record the reported terminal status. No error-code inspection, no
  attempt counting, no retryability judgement.
- Key the branch on the reported terminal status, never on `!result.Success` — `CancelledResult` and
  `PartialResult` set it false, `SkippedResult` sets it true.
- The refused-write early return in `FlushAndCleanupCheckpointAsync` keeps precedence over the new branch.
- `HandleAdapterNotFoundAsync` keeps deleting unconditionally.
- Release the claim in the same write as the status change.
- Strip credentials in that same write; derive the JSON key from `nameof`, never a literal.
- Both `ICheckpointRepository` implementations change together and stay behaviourally identical.
- `Checkpoint:TtlHours` must keep deleting stop-request rows exactly as it does today.
- New code names its types. No `var`. Far fewer comments than feel warranted.
- Do not commit, push, or open a PR. Build and test once at the end.

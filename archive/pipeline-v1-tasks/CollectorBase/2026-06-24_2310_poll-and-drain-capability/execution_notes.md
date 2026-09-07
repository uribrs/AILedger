# Execution Notes — poll-and-drain capability

## What changed

### Profile model — `CollectorExecutor/Profile/Profile.cs`
- `StepSpec` gained the shared item-tolerance + poll_and_drain + best_effort fields:
  - `Step` (sub-fetch, shared by `for_each` and `poll_and_drain`)
  - `ContinueOnItemFailure`, `MaxFailureRatio` (already on for_each, now shared)
  - `MaxItemRetries` — per-item TRANSIENT retry budget (0 = none)
  - `DrainPath` — JSON path to the growing item-id list
  - `FailStates` — terminal failure status values
  - `BestEffort` — emit-collected-and-continue on a hydrate sub-fetch failure
- `ProfileLoader.Validate` gained a `poll_and_drain` branch (requires `request.path`, `until.path`,
  `drain_path`, and a `step` that is a fetch); unsupported-kind message updated.

### Runner — `CollectorExecutor/Execution/CollectorExecutorRunner.cs`
- **Dispatch**: new `poll_and_drain` branch in the step loop; `StepStrategy("poll_and_drain") => "none"`;
  poll anchor seeded on resume exactly like `poll_until` (`ps = i == stepIndex ? pollStartedSeed : null`).
- **`RunToleratedItemAsync`** (shared): runs one inner fetch per item with a TRANSIENT retry budget.
  Outcome = Completed / Skipped (permanent failure tolerated) / Stop (propagate). Preserves the prior
  M2 semantics — tolerate ONLY non-retryable `Failure`; `TransientFailure` is retried up to
  `max_item_retries` then PROPAGATED (resume re-runs; no data loss); defer/cancel propagate.
- **`for_each`** refactored onto `RunToleratedItemAsync` (ratio gate unchanged); behavior identical.
- **`RunPollAndDrainStepAsync`**: fused poll + interleaved drain. Each cycle polls the status request,
  classifies it (`fail_states` terminal / 404→`EXPORT_GONE` fresh-restart / `fail_if`), drains the NEW
  members of `drain_path` (minus the processed set) immediately via `RunToleratedItemAsync`, applies the
  `max_failure_ratio` gate, and exits when `until` holds AND nothing is left undrained. Short intervals
  poll in-process; long ones externalize via `PartialResult`. Processed-item set persisted in the
  reserved `CaptureLists` key `__drained_<stepIndex>` (round-trips through the checkpoint), so resume
  restores it and continues draining idempotently. Poll-level checkpoints use strategy `"none"` so the
  resume strategy-match guard accepts re-entry.
- **`best_effort`** wired into the hydrate batch loop: a failed enrichment batch is dropped and
  collection continues (parent stream NOT failed). Distinct from item tolerance (parent survives a failed
  child call). Catches HTTP/circuit/parse failures only.

### Checkpointing
- No schema change needed — `CaptureLists` + `PollStartedUtc` already round-trip (CurrentVersion=2). The
  processed set rides the reserved `__drained_<stepIndex>` capture-list key. A4/A5 resolved against code:
  persistence shape = reserved CaptureLists key; externalize persists the set before the PartialResult.

### YAML — `integrations/tenable-io.yaml`
- Both `findings` and `assets` streams converted from `poll_until`→`for_each` to a single
  `poll_and_drain` step (interleaved progressive chunk download; `max_item_retries: 3`).

## Tests — `Tests/CollectorExecutor.Test/CollectorExecutorTests.cs` (+8, total 51 green)
- `PollAndDrain_InterleavesDownloads_WhileStillProcessing` — asserts the first chunk GET precedes the
  last status poll (download happened during PROCESSING, not after FINISHED).
- `PollAndDrain_ToleratesItemFailures_UnderRatio` / `_TooManyItemFailures_Aborts` — skip + ratio abort.
- `PollAndDrain_TerminalFailState_Fails` (`POLL_FAILED`) / `_StatusGone_404_SignalsFreshRestart` (`EXPORT_GONE`).
- `PollAndDrain_PerItemRetryBudget_RetriesTransientThenSucceeds` — flaky chunk 503→200, recovered via budget.
- `PollAndDrain_ResumeMidDrain_SkipsProcessed_NoRedownload` — processed `d1` restored; only `d2,d3`
  downloaded; export not re-requested.
- `BestEffort_Hydrate_EmitsBaseRecords_WhenEnrichmentFails` — one hydrate batch 404s; stream survives.

## Commands
- `dotnet build CollectorBase.slnx` → clean.
- `dotnet test CollectorBase.slnx` → 51/51 pass.

## Post-review repairs (verifier-1 + code-reviewer-1)
- **M1 (major) — checkpoint write storm**: poll_and_drain checkpointed per drained item (O(N²)
  serialization of the growing processed set). Fixed → ONE checkpoint per poll CYCLE. The interleaved
  design yields many small cycles, so resume granularity stays fine; a mid-cycle crash re-runs that
  cycle's items (idempotent). `RunPollAndDrainStepAsync` drain loop.
- **M2 (major) — reserved-key collision**: `__drained_<idx>` shared the user `capture_list` namespace.
  Fixed → `ProfileLoader.Validate` rejects any `capture`/`capture_list` key starting with `__`
  (test: `ReservedCaptureKey_DoubleUnderscore_FailsValidation`).
- Removed now-dead `FlakyStreamPublisher` test helper (the publisher *throws* on failure → ADAPTER_EXCEPTION,
  so the retry budget is exercised via the 503→VENDOR_HTTP_ERROR transient path instead).
- `statusVal` now read via tolerant `JsonNav.StringAt` (consistent with the remediation token rule).
- **Accepted (not fixed)**: processed-set growth is inherent to idempotent resume (native parity);
  ~40 lines duplicated between `RunPollUntilAsync` and `RunPollAndDrainStepAsync` (left for a later
  extraction — extracting now would entangle two independently-evolving control flows).

## Residual risks / pending
- **Live Tenable run (P8) PENDING**: the previously-exposed API keys were burned/rotated and no valid
  creds are available, so the live interleave/contiguity check was not run. The interleave is instead
  proven DETERMINISTICALLY in-harness (`PollAndDrain_InterleavesDownloads_WhileStillProcessing`), which is
  a stronger guarantee than a single live log. Run the live check when fresh Tenable creds are available.
- **Publish-failure is not a per-item transient**: `CollectorNdjsonPublisher` THROWS on a failed publish
  (→ `ADAPTER_EXCEPTION`), so the budget's transient retry is driven by transient HTTP classification
  (e.g. 503 → `VENDOR_HTTP_ERROR`), not publish failures. The `if (!publishResult.Success)` branch in
  `RunFetchStepAsync` is effectively defensive. Noted; not in scope to change shared publisher behavior.
- A publish-failure-then-retry on a paginated inner step can leave a page-number gap (pre-existing; the
  inner fetches here are single-page so it does not arise for Tenable).

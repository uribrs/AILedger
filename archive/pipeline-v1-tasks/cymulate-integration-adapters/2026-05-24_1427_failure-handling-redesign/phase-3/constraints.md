- Branch: `falcon-another-resume-layer`. Baseline:
  `f51ed3d` + uncommitted P0a + P0b + P1 + P2 (+P2 post-execution patches
  D-P2-17/18) working tree.
- SDK pinned at 2.0.26. Do not modify `Directory.Packages.props` or
  `Directory.Build.props`.
- Do not touch any file under `phase-0a/`, `phase-0b/`, `phase-1/`,
  `phase-2/`, or modify `plan.md`. Append-only documentation under
  `phase-3/`.
- Do not modify `UnknownFlowRetryPolicy.IsUnknownRetryCandidate`,
  `ClassifiedRetryablePolicy.ShouldHandle`, or the
  `ClassifiedRetryableTriggerException` type — those belong to P5.
- Do not enable classified-retry on any collector. No collector edits
  except (a) one minimal hook on `DummyCollector` for testability if
  needed and (b) no production cloud-collector edits.
- Reserved key prefix is `_retry.` (underscore-dot). Verified
  collision-free across all 19 `*CheckpointHelper.cs` /
  `*CheckpointWriter.cs` / `*CheckpointReader.cs` files.
- `RetryBudget` must default to zero/null when keys are absent — old
  checkpoints round-trip cleanly.
- After every retry-budget write, call `progressContext.AdvancePage(0, 0)`
  to trigger the platform's `OnCheckpoint` persistence callback. Match
  the existing collector pattern (SetState loop + AdvancePage flush).
- On successful run completion (resume path), clear all `_retry.*` keys
  by writing `string.Empty` via `SetState`, then flush via
  `AdvancePage(0, 0)`. SDK has no `RemoveKey` API.
- LoC budget: stretch ~250 LoC net add; hard ceiling 600 LoC.
- Build: 0 errors, 0 new warnings vs P2 baseline.
- All new + modified tests must pass on `dotnet test`. No new pre-existing-hang regressions.
- Cancellation propagation unchanged: every new code path threads
  `CancellationToken`; no `Task.Run`, no `Thread`, no `async void`.

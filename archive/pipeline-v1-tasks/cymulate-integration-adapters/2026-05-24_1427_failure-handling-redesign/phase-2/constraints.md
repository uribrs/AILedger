- Branch: `falcon-another-resume-layer`. Baseline commit: `f51ed3d` plus
  uncommitted P0a + P0b + P1 working-tree changes. Do not commit; the
  user owns commit cadence.
- SDK pinned at 2.0.26. Do not modify `Directory.Packages.props` or any
  `Directory.Build.props`. (Carried from D-P1-17 — P0b's executor bumped
  SDK and was reverted.)
- Do not touch any file under `phase-0a/`, `phase-0b/`, `phase-1/`, or
  modify `plan.md`. Append-only documentation under `phase-2/`.
- Do not modify the marker exception type
  `ClassifiedRetryablePolicy.ClassifiedRetryableTriggerException` or
  delete it. Phase 5 owns deletion.
- Do not change `UnknownFlowRetryPolicy.IsUnknownRetryCandidate`. The
  exclude-marker fix is Phase 4's scope per source D8.
- Do not change `ClassifiedRetryablePolicy.ShouldHandle`. The chain-walk
  fix waits for P5.
- Do not enable classified-retry on any collector. No collector edits.
- Do not introduce `_retry.*` checkpoint reads/writes. P3 scope.
- No new public types beyond what plan rev 4 §6 Phase 2 names.
- LoC budget: stretch ~250 LoC net add (production + tests combined).
  Hard ceiling 600 LoC. Above hard ceiling, stop and surface.
- All new + modified tests must pass on `dotnet test`. Pre-existing test
  hangs on `DummyCollectorTests` are EXPECTED to be FIXED by this phase
  (C10 closes here).
- Build: 0 warnings / 0 errors after the changes. Treat existing warnings
  as a baseline; do not introduce new ones.
- Test seed determinism: any test that asserts a jitter-affected delay
  must inject a `Random(seed)` instance with a fixed seed AND assert the
  exact delay produced by that seed — no tolerance windows.
- Behaviour identity outside jitter: every assertion that pinned a
  specific delay before P2 must still pass under the same seed (i.e.,
  picking the SAME RetryDelays entry, then applying ±0% jitter when
  range is zero, then applying ±20% jitter only when callers opt in).
- Diagnostic-guard call sites added in P1 stay unchanged.
- Cancellation propagation unchanged: `CancellationToken` flows through
  every new code path; no `Task.Run` / `Thread` / async-void.

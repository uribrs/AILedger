# Phase 2 — Programmer-bug blacklist + jitter wiring

Source: `plan.md` rev 4 §6 Phase 2 (lines 601-621).

Replace Phase 1's `ProgrammerBugClassifier.IsBug` and `DelayPlanner.PickX`
stubs with their working bodies. Thread `DelayPlanner` into the two Polly
pipelines (`UnknownFlowRetryPolicy.CreatePipeline`,
`ClassifiedRetryablePolicy.CreatePipeline`) with ±20% jitter. Wire
`ProgrammerBugClassifier` into `DefaultFailurePolicy.DecideAsync` as an
early branch that returns `FailFast` with `ErrorCode = "PROGRAMMER_BUG"`,
`IsRetryable = false`.

Fix C10 — `DummyCollectorTests` ~21-minute test bloat — by passing a
zero-delay `DelayPlanner` into the Polly pipelines under test.

Out of scope: `_retry.*` checkpoint persistence (P3), enabling
classified-retry on any collector (P4), and either of the marker-leak
fixes (chain-walk → P5; exclude-marker → P4 per source state D8).

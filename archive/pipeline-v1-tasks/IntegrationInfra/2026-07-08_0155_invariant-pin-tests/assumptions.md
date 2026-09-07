# Assumptions

- VALIDATED — (a) has zero existing coverage: no test references `_checkpoint.*` keys, `SeedFromPersistedState`, or `AdapterCheckpoint` in FaultGovernance.Tests (grep 2026-07-08).
- VALIDATED — (c)/(d) runner-level tests EXIST: `AdapterBusEntrypointRunnerInvariantTests` has `PartialSuccessWins_FailureIsSuppressed_WhenPartialResultIsBuilt`, `Cancellation_IsNackNotFailure_PublishesNothing`, `ThrowingHostCallback_DoesNotSuppressTheFailurePublish`. Scope is assertion-depth audit, not new scenarios.
- VALIDATED — (b) classifier suites exist and are broad (socket theory, marker theory, nested chain, circuit-by-name, null-arg). Gap candidates only: depth-cap-10 boundary, full 9-code socket set, RetryDelays clamp.
- VALIDATED — (e) evaluator coverage exists (defer@1, stuck gate, forward reset, both backstops, GetDelay family). Gap candidates: backstop-before-stuck ordering when both fire, malformed-coordinate fails open, and the AdapterRecoveryBudget persistence layer (folded into (a)).
- VALIDATED — (f) atomicity suite is comprehensive (12 scenarios incl. abort-exactly-once, cancellation, dispose, complete-failure, giant record, scoped path). Gap candidate: explicit commit-incomplete `DataPipelineException` message pin.
- OPEN — the SDK's `AdapterCheckpoint`/`AdapterState` can be constructed directly in tests (public ctor/init). If the SDK requires host-side factories, executor falls back to constructing the same shape via the type's public surface and records how.
- OPEN — `AdapterFailureDecisionExecutor.ExecuteAsync` can be driven with a lightweight fake execution context (the Conducting invariant tests prove a context double exists — verify it is reusable from FaultGovernance.Tests or clone the minimal shape locally).

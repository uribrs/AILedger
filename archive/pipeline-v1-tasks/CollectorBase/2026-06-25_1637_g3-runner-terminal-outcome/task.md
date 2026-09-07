# Task: Runner terminal-outcome semantics

Two MAJOR fixes in the runner's terminal-classification / partial-success / long-wait-externalized cluster.

## A-M1 — server-suggested long-delay externalization hardcoded off
`CollectorExecutorSessionFactory.cs:28` sets `ExternalizeServerSuggestedDelays = false` (no cap). Long
`Retry-After` is slept in-process; the `catch (ServerSuggestedRetryDelayException)` → PartialResult defer at
`Runner.cs:204` is dead. Shared default (SessionRetryDefaults.cs:37-39) is true + 24h cap.
FIX: `ExternalizeServerSuggestedDelays = retry.ServerDelayStrategy != "disabled"`;
`MaxServerSuggestedDelay = TimeSpan.FromSeconds(Math.Max(1, retry.MaxInProcessDelaySeconds))` (default 60 — the
declared in-process threshold). ≤cap honored in-process; >cap externalized as PartialResult.

## A-M2 — publish-failure-after-partial misclassified
`Runner.cs:350` returns hard `TransientFailure("PUBLISH_FAILED")` even after ≥1 page emitted, ignoring the
partial-success-wins branch every other terminal exit uses (`emitted>0 ? PartialSuccess(...) : <hard fail>`,
:281/:375/:390).
FIX: `emitted>0 ? PartialSuccess(emitted,page,vendorName,flowName) : TransientFailure(...,"PUBLISH_FAILED")`.

## Out of scope
No other behavior change; the Shared resilience policy order/values are untouched.

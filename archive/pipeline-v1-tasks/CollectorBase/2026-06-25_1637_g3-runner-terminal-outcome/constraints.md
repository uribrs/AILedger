# Constraints

- A-M1: only the two RetryOptions fields in SessionFactory change (ExternalizeServerSuggestedDelays + MaxServerSuggestedDelay); map the cap from `ResilienceSpec.MaxInProcessDelaySeconds`. Do not alter RespectRetryAfterHeader semantics or the fixed resilience policy order.
- A-M2: only the publish-failure branch (Runner.cs:350) changes — to the established `emitted>0 ? PartialSuccess(...) : TransientFailure(...)` pattern. No other runner change.
- Reuse Shared resilience (no bespoke transport/policy); generic engine, no vendor identity.
- net8.0; CollectorBase.slnx.
- No regression of the 79 passing tests. CRITICAL: ProcessAsync_Findings_HonorsRetryAfterInProcess_Emits3Records (1s Retry-After ≤ 60s cap) must STILL be honored in-process (not externalized).
- The A-M1 long-Retry-After test must NOT actually sleep (the externalized path returns a PartialResult immediately) — the suite stays fast.

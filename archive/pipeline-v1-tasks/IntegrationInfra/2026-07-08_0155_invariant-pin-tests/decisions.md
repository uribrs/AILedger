# Decisions

- Scope reshaped at contract time: (a) is the core deliverable (zero coverage); (b)(e)(f) are audit-and-gap-fill, each may legitimately be a no-op; (c)(d) are assertion-depth audits of existing tests. — grounding grep showed the first-pass assumption of missing coverage was wrong for everything but (a).
- (a) also pins `AdapterRecoveryBudget` persistence (Write/Load, Clear vs ClearEpisode, Seed copies only `_resilience.*`) — same file, same fixture, no extra cost; this is the (e)-persistence gap folded in.
- A no-op audit verdict must be recorded in execution_notes.md with the existing test names that cover each contract point — "nothing to add" requires evidence, not assertion.
- Circuit-breaker pin tests use locally-declared exception types named `BrokenCircuitException`/`IsolatedCircuitException` (no Polly package reference in Kernel.Tests).
- Deepening an existing test's assertions is preferred over adding a near-duplicate test for (c)/(d).
- SDK source verified (2026-07-08, /Users/user/Dev/IntegrationServiceBus/.../Sdk): the `_checkpoint.*` literals match the SDK's private consts exactly (AdapterProgressContext, IAdapterExecutionContext.cs:111-114). The convention exists in THREE private copies (SDK writer, ISB-host reader in Infrastructure.Core/AdapterExecutionContext.cs:24-27, Infra's deferred-wait writer). Root-fix follow-up recorded: SDK should export a public CheckpointMetadataKeys constants class; until then the pin test is the tripwire.

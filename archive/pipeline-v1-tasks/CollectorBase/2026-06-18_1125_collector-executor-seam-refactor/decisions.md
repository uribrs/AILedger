# Decisions

- Runner becomes a composition root; behavior lives in named strategy components resolved from the profile via a registry.
- Seam set for Phase 0: WindowPlanner, StagePlanner, Paginator, PageSize, Hydration, ResponseMapper, FailurePolicies (Shared), RecoveryBudget (Shared, always-on).
- Cross-seam coordination via a shared RunContext + a small decision vocabulary (ResetToWatermark | DeferUnbudgeted | Retry | Fail); strategies emit/honor signals, never call each other.
- Invariants (recovery budget, partial-page-success, checkpoint ordering, server-delay externalization, bounded memory) are orchestrator-guaranteed, not opt-out.
- Mirror YamlCollector's mechanism (PaginatorFactory-style registry, checkpoint Strategy discriminator, CanResumeFrom gating); lift hardened content from Falcon in LATER phases.
- Generic strategies live in a separate Strategies library in CollectorBase (so established collectors can converge later).
- Copy the CollectorExecutor unit test project into CollectorBase as the local regression oracle (don't depend on the adapters repo for verification).
- Phase 0 is behavior-preserving: NO Falcon hardened quirks implemented this pass.
- The seam set must be proven open/closed (adding a strategy needs no orchestrator edit), even though no new strategy is added now.
- [orchestrator, B1] Behavior-preserving WINS over "wire recovery budget": Phase 0 creates the resilience/recovery seam with default = today's behavior (no budget gating). Real progress-anchored RecoveryBudget is Phase-1 hardened content. (Resolves the contract contradiction.)
- [orchestrator, A3] RunContext is a SIDECAR wrapping the host-created AdapterProgressContext (cannot subclass a host-created instance).
- [orchestrator, increment plan] Increment 1 = local oracle + A3/A4 (DONE, verified green). Increment 2 = the S1–S6 seam refactor (direct path, same contract, B1 resolved), gated by the green oracle.

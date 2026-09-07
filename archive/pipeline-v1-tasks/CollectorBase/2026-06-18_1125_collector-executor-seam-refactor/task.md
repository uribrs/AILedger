# Task: CollectorExecutor seam refactor (Phase 0)

Refactor the `CollectorExecutor` runner from a fixed, inlined loop into a **seam-based composition
pipeline**, so vendor behaviors become named, YAML-selected, registered strategies — and define the
cross-seam **decision-vocabulary** contract. This is the foundation that lets Falcon's hardened
quirks (and other collectors') be added later WITHOUT modifying the orchestrator.

Behavior-preserving: this pass adds structure and seams, not capability.

## Working repo
/Users/user/Dev/Uri/localprojects/CollectorBase (Shared + CollectorExecutor + Runner; net8.0 sln)

## Reference (read-only)
/Users/user/Dev/cymulate-integration-adapters — FalconCollector (hardened content, later phases),
Collectors/YamlCollector + its engine (the strategy-selection MECHANISM to mirror), Shared.

## In scope
- Seam interfaces: WindowPlanner, StagePlanner, Paginator, PageSize, Hydration, ResponseMapper;
  resilience seam (FailurePolicies via Shared AdapterResilienceStrategy/IAdapterFailurePolicy);
  always-on RecoveryBudget (Shared AdapterRecoveryBudget).
- Today's behavior reimplemented as the DEFAULT strategy in each seam.
- Strategy registry (name -> factory); profile names the strategy per seam; load-time fail-closed validation.
- Decision-vocabulary contract (ResetToWatermark / DeferUnbudgeted / Retry / Fail) over a shared
  RunContext (extends AdapterProgressContext) so coupled quirks coordinate without reaching into each other.
- Invariants (always-on) vs opt-in strategies, explicitly separated.
- Generic strategy components live in a shared Strategies library in CollectorBase.
- Local regression oracle: copy the CollectorExecutor unit test project into CollectorBase.

## Out of scope (later phases)
Falcon hardened strategies: cursor_watermark fallback + depth-cap, time segmentation,
Spotlight status lanes, vendor failure quirks. Phase 0 only opens the seams for them.

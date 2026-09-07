# Constraints

- Change confined to the Shared resilience layer; it is global and affects all collectors.
- Target `net8.0`. SDK installed is 9.x — pin via csproj `TargetFramework`; never pass `-f net9.0`.
- Do NOT reach into per-collector flow loops from Shared. Use only `AdapterProgressContext` (`CurrentPage`, `ProcessedItems`, `ProcessedFindings`, and `AdapterState` are readable) plus persisted budget keys.
- Progress coordinate = stable hash of `AdapterState` entries whose key does NOT start with `_resilience.`, folded with `ProcessedItems` + `ProcessedFindings` + `CurrentPage`.
- Capture `CurrentPage` / `ProcessedItems` / `ProcessedFindings` at defer-entry, BEFORE the defer's own `AdvancePage(0,0)` call in `AdapterFailureDecisionExecutor.BuildPartialWaitResult`. The artificial page bump must not register as progress.
- Hash must be deterministic and culture-invariant (sorted keys, invariant culture).
- Preserve the two success-only `Clear` callsites' semantics (`AdapterBusStrategyFlowExecutor.cs`, `CollectorResumeStrategyExecutor.cs`). Extend `Clear` to wipe the new keys.
- Unbudgeted waits (`UseRecoveryBudget=false`: planned yields, server-suggested delays) stay budget-neutral and still clear.
- Backstop values configurable; defaults 50 total budgeted deferrals / 24h since first budgeted defer.
- Migration must be graceful: old checkpoints carry `attemptCount` but no coordinate → treat missing coordinate as progress.
- Tests: xUnit + Moq + FluentAssertions; central package management in `Directory.Packages.props` (add `PackageReference` without version, set version centrally).
- Test output is redirected to `artifacts/bin|obj/ut/`; do not change in-project obj/bin excludes.

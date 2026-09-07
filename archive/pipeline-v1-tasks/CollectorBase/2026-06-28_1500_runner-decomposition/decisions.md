# Decisions

- D1: NO polymorphic `IStepExecutor` interface + registry. The `RunAsync` switch stays explicit — the four step seeds genuinely differ (`FetchSeed` / `ForEachStart` / `PollStartedUtc`); a uniform interface would force a lowest-common-denominator seed and hide wiring.
- D2: `CollectorExecutorStepHelpers` decomposition is deferred to a later pass (keep this one focused on the Runner).
- D3: No public adapter-surface signature changes; internal refactor only.
- D4: Incremental extraction — one class at a time, build + test green between each; STOP on red rather than push through.
- D5: FULL decomposition chosen (Tier 1 + Tier 2) over internal-cleanup-only — user signed off the larger diff for the bigger readability win.
- D6: `for_each` and `poll_and_drain` executors take the fetch executor / item-tolerance runner as injected dependencies (honest, narrow dependency — not a framework).
- D7: `StepOutcomeFactory` is the single home for the partial-success-wins invariant; every former inline ternary call site routes through it.

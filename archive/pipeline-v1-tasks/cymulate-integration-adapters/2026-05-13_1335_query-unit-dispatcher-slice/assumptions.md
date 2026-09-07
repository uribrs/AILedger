- VALIDATED: The aggregate handoff recommends a fresh bounded contract for the concrete Query `IUnitDispatcher` slice.
- VALIDATED: The SDK `IUnitDispatcher` contract accepts only `IQueryAdapter`, `ExecutionPlan`, and `CancellationToken`; job settings are not directly available at dispatch time.
- VALIDATED: The current SDK `ExecutionPlan` does not carry `QueryJobSettings.MaxConcurrentUnits`.
- OPEN: If `MaxConcurrentUnits` must be honored exactly, the plan or dispatcher boundary will need a future SDK-level settings handoff.


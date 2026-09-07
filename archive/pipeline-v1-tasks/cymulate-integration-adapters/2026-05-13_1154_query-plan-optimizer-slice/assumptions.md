* VALIDATED: SDK `IPlanOptimizer` exists and exposes `OptimizeAsync(ExecutionPlan plan, CancellationToken cancellationToken)`.
* VALIDATED: SDK optimizer responsibility is to merge time-window units sharing `ResultType`, `TimeWindow`, and `Targeting`, with `Product` provided by the parent plan.
* VALIDATED: SDK `ExecutionUnit.UnitId` documentation defines the optimized deterministic hash from `Product`, `ResultType`, `TimeWindow`, and `Targeting`.
* VALIDATED: The plan builder already canonicalizes targeting and time-window payloads before optimizer input.
* VALIDATED: Cancellation behavior can be implemented with explicit checks while grouping the in-memory plan units.

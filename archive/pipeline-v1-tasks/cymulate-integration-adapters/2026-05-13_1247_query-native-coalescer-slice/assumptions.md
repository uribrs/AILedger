* VALIDATED: SDK `INativeCoalescer` exists and exposes `CoalesceAsync(ExecutionPlan plan, CancellationToken cancellationToken)`.
* VALIDATED: SDK native coalescer responsibility is grouping native units by exact `(Product, NormalizedQuery, ResultType)` and accumulating represented query ids.
* VALIDATED: SDK native unit id documentation defines the deterministic hash from `Product`, `NormalizedQuery`, and `ResultType`.
* VALIDATED: `INativeCoalescingOptOut` is a marker capability used by composition, not a dependency of the coalescer implementation itself.
* VALIDATED: Merged native result limits follow the same least-restrictive cap policy as optimized time-window units to avoid under-fetching.

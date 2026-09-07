* Implement `QueryPlanOptimizer` in the existing `Orchestration/Query/Planning` folder.
* Reuse `QueryPlanUnitIds` for deterministic optimized unit ids, extending it only as needed.
* Canonicalize optimizer grouping identity for targeting and UTC time-window values, while relying on the plan builder for validation.
* Merge time-window result limits with the least restrictive cap: `null` if any represented unit is unlimited, otherwise the maximum configured limit.
* Preserve native unit list values exactly; native coalescing remains a later slice.

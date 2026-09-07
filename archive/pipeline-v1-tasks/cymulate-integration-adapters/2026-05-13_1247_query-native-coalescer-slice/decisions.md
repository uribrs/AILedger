* Implement `NativeQueryCoalescer` in the existing `Orchestration/Query/Planning` folder.
* Extend `QueryPlanUnitIds` with deterministic coalesced native unit id generation that excludes original query id.
* Preserve native normalized query text exactly; do not trim, parse, lowercase, or otherwise normalize beyond the builder's current trim-only behavior.
* Merge native result limits with the least restrictive cap: `null` if any represented unit is unlimited, otherwise the maximum configured limit.
* Preserve time-window unit list values exactly; time-window optimization remains owned by `QueryPlanOptimizer`.

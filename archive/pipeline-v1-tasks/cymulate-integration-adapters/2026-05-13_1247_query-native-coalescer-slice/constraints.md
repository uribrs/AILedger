* Keep Shared concern-first; place coalescer code under `Orchestration/Query/Planning`.
* Implement SDK `INativeCoalescer`; do not create a duplicate local coalescer contract.
* Group only native `NativeExecutionUnit` values by plan product, normalized query, and result type.
* Do not merge or alter time-window `ExecutionUnit` values in this slice.
* Do not mutate the input `ExecutionPlan` or existing unit instances.
* Preserve `JobId`, `ClientId`, `Product`, `CreatedAt`, `SectionMembership`, and time-window units.
* Keep `INativeCoalescingOptOut` as a composition-level concern; do not add adapter capability detection to this class.
* Do not implement Dataflow topology, dispatcher, distributor, matcher, section tracker, recovery planner, or plan-store persistence.
* Do not add real or fake Query Integration clients.
* Do not decide composite `ResultType`, `ExpressionDialect`, expanded `QueryTargeting`, or deeper native normalization policy.
* Keep methods small, focused, and consistent with existing Shared conventions.
* Preserve existing user changes.

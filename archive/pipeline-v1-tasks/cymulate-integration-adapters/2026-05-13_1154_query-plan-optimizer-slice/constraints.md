* Keep Shared concern-first; place optimizer code under `Orchestration/Query/Planning`.
* Implement SDK `IPlanOptimizer`; do not create a duplicate local optimizer contract.
* Merge only time-window `ExecutionUnit` values.
* Do not merge or alter `NativeExecutionUnit` values in this slice.
* Do not mutate the input `ExecutionPlan` or its existing unit instances.
* Preserve `JobId`, `ClientId`, `Product`, `CreatedAt`, `SectionMembership`, and native units.
* Do not implement `INativeCoalescer`, dispatcher, distributor, matcher, section tracker, Dataflow topology, recovery planner, or plan-store persistence.
* Do not add real or fake Query Integration clients.
* Do not decide composite `ResultType`, `ExpressionDialect`, expanded `QueryTargeting`, or native normalization policy.
* Keep methods small, focused, and consistent with existing Shared conventions.
* Preserve existing user changes.

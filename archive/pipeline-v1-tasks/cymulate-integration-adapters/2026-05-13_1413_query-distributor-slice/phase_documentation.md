# Query Distributor Slice

Implemented product code:

- `Orchestration/Query/Dataflow/QueryDistributor`

Behavior now available:

- SDK `IDistributor` implementation.
- Per-plan unit index covering time-window and native units.
- Fan-out from each `RawResponse.UnitId` to represented logical query ids.
- Section ownership resolution from `ExecutionPlan.SectionMembership`.
- One `DistributedResponse` emitted per section/query/raw-response target.
- Original `RawResponse` instance preserved for matcher input.
- Clear failures for unknown unit ids, duplicate unit ids, and represented queries missing from section membership.
- Streaming distribution directly from the input response stream.
- Cancellation propagation during input enumeration and fan-out.

Still future work:

- Concrete `IMatcher`.
- Concrete `ISectionTracker`.
- Query recovery planner/resume runner.
- `IExecutionPlanStore` integration decision outside adapters Shared persistence.
- Real Query clients.


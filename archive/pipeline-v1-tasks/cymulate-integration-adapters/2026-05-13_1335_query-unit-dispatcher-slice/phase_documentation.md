# Query Unit Dispatcher Slice

Implemented product code:

- `Orchestration/Query/Dataflow/QueryUnitDispatcher`
- `Orchestration/Query/Dataflow/QueryUnitDispatcherOptions`

Behavior now available:

- SDK `IUnitDispatcher` implementation.
- Time-window unit dispatch through `IQueryAdapter.ExecuteAsync`.
- Native unit dispatch through `IQueryAdapter.ExecuteNativeAsync`.
- Capability marker validation for `ITimeWindowQueryCapability` and `INativeQueryCapability`.
- Bounded unit concurrency through dispatcher options.
- Bounded raw-response buffering through a channel.
- Progressive `RawResponse` streaming without materializing full job output.
- `RawResponse.UnitId` invariant validation before fan-out to later stages.
- Cancellation and adapter exception propagation.

Still future work:

- Job-specific `QueryJobSettings.MaxConcurrentUnits` handoff if the SDK dispatcher boundary is extended.
- Concrete `IDistributor`.
- Concrete `IMatcher`.
- Concrete `ISectionTracker`.
- Query recovery planner/resume runner.
- `IExecutionPlanStore` integration decision outside adapters Shared persistence.
- Real Query clients.

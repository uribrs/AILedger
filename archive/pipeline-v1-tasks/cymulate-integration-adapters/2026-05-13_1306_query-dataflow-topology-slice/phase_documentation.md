# Query Dataflow Topology Slice

Implemented product code:

- `Orchestration/Query/Dataflow/QueryAdapterPipeline`
- `Orchestration/Query/Dataflow/QueryDataflowOptions`
- `Orchestration/Query/Dataflow/IQuerySectionTrackerFactory`
- `Orchestration/Query/Dataflow/IQuerySectionPublisherFactory`
- `Orchestration/Query/Dataflow/QuerySectionPublisherFactory`

Behavior now available:

- SDK `IQueryAdapterPipeline` composition shell.
- Query progress context creation through `IAdapterExecutionContext`.
- `QueryPipelineContext` creation.
- Ordered plan builder, optimizer, and native coalescer composition.
- Injected SDK-stage boundaries for dispatch, distribution, matching, tracking, and publication.
- Per-plan section tracker creation through a local factory.
- Per-run Query section publisher creation backed by existing Shared publication code.
- Job completion summary publication with sections published, execution units run, original query count, timestamps, and outcome.
- Failed job outcome when the tracker completes before all planned sections are published.

Still future work:

- Concrete `IUnitDispatcher`.
- Concrete `IDistributor`.
- Concrete `IMatcher`.
- Concrete `ISectionTracker`.
- Query recovery planner/resume runner.
- `IExecutionPlanStore` integration decision outside adapters Shared persistence.
- Real Query clients.

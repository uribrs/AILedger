# Query Section Tracker Slice

Implement the concrete Shared Query `ISectionTracker` stage behind the SDK contract.

The tracker accumulates SDK `Finding` instances per (section, query), attributes SDK `QueryErrorV2` instances to all queries represented by a failed execution unit, and reads SDK `ExecutionUnitState` transitions from an injected `IExecutionPlanStore` to decide when each section is fully covered. When every original query in a section has either at least one finding plus all its representing units in `Completed`, or all its representing units in `Failed`/`Cancelled` with a matching `RecordUnitFailureAsync` call, the tracker emits exactly one SDK `SectionResultV2` through its `CompletedSections` async stream.

Findings are emitted as inline `FindingRefV2` instances. The Spillover stage is downstream future work. `QueryJobCompletedV2` is the publisher's responsibility and is not produced by the tracker.

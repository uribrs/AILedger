# Query Section Tracker Slice

Implemented product code:

- `Orchestration/Query/Dataflow/QuerySectionTracker`
- `Orchestration/Query/Dataflow/QuerySectionTrackerOptions`

Behavior now available:

- SDK `ISectionTracker` implementation.
- Constructor takes `ExecutionPlan`, SDK `IExecutionPlanStore`, and `QuerySectionTrackerOptions` (default `PollInterval` 1 second).
- Thread-safe accumulation of findings per `(SectionId, QueryId)`.
- Unit-failure attribution to every owning section's matching queries.
- Polling-driven detection of unit terminal states from `IExecutionPlanStore.GetUnitStatesAsync`.
- Wait-for-matching-failure invariant: a section whose store unit-state is `Failed`/`Cancelled` is not emitted until the matching `RecordUnitFailureAsync` has been recorded.
- Section emission as `SectionResultV2` with `QueryResultV2.Findings` populated as inline `FindingRefV2` items in arrival order; `ByteSize` computed as the UTF-8 byte count of the inline payload's raw JSON text.
- Idempotent emission: each section emitted exactly once.
- Single-consumer guarantee on `CompletedSections`.
- Cancellation propagation across `RecordFindingAsync`, `RecordUnitFailureAsync`, and `CompletedSections`.

Hygiene fix folded in:

- Added project-level `UnitTests/.../GlobalUsings.cs` so the test project compiles after the operator/linter trimmed per-file usings from `QueryDistributorTests.cs` and `QueryMatcherTests.cs`. The trimmed files were not reverted.

Still future work:

- Concrete `IExecutionPlanStore` implementation (lives outside this repo per SDK doc; this slice depends on the SDK interface only).
- Dispatcher/recovery wiring that actually transitions store unit states and records failures through the tracker.
- Spillover stage that decides inline-vs-S3 for `FindingRefV2`.
- Wiring `QuerySectionPublisher` into the dataflow as the consumer of `CompletedSections`.
- Structured matcher filtering by expression, IP/hostname, and payload-side result type (still gated on SDK structural rules).
- Query recovery planner / resume runner.
- Query run/progress/done envelopes.
- Real Query clients.

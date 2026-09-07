# Query Matcher Slice

Implemented product code:

- `Orchestration/Query/Dataflow/QueryMatcher`

Behavior now available:

- SDK `IMatcher` implementation.
- Admission-only mapping from each `DistributedResponse` to one `Finding`.
- `Finding.JobId` taken from the method parameter.
- `Finding.SectionId` and `Finding.QueryId` taken from `DistributedResponse`.
- `Finding.ResultType` taken from the matched `QueryV2` (composite flag values pass through unchanged).
- `Finding.Payload` taken from the inner `RawResponse.Payload` without copying.
- Defensive failure when `distributed.QueryId` does not equal `query.QueryId`.
- Cancellation honored before emission.
- Argument-null guards on `distributed` and `query`.

Hygiene fix folded in:

- Restored missing `using` directives in `UnitTests/.../QueryDistributorTests.cs` so the Query shared test project compiles. No behavior change.

Still future work:

- Structured matcher filtering by expression, IP/hostname, and payload-side result type once SDK exposes structured rules.
- Capability-aware passthrough swap in pipeline composition when adapter declares `IAdapterSideMatchingCapability`.
- Concrete `ISectionTracker` and job-completion tracker.
- Query recovery planner / resume runner.
- `IExecutionPlanStore` integration decision outside adapters Shared persistence.
- Query run/progress/done envelopes.
- Real Query clients.

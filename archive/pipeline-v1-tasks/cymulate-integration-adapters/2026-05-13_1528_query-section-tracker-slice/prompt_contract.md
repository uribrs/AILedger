Role:
You are a senior .NET engineer working in the Cymulate adapters Shared Query domain.

Goal:
Implement the concrete Shared Query `ISectionTracker` slice that accumulates findings and unit failures per section and emits one SDK `SectionResultV2` per section when its full query set is covered, using an injected SDK `IExecutionPlanStore` for unit state.

Context:
The Query planning, dataflow topology, dispatcher, distributor, and admission-only matcher stages are already implemented. `QueryAdapterPipeline` currently depends on the SDK `ISectionTracker` contract but no concrete Shared tracker exists. The SDK doc states the tracker reads execution-unit state from `IExecutionPlanStore` and signals the publisher when each section is complete. `IExecutionPlanStore` has no concrete implementation in this repo; the tracker depends on the SDK interface and tests use a mock.

Constraints:

* Keep implementation under `Orchestration/Query/Dataflow`.
* Use SDK Query contracts directly.
* Implement only `ISectionTracker` behavior in this slice.
* Constructor takes the `ExecutionPlan`, an SDK `IExecutionPlanStore`, and a `QuerySectionTrackerOptions` value with a polling interval (default 1 second).
* Accumulate findings thread-safely per `(SectionId, QueryId)`.
* Attribute each `RecordUnitFailureAsync` call to every query in the failing unit's `RepresentedQueryIds`, scoped to the sections that own each query.
* `CompletedSections` is a single-consumer async stream that yields each `SectionResultV2` exactly once.
* A section is ready to emit when, for every `QueryId` in `SectionMembership[SectionId]`, all units representing that query are in a terminal state (`Completed`, `Failed`, or `Cancelled`) per `IExecutionPlanStore.GetUnitStatesAsync`, AND every representing unit in `Failed`/`Cancelled` has received a matching `RecordUnitFailureAsync` call.
* Build each `QueryResultV2` with `Findings` populated from accumulated `Finding`s wrapped in inline `FindingRefV2` (set `InlinePayload` to the finding payload, `S3Uri` to null, `ByteSize` to the UTF-8 byte count of the payload's raw JSON text), `StreamedBatchCount` null, and `Error` set to the first recorded `QueryErrorV2` for any of the query's representing units (null when no failure was recorded).
* `SectionResultV2.EmittedAt` is `DateTimeOffset.UtcNow` at the moment the result is enqueued.
* Use a bounded `Channel<SectionResultV2>` for the output stream.
* Re-evaluate completion both on every `RecordFindingAsync`/`RecordUnitFailureAsync` call and on a polling tick driven by the configured interval, until the consumer cancels or every section has been emitted.
* Respect cancellation in `RecordFindingAsync`, `RecordUnitFailureAsync`, and `CompletedSections`.
* Do not implement publication transport, Spillover (inline-vs-S3), section publisher, recovery planner, resume runner, or `IExecutionPlanStore` itself.
* Do not emit `QueryJobCompletedV2` (publisher owns it).
* Do not add real Query clients or fake Query clients as product code.
* Do not add RabbitMQ, Postgres, outbox, storage, session, HTTP retry, host, or transport infrastructure.
* Keep code small, focused, and aligned with existing Shared Query style.

Success Criteria:

* A concrete Shared class implements SDK `ISectionTracker`.
* Constructor signature accepts `ExecutionPlan`, `IExecutionPlanStore`, and `QuerySectionTrackerOptions`.
* `RecordFindingAsync` thread-safely accumulates `Finding`s into `(SectionId, QueryId)` buckets and triggers a completion re-check.
* `RecordUnitFailureAsync` attributes the `QueryErrorV2` to every `(SectionId, QueryId)` pair for which the failing unit's `RepresentedQueryIds` intersects the section's membership, and triggers a completion re-check.
* `CompletedSections` yields exactly one `SectionResultV2` per section once its completion criterion is met.
* `QueryResultV2.Findings` is populated with inline `FindingRefV2` items in the same order as the recorded findings; `ByteSize` is the UTF-8 byte count of the inline payload's raw text.
* `QueryResultV2.Error` is set to a recorded `QueryErrorV2` for the query when any representing unit failed; null otherwise.
* `QueryResultV2.StreamedBatchCount` is always null in this slice.
* `SectionResultV2.EmittedAt` is set at emission.
* Polling cadence honored — sections with zero findings and all-Completed units still emit.
* Cancellation propagates as `OperationCanceledException` from each method.
* Focused tests cover: single-section single-query happy path; multi-query section; empty query (zero findings, unit Completed); unit failure attribution including multi-section ownership; multiple representing units per query; idempotent emission (each section emitted once); the wait-for-matching-failure invariant (store says Failed, no RecordUnitFailureAsync yet → not emitted); cancellation of `RecordFindingAsync`, `RecordUnitFailureAsync`, and `CompletedSections`.
* Targeted Shared build and Query shared tests pass, or any failure is documented with evidence.
* Task state, execution notes, and aggregate handoff state are updated.

Execution Rules:

* Do not assume missing data.
* Respect constraints strictly.
* Keep changes scoped to this section tracker slice.
* Work with existing user changes and do not revert unrelated edits.
* Update `state.json` as step status or verification status changes.

Output Format:
Final response must include what changed, validation results, and any blocker or residual risk.

Stop Conditions:

* When goal is achieved.
* When required data is missing.
* When SDK contract limitations prevent a required behavior without changing SDK source.

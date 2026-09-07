Role:
You are a senior .NET engineer working in the Cymulate adapters Shared Query domain.

Goal:
Wire `QuerySectionPublisher` to consume `QuerySectionTracker.CompletedSections` and emit `QueryJobCompletedV2` at job end inside `QueryAdapterPipeline`, with composition-only edits, and add wiring-focused tests that exercise the real tracker and real publisher together end-to-end.

Context:
The Query planning, dataflow topology, dispatcher, distributor, admission-only matcher, and section tracker stages are implemented. `QueryAdapterPipeline` already composes the tracker and publisher via `IQuerySectionTrackerFactory` and `IQuerySectionPublisherFactory`, iterates `tracker.CompletedSections`, publishes each `SectionResultV2`, and emits a pipeline-built `QueryJobCompletedV2` after the stream completes. The mock-based composition tests in `QueryAdapterPipelineTests.cs` assert the call order but do not exercise the real `QuerySectionTracker` or real `QuerySectionPublisher`. The aggregate Query state.json and the section tracker handoff both call out this slice as the next composition step. Spillover, structured matcher filtering, a concrete `IExecutionPlanStore`, recovery planner, and run/progress/done envelopes remain out of scope.

Constraints:

* Composition-only edits inside `QueryAdapterPipeline`. No new infrastructure dependency.
* Preserve `QuerySectionTracker` "Behavior To Preserve" exactly as listed in `2026-05-13_1528_query-section-tracker-slice/next_agent_handoff.md`.
* `QuerySectionTracker.CompletedSections` is single-consumer; the wiring code must consume it exactly once.
* Spillover is out of scope. Sections emit inline `FindingRefV2` only.
* Structured matcher filtering is out of scope.
* `QueryJobCompletedV2` is emitted at job end via `ISectionPublisher.PublishJobCompletedAsync`; the tracker never emits it.
* `QueryJobCompletedV2` is built by `QueryAdapterPipeline` (already implemented in `PublishCompletedSectionsAsync`); do not relocate this responsibility into the tracker or the publisher.
* `IExecutionPlanStore` concrete implementation is not part of this slice. Tests may use an in-memory `IExecutionPlanStore` fake.
* `IAdapterExecutionContext.PublishAsync` is the publication boundary. Wiring tests substitute a capturing `IAdapterExecutionContext` fake; the publisher itself is not mocked.
* Cancellation in the wired path must propagate as `OperationCanceledException` from the tracker through the pipeline, then translate to `AdapterResult.FailureResult` with `OPERATION_CANCELLED` at the pipeline boundary (existing translation; preserve it).
* `dotnet build` on the Shared csproj passes with 0 warnings and 0 errors (NU1900 acceptable).
* `dotnet test` on `Cymulate.Integration.Adapters.QueryIntegration.Shared.Test` passes; this slice adds wiring tests.
* Wiring tests must use the real `QuerySectionTracker` and the real `QuerySectionPublisher`. Mocks are acceptable for `IQueryAdapter`, `IPlanBuilder`, `IPlanOptimizer`, `INativeCoalescer`, `IUnitDispatcher`, `IDistributor`, `IMatcher`, and `IExecutionPlanStore`.
* If trimmed-usings affect the test project compile, extend the existing `GlobalUsings.cs` rather than restoring per-file `using` directives.
* No real or fake Query clients as product code. No RabbitMQ, Postgres, outbox, storage, session, HTTP retry, host, or transport infrastructure.
* Do not refactor unrelated pipeline behavior.
* Do not revive the superseded topology task or any deferred items without a new contract.

Success Criteria:

* `QueryAdapterPipeline` composes the tracker and publisher correctly: the publisher consumes `QuerySectionTracker.CompletedSections` exactly once per pipeline run and emits `QueryJobCompletedV2` at job end. (Already implemented; this slice verifies and documents the wiring; any minimal composition-only adjustment is allowed but not required.)
* Shared build clean: 0 warnings, 0 errors (NU1900 acceptable).
* All existing Query shared tests still pass with no regression.
* New wiring tests cover, with the real tracker and real publisher:
  * Section emission flow: dispatcher emits raw responses, distributor fans out, matcher admits findings, the real `QuerySectionTracker` accumulates and emits each `SectionResultV2` exactly once through the pipeline, and the real `QuerySectionPublisher` publishes each section through a capturing `IAdapterExecutionContext`.
  * Single-consumer enforcement: a focused test asserts that a second call to `tracker.CompletedSections` on the same tracker instance throws `InvalidOperationException`. The pipeline-driven path consumes the stream exactly once.
  * Cancellation propagation: cancelling while the tracker awaits store transitions surfaces `OperationCanceledException` and the pipeline returns `AdapterResult.FailureResult` with `ErrorCode` `OPERATION_CANCELLED`.
  * `QueryJobCompletedV2` emission at job end: after the tracker stream completes, the publisher receives a single `QueryJobCompletedV2` envelope with `JobId`, `SectionsPublished`, `ExecutionUnitsRun`, `OriginalQueryCount`, and `Outcome` derived from the executed plan and published sections. Cover `QueryJobOutcome.Succeeded`, `QueryJobOutcome.Partial`, and `QueryJobOutcome.Failed`.
* `Dataflow/README.md` and `Publishing/Query/README.md` updated to describe the wired tracker -> publisher flow and the pipeline-owned `QueryJobCompletedV2` boundary. No structural change to SDK interfaces.
* Aggregate Query state.json updated to move "Wiring QuerySectionPublisher into the dataflow" out of `notImplemented`.
* Task state, execution notes, and global archive mirror updated.

Execution Rules:

* Do not assume missing data.
* Respect constraints strictly.
* Keep changes scoped to this publisher wiring slice.
* Work with existing user changes and do not revert unrelated edits.
* Update `state.json` as step status or verification status changes.

Output Format:
Final response must include what changed, validation results, and any blocker or residual risk.

Stop Conditions:

* When goal is achieved.
* When required data is missing.
* When SDK contract limitations prevent a required behavior without changing SDK source.

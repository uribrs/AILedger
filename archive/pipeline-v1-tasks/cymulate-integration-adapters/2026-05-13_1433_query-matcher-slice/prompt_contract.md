Role:
You are a senior .NET engineer working in the Cymulate adapters Shared Query domain.

Goal:
Implement the concrete Shared Query `IMatcher` slice as an admission-only stage that converts each SDK `DistributedResponse` into one SDK `Finding` for the matched logical `QueryV2`.

Context:
The Query planning stages, Dataflow topology, unit dispatcher, and distributor are already implemented. `QueryAdapterPipeline` currently depends on the SDK `IMatcher` contract but no concrete Shared matcher exists. SDK exposes no structured expression dialect, payload-side targeting schema, or payload-side result-type field, so structured filtering cannot be implemented in this slice without inventing semantics. The matcher is therefore an admission stage that produces findings from already-distributed responses; expression/IP/type filtering is deferred until SDK exposes structured rules.

Constraints:

* Keep implementation under `Orchestration/Query/Dataflow`.
* Use SDK Query contracts directly.
* Implement only `IMatcher` behavior in this slice.
* Emit exactly one SDK `Finding` per input `DistributedResponse`.
* Take `JobId` from the method parameter, `SectionId` and `QueryId` from `DistributedResponse`, `ResultType` from the matched `QueryV2`, and `Payload` from the inner `RawResponse.Payload`.
* Fail clearly when `distributed.QueryId` does not equal `query.QueryId`.
* Respect cancellation; honor the token before emitting the finding.
* Do not implement expression, targeting, or result-type filtering in this slice.
* Do not branch on `IAdapterSideMatchingCapability` inside the matcher; that swap lives in pipeline composition.
* Do not implement section tracker, section publisher, recovery planner, resume runner, or `IExecutionPlanStore`.
* Do not add real Query clients or fake Query clients as product code.
* Do not add RabbitMQ, Postgres, outbox, storage, session, HTTP retry, host, or transport infrastructure.
* Keep code small, focused, and aligned with existing Shared Query style.

Success Criteria:

* A concrete Shared class implements SDK `IMatcher`.
* Each call emits exactly one `Finding` populated as specified above.
* `Finding.JobId` equals the method parameter; `Finding.SectionId` and `Finding.QueryId` equal the `DistributedResponse` values; `Finding.ResultType` equals `QueryV2.ResultType`; `Finding.Payload` references the same `JsonElement` as the inner `RawResponse.Payload`.
* `distributed.QueryId != query.QueryId` fails with a clear exception.
* Cancellation propagates as `OperationCanceledException`.
* Focused tests cover admission, `ResultType` propagation, `JobId`/`SectionId`/`QueryId` propagation, `Payload` pass-through, `QueryId` mismatch failure, and cancellation.
* Targeted Shared build and Query shared tests pass, or any failure is documented with evidence.
* Task state, execution notes, and aggregate handoff state are updated.

Execution Rules:

* Do not assume missing data.
* Respect constraints strictly.
* Keep changes scoped to this matcher slice.
* Work with existing user changes and do not revert unrelated edits.
* Update `state.json` as step status or verification status changes.

Output Format:
Final response must include what changed, validation results, and any blocker or residual risk.

Stop Conditions:

* When goal is achieved.
* When required data is missing.
* When SDK contract limitations prevent a required behavior without changing SDK source.

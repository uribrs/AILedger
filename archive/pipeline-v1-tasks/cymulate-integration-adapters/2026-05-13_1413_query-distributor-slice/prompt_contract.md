Role:
You are a senior .NET engineer working in the Cymulate adapters Shared Query domain.

Goal:
Implement the concrete Shared Query `IDistributor` slice for fan-out from physical raw responses to represented logical query ids and section ids.

Context:
The Query planning stages, Dataflow topology, and unit dispatcher are already implemented. `QueryAdapterPipeline` currently depends on the SDK `IDistributor` contract but no concrete Shared distributor exists. The distributor is the next bounded stage and must remain focused on plan-index lookup and raw-response fan-out.

Constraints:

* Keep implementation under `Orchestration/Query/Dataflow`.
* Use SDK Query contracts directly.
* Implement only `IDistributor` behavior in this slice.
* Build distribution mappings from `ExecutionPlan.TimeWindowUnits`, `ExecutionPlan.NativeUnits`, and `ExecutionPlan.SectionMembership`.
* Fan each `RawResponse` out to every represented query id for its unit.
* Emit one SDK `DistributedResponse` per section/query/raw-response target.
* Preserve the original `RawResponse` instance in emitted distributed responses.
* Fail clearly when a raw response references an unknown unit id.
* Fail clearly when a represented query id has no owning section in `SectionMembership`.
* Respect cancellation while consuming input responses and emitting fan-out items.
* Do not implement matcher, tracker, recovery persistence, real Query clients, fake Query clients, or external infrastructure.

Success Criteria:

* A concrete Shared class implements SDK `IDistributor`.
* Time-window and native units are both indexed.
* Each input `RawResponse` fans out to all represented query ids and their owning section ids.
* The original `RawResponse` is preserved in each emitted `DistributedResponse`.
* Unknown unit ids fail with a clear exception.
* Represented query ids missing from section membership fail with a clear exception.
* Cancellation stops input enumeration/fan-out and propagates as cancellation.
* Focused tests cover time-window fan-out, native fan-out, multi-section ownership, unknown unit id, missing section membership, streaming, and cancellation.
* Targeted Shared build and Query shared tests pass, or any failure is documented with evidence.
* Task state, execution notes, and aggregate handoff state are updated.

Execution Rules:

* Do not assume missing data.
* Respect constraints strictly.
* Keep changes scoped to this distributor slice.
* Work with existing user changes and do not revert unrelated edits.
* Update `state.json` as step status or verification status changes.

Output Format:
Final response must include what changed, validation results, and any blocker or residual risk.

Stop Conditions:

* When goal is achieved.
* When required data is missing.
* When SDK contract limitations prevent a required behavior without changing SDK source.


Role:
You are a senior .NET engineer working in the Cymulate adapters Shared Query domain.

Goal:
Implement the concrete Shared Query `IUnitDispatcher` slice for dispatching planned Query execution units to an SDK `IQueryAdapter`.

Context:
The Query planning stages and Dataflow topology are already implemented. `QueryAdapterPipeline` currently depends on the SDK `IUnitDispatcher` contract but no concrete Shared dispatcher exists. The dispatcher is the next bounded stage and must remain focused on unit execution and raw-response streaming.

Constraints:

* Keep implementation under `Orchestration/Query/Dataflow`.
* Use SDK Query contracts directly.
* Implement only `IUnitDispatcher` behavior in this slice.
* Dispatch time-window units through `IQueryAdapter.ExecuteAsync`.
* Dispatch native units through `IQueryAdapter.ExecuteNativeAsync`.
* Enforce `ITimeWindowQueryCapability` and `INativeQueryCapability` capability markers before dispatching corresponding unit kinds.
* Use a bounded dispatcher concurrency default because current SDK `DispatchAsync` does not receive `QueryJobSettings`.
* Stream `RawResponse` values without materializing the full job result.
* Respect cancellation tokens while scheduling units and enumerating adapter response streams.
* Do not implement distributor, matcher, tracker, recovery persistence, real Query clients, or fake Query clients.
* Do not add external infrastructure or change SDK source from this repo.

Success Criteria:

* A concrete Shared class implements SDK `IUnitDispatcher`.
* Time-window units invoke `IQueryAdapter.ExecuteAsync` and native units invoke `IQueryAdapter.ExecuteNativeAsync`.
* Unsupported time-window or native units fail clearly before dispatching that unsupported kind.
* Unit dispatch uses a positive bounded concurrency option.
* Responses are yielded progressively from adapter async streams.
* Cancellation stops scheduling/enumeration and propagates as cancellation.
* Focused tests cover method selection, capability checks, bounded concurrency, streaming, cancellation, and adapter failures.
* Targeted Shared build and Query shared tests pass, or any failure is documented with evidence.
* Task state, execution notes, and aggregate handoff state are updated.

Execution Rules:

* Do not assume missing data.
* Respect constraints strictly.
* Keep changes scoped to this dispatcher slice.
* Work with existing user changes and do not revert unrelated edits.
* Update `state.json` as step status or verification status changes.

Output Format:
Final response must include what changed, validation results, and any blocker or residual risk.

Stop Conditions:

* When goal is achieved.
* When required data is missing.
* When SDK contract limitations prevent a required behavior without changing SDK source.


Role:
You are a senior .NET integration engineer implementing a bounded Shared Query Dataflow topology slice in the adapters repo.

Goal:
Add the smallest executable Query Dataflow topology composition that wires completed planning stages and SDK query pipeline stage contracts behind `IQueryAdapterPipeline`, without implementing vendor clients or unresolved downstream stage behavior.

Context:
The aggregate entrypoint is `/Users/user/Dev/cymulate-integration-adapters/ai/active/2026-05-13_1238_query-domain-creation/state.json`.
The completed planning stages are `QueryPlanBuilder`, `QueryPlanOptimizer`, and `NativeQueryCoalescer`.
The implementation target is `/Users/user/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query`.
SDK query contracts are described by `/Users/user/Dev/Uri/Planning/2026-05-11_1326_crowdstrike-domain-doc-validation/QUERY_INTEGRATION_SDK_CONTRACT_DESCRIPTOR.md`.

Constraints:

* Keep Shared concern-first; place topology code under `Orchestration/Query`.
* Use SDK Query pipeline contracts directly where available.
* Do not add a real Query Integration client.
* Do not add a fake vendor client as product code.
* Do not recreate RabbitMQ, Postgres, outbox, storage upload, session, HTTP retry, ServiceBus host, or transport infrastructure.
* Do not implement adapters-repo `IExecutionPlanStore` storage.
* Do not decide or implement `QueryV2.ExpressionDialect`, expanded cloud/SaaS `QueryTargeting`, composite `ResultType` policy, or native-query normalization changes.
* Dataflow composition must use explicit cancellation, bounded capacity, explicit parallelism, explicit ordering, and completion propagation.
* Publication must go through SDK `ISectionPublisher` and the existing Query publisher surface.
* Keep dispatcher, distributor, matcher, section tracker, and recovery planner implementations in separate future slices.
* Keep classes and methods small, focused, and consistent with existing Shared conventions.
* Preserve existing user changes.

Success Criteria:

* A bounded Query Dataflow topology composition is added under Shared Query infrastructure.
* The topology implements SDK `IQueryAdapterPipeline`.
* The topology creates a Query `AdapterProgressContext` and `QueryPipelineContext`.
* The topology composes `IPlanBuilder`, `IPlanOptimizer`, and `INativeCoalescer` in order.
* The topology uses injected SDK-stage collaborators for dispatch, distribution, matching, tracking, and publication.
* The topology uses explicit bounded capacity, explicit parallelism, explicit ordering, cancellation tokens, and propagated completion.
* One section tracker is created per execution plan through a local factory boundary.
* Job-completed publication summarizes sections published, execution units run, original query count, start/completion timestamps, and outcome.
* Focused tests cover option validation, ordered stage execution, query progress context creation, publication flow, cancellation/failure propagation, and failed publication outcome behavior.
* Source verification confirms no real or fake Query client product code was added.
* Build or targeted tests are run, or any inability to run them is documented.
* Task state and execution notes are updated after execution.

Execution Rules:

* Do not assume missing data.
* Respect constraints strictly.
* Keep changes scoped to this topology composition slice.
* Work with existing user changes and do not revert unrelated edits.
* Update task state and execution notes after execution.

Output Format:
Final response should summarize files changed, verification results, and remaining open questions or risks.

Stop Conditions:

* Stop when the topology composition slice is implemented and verified.
* Stop if required SDK or Shared source paths are missing.
* Stop if implementation requires a real product/client decision.
* Stop if SDK contracts require a source change outside this adapters repo before this slice can be implemented safely.

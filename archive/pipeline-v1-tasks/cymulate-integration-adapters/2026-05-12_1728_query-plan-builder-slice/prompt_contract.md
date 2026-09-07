Role:
You are a senior .NET integration engineer implementing the next Query Integration Shared/Dataflow infrastructure slice in the adapters repo.

Goal:
Add a minimal Shared Query `IPlanBuilder` implementation that converts SDK `QueryJobV2` jobs into SDK `ExecutionPlan` values with focused validation and deterministic unit ids, without implementing later query pipeline stages.

Context:
Previous task state: `/Users/user/Dev/cymulate-integration-adapters/ai/active/2026-05-12_1420_query-integration-shared-dataflow`.
Primary planning source: `/Users/user/Dev/Uri/Planning/2026-05-11_1326_crowdstrike-domain-doc-validation/QUERY_INTEGRATION_SHARED_DATAFLOW_BLUEPRINT_PRD.md`.
SDK descriptor: `/Users/user/Dev/Uri/Planning/2026-05-11_1326_crowdstrike-domain-doc-validation/QUERY_INTEGRATION_SDK_CONTRACT_DESCRIPTOR.md`.
Implementation target: `/Users/user/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared`.
SDK source: `/Users/user/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Sdk/Cymulate.Integration.Sdk/Query`.

Constraints:

* Keep Shared concern-first; place plan-building code under `Orchestration/Query/Planning`.
* Implement SDK `IPlanBuilder`; do not create a duplicate local plan-builder contract.
* Do not add a real Query Integration client.
* Do not add a fake vendor client as product code.
* Do not implement optimizer, dispatcher, distributor, matcher, section tracker, Dataflow topology, recovery planner, or plan-store persistence in this slice.
* Do not recreate RabbitMQ, Postgres, outbox, storage upload, session, HTTP retry, ServiceBus host, or transport infrastructure.
* Do not decide or implement `QueryV2.ExpressionDialect`, expanded cloud/SaaS `QueryTargeting`, or composite `ResultType` policy.
* Do not reject composite `QueryV2.ResultType` values in this slice.
* Keep classes and methods small, focused, and consistent with existing Shared conventions.
* Preserve existing user changes.

Success Criteria:

* A Query plan builder implementing SDK `IPlanBuilder` is added under Shared Query infrastructure.
* The builder returns SDK `ExecutionPlan` with job id, client id, product, section membership, created timestamp, time-window units, and native units.
* Time-window queries produce SDK `ExecutionUnit` values preserving query id, result type, time window, targeting, and result limit.
* Native queries produce SDK `NativeExecutionUnit` values preserving query id, result type, trimmed native expression, and result limit.
* Unit ids are deterministic and change when relevant unit content changes.
* Validation rejects null/empty jobs, duplicate section ids, duplicate query ids, missing time windows for time-window queries, invalid time-window ordering, blank native expressions, and unsupported query kinds.
* Composite `ResultType` values are not rejected solely because they are composite.
* No real or fake query client is added.
* No later pipeline stage or replacement infrastructure is added.
* Focused tests cover introduced behavior.
* Build or targeted tests are run, or any inability to run them is documented.
* Task state and execution notes are updated after execution.

Execution Rules:

* Do not assume missing data.
* Respect constraints strictly.
* Keep changes scoped to this plan-builder slice.
* Work with existing user changes and do not revert unrelated edits.
* Update task state and execution notes after execution.

Output Format:
Final response should summarize files changed, verification results, and remaining open questions or risks.

Stop Conditions:

* Stop when the plan-builder slice is implemented and verified.
* Stop if required SDK or Shared source paths are missing.
* Stop if implementation requires a real product/client decision.
* Stop if SDK contracts require a source change outside this adapters repo before this slice can be implemented safely.

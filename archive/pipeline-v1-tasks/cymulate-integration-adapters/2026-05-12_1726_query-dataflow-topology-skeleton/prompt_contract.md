Supersession Notice:
This prompt contract is no longer active. It was superseded before product-code execution by `/Users/user/Dev/cymulate-integration-adapters/ai/active/2026-05-12_1728_query-plan-builder-slice`, which implemented and verified the Query `IPlanBuilder` slice. Do not execute topology work directly from this contract. Use this file only as historical context for future topology planning.

Role:
You are a senior .NET integration engineer implementing the next Query Integration Shared/Dataflow infrastructure slice in the adapters repo.

Goal:
Historical goal only: add the smallest bounded Query Dataflow topology skeleton that composes existing SDK query pipeline stages and existing Shared query publication infrastructure without implementing vendor clients or unresolved query-domain policies.

Current status:
The immediate next slice changed after SDK/PRD review. Query `IPlanBuilder` was implemented first and is now complete. Future topology work should start from the completed plan-builder documentation and should probably wait for `IPlanOptimizer` and `INativeCoalescer`.

Context:
Previous task state: `/Users/user/Dev/cymulate-integration-adapters/ai/active/2026-05-12_1420_query-integration-shared-dataflow`.
Primary planning source: `/Users/user/Dev/Uri/Planning/2026-05-11_1326_crowdstrike-domain-doc-validation/QUERY_INTEGRATION_SHARED_DATAFLOW_BLUEPRINT_PRD.md`.
SDK descriptor: `/Users/user/Dev/Uri/Planning/2026-05-11_1326_crowdstrike-domain-doc-validation/QUERY_INTEGRATION_SDK_CONTRACT_DESCRIPTOR.md`.
Implementation target: `/Users/user/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared`.
SDK source: `/Users/user/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Sdk/Cymulate.Integration.Sdk/Query`.

Constraints:

* Keep Shared concern-first; place query topology code under the owning Shared concern.
* Prefer existing SDK Query contracts over local duplicate contracts.
* Do not add a real Query Integration client.
* Do not add a fake vendor client as product code.
* Do not recreate RabbitMQ, Postgres, outbox, storage upload, session, HTTP retry, ServiceBus host, or transport infrastructure.
* Do not implement adapters-repo `IExecutionPlanStore` storage.
* Do not decide or implement `QueryV2.ExpressionDialect`, expanded cloud/SaaS `QueryTargeting`, or composite `ResultType` policy.
* Dataflow blocks must use explicit cancellation, bounded capacity, explicit parallelism, explicit ordering, and completion propagation.
* Query section/job output publication must go through existing `IAdapterExecutionContext.PublishAsync` surfaces via the existing Query publisher.
* Keep classes and methods small, focused, and consistent with existing Shared conventions.
* Preserve existing user changes.

Success Criteria:

* A bounded Query Dataflow topology skeleton is added under Shared Query infrastructure.
* The skeleton composes existing SDK query stage contracts where it executes stage behavior.
* Dataflow options validate positive bounded capacity and explicit parallelism.
* Blocks pass cancellation tokens to stage calls.
* Blocks link with completion propagation.
* Ordered planning/context stages and serialized publication/checkpoint-sensitive stages are explicit.
* No real or fake query client is added.
* No replacement platform infrastructure or plan-store persistence is added.
* Focused tests cover introduced topology behavior.
* Build or targeted tests are run, or any inability to run them is documented.
* Task state and execution notes are updated after execution.

Execution Rules:

* Do not assume missing data.
* Respect constraints strictly.
* Keep changes scoped to this topology skeleton slice.
* Work with existing user changes and do not revert unrelated edits.
* Update task state and execution notes after execution.

Output Format:
Final response should summarize files changed, verification results, and remaining open questions or risks.

Stop Conditions:

* Stop when the topology skeleton slice is implemented and verified.
* Stop if required SDK or Shared source paths are missing.
* Stop if implementation requires a real product/client decision.
* Stop if SDK contracts require a source change outside this adapters repo before this slice can be implemented safely.

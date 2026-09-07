Role:
You are a senior .NET integration engineer implementing the next Query Integration Shared/Dataflow planning slice in the adapters repo.

Goal:
Add a minimal Shared Query `IPlanOptimizer` implementation that merges equivalent SDK time-window `ExecutionUnit` entries in an `ExecutionPlan`, while preserving native units, section membership, and plan metadata.

Context:
Previous completed slice: `/Users/user/Dev/cymulate-integration-adapters/ai/active/2026-05-12_1728_query-plan-builder-slice`.
Primary planning source: `/Users/user/Dev/Uri/Planning/2026-05-11_1326_crowdstrike-domain-doc-validation/QUERY_INTEGRATION_SHARED_DATAFLOW_BLUEPRINT_PRD.md`.
SDK descriptor: `/Users/user/Dev/Uri/Planning/2026-05-11_1326_crowdstrike-domain-doc-validation/QUERY_INTEGRATION_SDK_CONTRACT_DESCRIPTOR.md`.
Implementation target: `/Users/user/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Planning`.
SDK source: `/Users/user/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Sdk/Cymulate.Integration.Sdk/Query`.

Constraints:

* Keep Shared concern-first; place optimizer code under `Orchestration/Query/Planning`.
* Implement SDK `IPlanOptimizer`; do not create a duplicate local optimizer contract.
* Merge only time-window `ExecutionUnit` values sharing plan `Product`, `ResultType`, `TimeWindow`, and `Targeting`.
* Compare optimized identity using canonical targeting and UTC time-window values, while relying on the plan builder for validation.
* Accumulate `RepresentedQueryIds` from merged time-window units.
* Preserve merged time-window result-limit semantics by using the least restrictive cap: `null` if any represented unit is unlimited, otherwise the maximum configured limit.
* Generate deterministic merged unit ids from the optimized unit identity.
* Do not merge or alter `NativeExecutionUnit` values in this slice.
* Do not mutate the input `ExecutionPlan` or existing unit instances.
* Preserve `JobId`, `ClientId`, `Product`, `CreatedAt`, `SectionMembership`, and native units.
* Do not implement `INativeCoalescer`, dispatcher, distributor, matcher, section tracker, Dataflow topology, recovery planner, or plan-store persistence.
* Do not add real or fake Query Integration clients.
* Do not decide composite `ResultType`, `ExpressionDialect`, expanded `QueryTargeting`, or native normalization policy.
* Keep methods small, focused, and consistent with existing Shared conventions.
* Preserve existing user changes.

Success Criteria:

* A Query plan optimizer implementing SDK `IPlanOptimizer` is added under Shared Query planning infrastructure.
* Equivalent time-window units are merged by plan product, result type, time window, and targeting.
* Merged time-window units accumulate all represented query ids deterministically.
* Merged time-window units keep the least restrictive result limit from represented units.
* Merged unit ids are deterministic and change when optimized unit identity changes.
* Non-equivalent time-window units remain separate.
* Native units are preserved unchanged.
* Section membership is preserved unchanged.
* Plan metadata is preserved unchanged.
* The optimizer respects cancellation during non-trivial loops.
* Focused tests cover no-op behavior, merge behavior, represented query id accumulation, native preservation, section membership preservation, deterministic unit ids, identity changes, and cancellation.
* Build or targeted tests are run, or any inability to run them is documented.
* Task state and execution notes are updated after execution.

Execution Rules:

* Do not assume missing data.
* Respect constraints strictly.
* Keep changes scoped to this optimizer slice.
* Work with existing user changes and do not revert unrelated edits.
* Update task state and execution notes after execution.

Output Format:
Final response should summarize files changed, verification results, and remaining open questions or risks.

Stop Conditions:

* Stop when the optimizer slice is implemented and verified.
* Stop if required SDK or Shared source paths are missing.
* Stop if implementation requires a real product/client decision.
* Stop if SDK contracts require a source change outside this adapters repo before this slice can be implemented safely.

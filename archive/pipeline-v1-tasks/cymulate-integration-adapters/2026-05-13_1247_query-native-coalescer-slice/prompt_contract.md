Role:
You are a senior .NET integration engineer implementing the next Query Integration Shared planning slice in the adapters repo.

Goal:
Add a minimal Shared Query `INativeCoalescer` implementation that coalesces equivalent SDK native `NativeExecutionUnit` entries in an `ExecutionPlan`, while preserving time-window units, section membership, and plan metadata.

Context:
Aggregate Query state: `/Users/user/Dev/cymulate-integration-adapters/ai/active/2026-05-13_1238_query-domain-creation/state.json`.
Previous completed optimizer slice: `/Users/user/Dev/cymulate-integration-adapters/ai/active/2026-05-13_1154_query-plan-optimizer-slice`.
Primary planning source: `/Users/user/Dev/Uri/Planning/2026-05-11_1326_crowdstrike-domain-doc-validation/QUERY_INTEGRATION_SHARED_DATAFLOW_BLUEPRINT_PRD.md`.
SDK descriptor: `/Users/user/Dev/Uri/Planning/2026-05-11_1326_crowdstrike-domain-doc-validation/QUERY_INTEGRATION_SDK_CONTRACT_DESCRIPTOR.md`.
Implementation target: `/Users/user/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Planning`.
SDK source: `/Users/user/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Sdk/Cymulate.Integration.Sdk/Query`.

Constraints:

* Keep Shared concern-first; place coalescer code under `Orchestration/Query/Planning`.
* Implement SDK `INativeCoalescer`; do not create a duplicate local coalescer contract.
* Group only native `NativeExecutionUnit` values sharing plan `Product`, `NormalizedQuery`, and `ResultType`.
* Accumulate `RepresentedQueryIds` from coalesced native units.
* Preserve native normalized query text exactly; do not deepen native normalization.
* Preserve merged native result-limit semantics by using the least restrictive cap: `null` if any represented unit is unlimited, otherwise the maximum configured limit.
* Generate deterministic coalesced native unit ids from product, normalized query, and result type.
* Do not merge or alter time-window `ExecutionUnit` values in this slice.
* Do not mutate the input `ExecutionPlan` or existing unit instances.
* Preserve `JobId`, `ClientId`, `Product`, `CreatedAt`, `SectionMembership`, and time-window units.
* Keep `INativeCoalescingOptOut` as a composition-level concern; do not add adapter capability detection to this class.
* Do not implement Dataflow topology, dispatcher, distributor, matcher, section tracker, recovery planner, or plan-store persistence.
* Do not add real or fake Query Integration clients.
* Do not decide composite `ResultType`, `ExpressionDialect`, expanded `QueryTargeting`, or deeper native normalization policy.
* Keep methods small, focused, and consistent with existing Shared conventions.
* Preserve existing user changes.

Success Criteria:

* A Query native coalescer implementing SDK `INativeCoalescer` is added under Shared Query planning infrastructure.
* Equivalent native units are coalesced by plan product, normalized query, and result type.
* Coalesced native units accumulate all represented query ids deterministically.
* Coalesced native unit ids are deterministic and change when coalesced native identity changes.
* Non-equivalent native units remain separate.
* Time-window units are preserved unchanged.
* Section membership is preserved unchanged.
* Plan metadata is preserved unchanged.
* Coalescer respects cancellation during non-trivial loops.
* Focused tests cover no-op behavior, coalescing behavior, represented query id accumulation, time-window preservation, section membership preservation, deterministic unit ids, identity changes, result-limit merge policy, and cancellation.
* Build or targeted tests are run, or any inability to run them is documented.
* Query README/planning docs and aggregate Query state are updated after execution.
* Task state and execution notes are updated after execution.

Execution Rules:

* Do not assume missing data.
* Respect constraints strictly.
* Keep changes scoped to this native coalescer slice.
* Work with existing user changes and do not revert unrelated edits.
* Update task state and execution notes after execution.

Output Format:
Final response should summarize files changed, verification results, review results, documentation updates, and remaining open questions or risks.

Stop Conditions:

* Stop when the native coalescer slice is implemented, documented, reviewed, and verified.
* Stop if required SDK or Shared source paths are missing.
* Stop if implementation requires a real product/client decision.
* Stop if SDK contracts require a source change outside this adapters repo before this slice can be implemented safely.

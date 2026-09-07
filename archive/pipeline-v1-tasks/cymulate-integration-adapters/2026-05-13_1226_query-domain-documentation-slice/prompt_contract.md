Role:
You are a senior .NET integration engineer documenting the Query domain in the adapters Shared project.

Goal:
Create the first practical README/documentation pass for Shared Query infrastructure, reflecting the completed plan-builder and plan-optimizer slices while clearly marking unfinished pipeline stages.

Context:
Completed plan-builder slice: `/Users/user/Dev/cymulate-integration-adapters/ai/active/2026-05-12_1728_query-plan-builder-slice`.
Completed plan-optimizer slice: `/Users/user/Dev/cymulate-integration-adapters/ai/active/2026-05-13_1154_query-plan-optimizer-slice`.
Primary docs target: `/Users/user/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared`.

Constraints:

* Document only behavior that exists or is explicitly planned in completed handoff docs.
* Keep unfinished components clearly marked as not implemented.
* Do not modify product code in this slice.
* Keep docs close to the owning concern folders.
* Preserve the concern-first Shared structure.
* Do not imply that the end-to-end Query Dataflow pipeline is runnable.
* Do not resolve open product/SDK questions such as composite result types, expression dialects, cloud/SaaS targeting, or native normalization depth.
* Keep prose concise and useful for the next implementer.

Success Criteria:

* `Orchestration/Query/README.md` explains current Query domain scope, implemented pieces, missing pieces, and next implementation sequence.
* `Orchestration/Query/Planning/README.md` documents `QueryPlanBuilder`, `QueryPlanOptimizer`, unit-id semantics, validation boundaries, and planned `INativeCoalescer`.
* `Publishing/Query/README.md` documents Query publication helpers and target path semantics.
* `Recovery/Query/README.md` documents current checkpoint-key status and deferred recovery ownership.
* Documentation distinguishes completed behavior from future work.
* Documentation does not introduce product behavior that is absent from code or handoff docs.
* Task state and execution notes are updated after documentation changes.

Execution Rules:

* Do not assume missing data.
* Respect constraints strictly.
* Work with existing user changes and do not revert unrelated edits.
* Use Markdown only for this slice.

Output Format:
Final response should summarize documentation files changed, verification performed, and remaining documentation gaps.

Stop Conditions:

* Stop when the first Query domain documentation pass is complete.
* Stop if documentation requires resolving a product/SDK policy decision.

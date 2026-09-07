# Orchestration Plan

## Complexity Decision
- Path: **decompose**
- Rationale: cross-repo behavioral mirror against a large (~1144-line), unfamiliar legacy engine with a different architecture than the spec. The engine must be mapped (read-only) before a mirror can be implemented without re-introducing an architecture miss — and the adapter→agent-service change-map is itself a user-requested deliverable ("map the changes"). Clean gate between mapping (W1) and implementation (W2).

## Research Decisions
- **None external.** External Tenable behavior (endpoints/filters/ACR) is settled — A5 VALIDATED from the adapter + live data. The co-located dual-lane requirement is settled — A6 VALIDATED.
- OPEN assumptions A1 (C1 mapping), A3 (engine/resume/egress), A4 (run-mode dispatch) are **codebase-internal** to the AgentService target → resolved by W1's read-only code exploration, not `technical-researcher`.

## Worker Plan
- **W1 — engine map + change plan (read-only).** scope: read the AgentService `TenableCollector/TenableIoCollector.cs` (+ neighbours/host contracts it depends on) and cross-reference the adapter `Documentation/`. Produce a concrete adapter→agent-service change-map: where the info filter lives, where enrichment lives, the chunk engine + resume/checkpoint mechanism, the batchful-upload egress, the run-mode/topic dispatch, and exactly how to (1) add the assets lane, (2) drop the info filter, (3) remove enrichment, (4) source ACR from ratings.acr.score, (5) co-locate both lanes for a combined run, (6) whether the C1 mid-chunk-resume duplication exposure exists here and how to mirror the fix intent. Resolve A1/A3/A4. inputs: adapter Documentation/, target repo. output: `research/engine-map-and-change-plan.md`. **Gates W2.**
- **W2 — implement + document the mirror.** scope: apply the change-map to the AgentService collector (behavioral mirror, adapt to engine; no line copy); add an agent-service mirror `Documentation/` equivalent to the adapter's. inputs: W1.output, adapter spec. output: code + docs + `execution_notes.md#w2`. dependencies: W1.

## Synthesis Approach
After W1: reconcile its change-map against the adapter spec and the contract Success Criteria; confirm it covers all 5 behavioral changes + the C1 scoping before authorizing W2. After W2: confirm each adapter behavior mapped to a concrete agent-service change and that resumability/upload is preserved, before the verifier.

## Verification Obligations
- Cross-check against prompt_contract.md Success Criteria.
- Confirm from code: assets lane added (`/assets/export`, last_assessed 30d); info-severity filter removed (all severities); `/assets/{id}` enrichment removed; ACR from `ratings.acr.score`; both lanes co-located for a combined run; assets-only run emits assets only.
- Resumability / batchful upload preserved (no regression); C1 exposure scoped and handled or recorded as accepted risk.
- Builds (or, if no local build for the AgentService solution, inspection + spec conformance — note explicitly).
- Agent-service mirror documentation present.

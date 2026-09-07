# Orchestration Plan

## Complexity Decision
- Path: decompose
- Rationale: Five engine work items with sharp, mostly file-disjoint responsibilities and
  a user preference for per-responsibility subagents. Coupling exists only through the
  shared schema file and the IntegrationEngine page loop, handled by sequencing (below).
  Cross-repo synthesis (YAML consumption, schema re-sync, live parity run, comparison)
  stays centralized in the main thread.

## Research Decisions
- None needed. All five items are grounded in live-run evidence and direct code reading
  recorded in task.md; OPEN assumptions A2/A3/A4 are executor design choices inside the
  engine, A5 is an optional stretch with safe degradation, A7 is a local artifact check.

## Worker Plan
Baseline: engine test project green at start (605/605, run 2026-07-21).

- W1 — scope: item 1, paginator recovery-state carry bug fix + paginator audit + tests.
  inputs: Pagination/*.cs, PaginationState, cursor_recovery flow in IntegrationEngine (read-only).
  output: fixed paginators + unit tests proving floor/watermark survive page advance.
  files: Pagination/ + test project only. NO schema edits. dependencies: none.
- W2 — scope: item 2, merge_into `group: true` opt-in N:1 fan-in + loader + schema + tests.
  inputs: Workflow/MergeEnrichmentSink.cs, Workflow/MergeEnrichment.cs, Models/WorkflowConfig.cs,
  Loader/YamlIntegrationLoader.cs, Schemas/integration.schema.json.
  output: grouped-embed mode; flag-off path byte-identical; tests both ways.
  dependencies: none (parallel with W1).
- W3 — scope: item 3, response-mapping `first_of` + `regex_extract` transform + schema + tests.
  inputs: Mapping/ResponseMapper.cs, Mapping/FieldTransformRegistry.cs (or equivalent),
  Models/ResponseConfig.cs / MappingTransformConfig.cs, Schemas/integration.schema.json.
  output: mapping value form `first_of` (merge-shape semantics) and registered
  `regex_extract` transform; tests. dependencies: W2 (shared schema file — sequenced to
  avoid concurrent edits, no logical dependency).
- W4 — scope: items 4+5, opt-in one-page prefetch + opt-in scroll-completeness assertion,
  PaginationConfig + IntegrationEngine page loop + loader + schema + tests.
  output: `prefetch` flag (fetch N+1 before sink processes N; buffer consumed next
  iteration; Polly at prefetch time, error-rule classification at consumption) and a
  completeness assertion (cursor scroll ends without cursor + collected < total path ⇒
  operation failure). Flag-off loop byte-identical. dependencies: W3 (shared schema file;
  also sole owner of page-loop edits).

All workers: adapters repo working tree, run the engine test project green before
finishing, return a structured report (files touched, behavior notes, test evidence,
proposed execution_notes entry). Workers do NOT write task-state files, do NOT edit
crowdstrike-falcon.yaml, do NOT re-sync the magic-integration schema, do NOT run live
vendor collections, and do NOT redesign the plan.

## Synthesis Approach
Main thread after W4: update crowdstrike-falcon.yaml to consume the new capabilities;
re-sync the engine schema to cymulate-magic-integration/schemas/; run both repos' test
gates; execute the live parity rerun (base 2026-07-15) via the scratchpad harness;
produce the comparison report vs the native baseline; append execution_notes.md.

## Verification Obligations
- Cross-check against prompt_contract.md Success Criteria (test gates, parity numbers,
  non-null aid, findings arrays, comparison report, booked-delta list updated).
- Explicitly verify flag-off invariance claims: engine suite green + qualys/insightvm
  YAMLs still load and their code paths untouched.
- Verifier subagent → review/verifier-N.md; code-reviewer subagent (minimal context)
  → review/code-reviewer-N.md.

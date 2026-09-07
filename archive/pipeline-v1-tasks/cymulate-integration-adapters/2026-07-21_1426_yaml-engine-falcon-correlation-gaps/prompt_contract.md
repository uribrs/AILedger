# Prompt Contract

Role:
You are a senior .NET engineer working on the Cymulate YAML integration engine
(`Cymulate.Integration.Yaml.Engine`, adapters repo), extending it under strict
backward-compatibility discipline.

Goal:
Make the declarative CrowdStrike Falcon correlated-findings flow (spine + merge)
produce native-equivalent output on a live lab-tenant run: full host set, non-null
aid on every envelope, findings embedded as per-host arrays, per-host finding counts
matching the native baseline modulo live drift.

Context:
- Engine: `/Users/user/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters/Collectors/YamlCollector/Cymulate.Integration.Yaml.Engine/`
  Key files: `Pagination/CursorPaginator.cs` (UpdateState drops RecoveryFloor/Watermark),
  `Workflow/MergeEnrichmentSink.cs` + `Workflow/MergeEnrichment.cs` (single-slot
  last-wins match dictionary), `Mapping/ResponseMapper.cs` + `Mapping/FieldTransformRegistry.cs`
  (transform registry: severity_map, parse_datetime, template), `IntegrationEngine.cs`
  (page loop: fetch → sink.PublishBatchAsync (runs merges) → AdvancePagination),
  `Loader/YamlIntegrationLoader.cs` (validation gates), `Schemas/integration.schema.json`.
- Tests: `…/UnitTests/Collectors/Cymulate.Integration.Yaml.Engine.Test/`.
- Consumer YAML: `/Users/user/Dev/cymulate-magic-integration/integrations/crowdstrike-falcon.yaml`
  (workflow: assets stage → hosts spine (topic findings, mapping {aid, host: $self},
  annotate chunk/isLastChunk) → merge stage batch_size 250 on `aid = aid` as `findings`).
- Five work items and their live-run evidence: see `task.md` in this directory.
- Native baseline: `logs/published-batches/20260721-125550/collector-run`.
- Parity harness: scratchpad `yamlload` project (see assumptions A7 for rebuild recipe).

Constraints:
- All items in `constraints.md` apply verbatim. Headliners:
  - Flag-off engine = bit-for-bit today's behavior; changes gated by opt-in flags /
    new registry entries / additive schema.
  - qualys.yaml, insightvm-cloud.yaml, and the 93 cursor-strategy YAMLs must pass
    existing tests unchanged.
  - New YAML syntax reuses `first_of` and `transform: {type}` idioms only.
  - Schema re-synced to cymulate-magic-integration after engine schema changes.
  - Never print credential values.

Success Criteria:
- Engine unit tests: new tests cover each item (flag off = old behavior, flag on = new
  behavior); full engine test project green; adapters solution test run green.
- cymulate-magic-integration: crowdstrike-falcon.yaml loads through the adapters
  engine loader with schema validation on; magic-integration catalog/schema tests green
  (with `-p:TreatWarningsAsErrors=false`).
- Live parity rerun (base date 2026-07-15T00:00:00Z):
  - hosts collected == vendor meta.pagination.total at run time (was 358);
  - every findings-lane envelope has non-null, non-empty `aid`;
  - `findings` is a JSON array on every envelope ([] allowed);
  - sum of findings ≈ native 35,651 modulo live drift;
  - per-host finding counts match native for overlapping aids (report distribution
    of diffs; attribute non-zero diffs to live drift or explain).
- A written comparison report (execution_notes.md or review/) with the numbers.
- Booked-delta list updated in the YAML header (what's now closed vs still open).

Execution Rules:
- Do not assume missing data; validate engine behavior by reading the code before
  changing it.
- Respect constraints strictly; if a constraint blocks an item, stop and surface.
- Implementation order: 1 → 2 → 3 → 4 → 5, then YAML + schema sync, then tests,
  then live rerun.
- Decompose to subagents per responsibility where practical (per-item workers with
  the engine-test gate run after each).
- Update `state.json` step statuses and append `execution_notes.md` as you go.

Output Format:
- Code changes in the adapters repo (+ YAML/schema in cymulate-magic-integration).
- `execution_notes.md` appended per item: what changed, files touched, test evidence.
- Final comparison report with the parity numbers and remaining deltas.

Stop Conditions:
- Goal achieved (success criteria met, reports written).
- A hard constraint cannot be honored (e.g. an item cannot be built without touching
  a default path) — stop and surface the conflict.
- Live run blocked (credentials/tenant/vendor errors that retries don't clear).

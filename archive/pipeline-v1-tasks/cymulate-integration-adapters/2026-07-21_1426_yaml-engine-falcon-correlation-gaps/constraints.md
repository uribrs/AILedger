# Constraints

- No modification of default engine code paths: every behavior change sits behind an
  opt-in flag or a new registry/schema entry. Flag-off engine behavior must be
  bit-for-bit today's behavior.
- Existing consumers must pass existing tests unchanged: qualys.yaml,
  insightvm-cloud.yaml (merge_into), 93 YAMLs on `strategy: cursor`.
- New YAML syntax must reuse existing idioms only: `first_of` value form,
  `transform: {type: …}` registry shape. No new syntax families.
- Engine changes land in the adapters repo
  (`Cymulate.Integration.Yaml.Engine` + its `Schemas/integration.schema.json` + its test project).
- ADR-0003 dual-home discipline: after engine schema changes, re-sync the schema copy to
  `cymulate-magic-integration/schemas/integration.schema.json`.
- `dotnet test` must pass in the adapters repo (engine test project at minimum;
  do not break the wider suite).
- `cymulate-magic-integration` schema/catalog tests must pass after re-sync; dotnet
  commands there require `-p:TreatWarningsAsErrors=false` (user-level NuGet source
  triggers NU1507; do NOT edit the user's global NuGet.Config).
- Byte parity is NOT this task's bar. Four byte-level deltas stay booked/accepted:
  `findingsInChunk`, record key order, `apps`/`suppression_info` strip,
  `remediation.entities` sort.
- Live parity run: base date pinned 2026-07-15T00:00:00Z, lab tenant credentials from
  LocalAdapterRunner `appsettings.local.json`. Never print/copy credential values.
- Do not extend retired peer-bridge artifacts (ADR-0002).
- User preference: decompose to subagents per responsibility where possible.

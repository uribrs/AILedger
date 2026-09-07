# Prompt Contract

Role:
You are a senior .NET engineer extending the Cymulate YAML integration engine
(`Cymulate.Integration.Yaml.Engine`, adapters repo) under strict additive,
flag-gated, backward-compatibility discipline.

Goal:
Add four opt-in `merge_into` capabilities so the correlated CrowdStrike Falcon
findings lane reproduces the native FalconCollector envelope record-for-record,
each implemented as a generic merge capability any correlated integration can reuse.

Context:
- Primary design (read first, authoritative, do not re-derive):
  `../2026-07-21_1426_yaml-engine-falcon-correlation-gaps/envelope-parity-design.md`
  (settled research: egress boundary verdict, exact native chunking algorithm,
  engine hook points — with file:line).
- Single choke point: `Workflow/MergeEnrichmentSink.cs` (`PublishBatchAsync`,
  post-plan pass between the plan loop and the empty-check) and
  `Workflow/MergeEnrichment.cs` (`EmbedValue`/`EmbedGroup`, `ApplyEmbed`/`ApplyGrouped`).
  Config on `Models/WorkflowConfig.cs` (MergeIntoConfig). Loader validation in
  `Loader/YamlIntegrationLoader.cs`. Schema `Schemas/integration.schema.json`.
- The four capabilities, order B->C->D->A, and the exact native algorithm each must
  match, are specified in task.md and the design doc. Chunk framing must reproduce
  `floor(n/cap)+1` (trailing empty terminal chunk on exact-cap) and the zero-finding
  single record.
- Consumer YAML: `/Users/user/Dev/cymulate-magic-integration/integrations/crowdstrike-falcon.yaml`
  (merge stage `get_findings_for_hosts` -> target `hosts`).
- Native baseline: `logs/published-batches/20260721-125550/collector-run`
  (357 hosts, 35,651 findings, record contract {aid,chunk,isLastChunk,findingsInChunk,host,findings[]}).

Constraints:
- All items in constraints.md apply verbatim. Headliners: additive/opt-in only;
  flag-off byte-identical; existing consumers unchanged; native-exact incl. off-by-one;
  generic (no vendor branching); egress untouched; ADR-0003 schema re-sync byte-identical;
  gates green (`-p:TreatWarningsAsErrors=false` in magic-integration).

Success Criteria:
- Each feature: new unit tests where flag-off = today's behavior and flag-on = native
  behavior (chunk tests MUST cover the exact-cap off-by-one and the zero-finding record).
- Engine test project green; YamlCollector adapter test project green; magic-integration
  catalog/schema tests green.
- Schemas byte-identical across the two repos after each schema change.
- Live parity rerun (base date 2026-07-15) vs native baseline:
  - assets == 357 (require_key drops the null-aid phantom -> equal to native, not +1);
  - per-host finding counts match native modulo documented live drift;
  - with chunk on: record count and per-record envelope (chunk/isLastChunk/
    findingsInChunk, native key order, stripped fields, sorted remediation.entities)
    match native record-for-record for overlapping hosts (allow documented drift).
- A written record-for-record comparison report (execution_notes.md or review/).
- crowdstrike-falcon.yaml consumes all four; booked-deltas header updated (closed vs the
  one remaining optional dedupe-by-id).

Execution Rules:
- Do not assume missing data; read the design doc and the cited engine code before editing.
- Respect constraints strictly; if a capability cannot be built without touching a default
  path or egress, STOP and surface.
- Implement B->C->D->A; after each, run the gates and a parity rerun before the next.
- Decompose to subagents per responsibility where practical; run the engine gate after each
  worker; the merge sink/config/schema are shared files — sequence workers to avoid
  concurrent edits to the same file.
- Update state.json step statuses and append execution_notes.md per feature.

Output Format:
- Code in the adapters repo (+ YAML/schema in magic-integration).
- execution_notes.md per feature: final YAML grammar, files touched, native-parity notes,
  test evidence, parity numbers.
- Final record-for-record comparison report + updated booked-deltas.

Stop Conditions:
- Goal achieved (criteria met, report written).
- A capability cannot be honored additively (would touch a default path / egress) — stop and surface.
- Live run blocked by credentials/tenant/vendor errors retries don't clear.

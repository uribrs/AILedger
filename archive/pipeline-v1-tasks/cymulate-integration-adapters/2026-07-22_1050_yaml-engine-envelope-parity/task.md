# Task: YAML engine envelope parity — four opt-in merge capabilities

Add four opt-in, additive capabilities to the YAML integration engine's `merge_into`
block so the correlated CrowdStrike Falcon findings lane reproduces the native
FalconCollector envelope record-for-record. Generic merge capabilities reusable by any
correlated integration; all at one choke point; egress untouched; flag-off byte-identical.

Continue on the `falcon-strategy-parity` branch in both repos
(cymulate-integration-adapters engine + cymulate-magic-integration YAML/schema).

**Primary design input (read first, do not re-derive):**
`../2026-07-21_1426_yaml-engine-falcon-correlation-gaps/envelope-parity-design.md`
— carries the three settled research threads (egress boundary verdict, exact native
chunking algorithm, engine hook points) and the full per-feature design.

## Capabilities (implementation order B -> C -> D -> A)

- **B `require_key: true`** — drop a target/spine record whose join key resolves
  null/empty (native AidExtractor-skip parity; closes the only parser-visible delta,
  the null-aid phantom asset). Record-level anchor only; loader rejects on array anchor.
  Hook: key guard in `MergeEnrichment.ApplyEmbed`/`ApplyGrouped`. EASY.
- **C `strip: ["apps","suppression_info","host_info"]`** — remove named keys from each
  embedded element post-clone. Hook: `MergeEnrichment.EmbedValue` after `DeepClone`
  (`JsonObject.Remove`); per member in `EmbedGroup`. EASY.
- **D `sort: {path, order}`** — deterministic nested-array order within each embedded
  element. First/only mode `serialized_ordinal` = `OrderBy(e.ToJsonString(), Ordinal)`
  ascending, only when Count>=2 (native parity). Hook: `EmbedValue` after clone/strip;
  `JsonArray` rebuild. MODERATE.
- **A `chunk: {array, max, index_field, last_field, count_field}`** — split each target
  record's embedded array into slices of `max`, emitting N records per target (1->N),
  each rebuilt in native key order with the three fields stamped, host cloned per slice.
  MUST reproduce native exactly: cap default 2000 clamp 1..100000; chunk 0-based; flush
  at count>=cap (last=false); exactly one terminal record (last=true); record count
  `floor(n/cap)+1` for all n>=0 (exact multiples emit a trailing EMPTY terminal chunk);
  zero-finding target -> one record {chunk:0,last:true,count:0,array:[]}; findings keep
  arrival order. Folds in findingsInChunk + native key order. When configured, the Falcon
  spine op stops annotating chunk/isLastChunk. Hook: new post-plan pass in
  `MergeEnrichmentSink.PublishBatchAsync` (pipeline is already 1->{0,1}; nothing assumes
  in==out, so expanding the nodes list is safe). MODERATE.

## Out of scope (booked)
Optional per-batch dedupe of embedded elements by a key (native `seenFindingIds`) — only
bites on cursor-recovery overlap; the parser dedupes findings by id downstream.

## Per-feature completion loop
Additive field on `MergeIntoConfig` -> loader validation -> schema (adapters engine, then
re-synced byte-identical to magic-integration per ADR-0003) -> tests (extend
`MergeIntoTests.cs` + focused new tests incl. exact-cap off-by-one and zero-finding) ->
engine gate -> YamlCollector adapter gate -> live parity rerun vs native baseline.
Finally: `crowdstrike-falcon.yaml` consumes all four on `get_findings_for_hosts`.

## Reference paths
- Engine: `src/Cymulate.Integration.Adapters/Collectors/YamlCollector/Cymulate.Integration.Yaml.Engine/`
- Tests: `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Yaml.Engine.Test/`
- Adapter tests: `.../Cymulate.Integration.Adapters.Collectors.YamlCollector.Test/`
- YAML: `/Users/user/Dev/cymulate-magic-integration/integrations/crowdstrike-falcon.yaml`
- Native baseline: `logs/published-batches/20260721-125550/collector-run`
- Parity harness: `/private/tmp/claude-501/-Users-user-Dev-cymulate-magic-integration/43fc3fb6-4144-424a-8908-bf8795124a3e/scratchpad/yamlload/`

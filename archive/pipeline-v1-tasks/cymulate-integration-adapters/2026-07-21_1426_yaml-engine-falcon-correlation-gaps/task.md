# Task: Close YAML-engine gaps blocking Falcon correlated-findings parity

Close the four engine gaps (plus one guard) that prevent the declarative YAML route
from reproducing the native FalconCollector v5.0.0 correlated findings output, then
re-run the live lab-tenant parity collection and compare against the captured native
baseline.

## Work items (implementation order)

1. **Bug fix — cursor_recovery floor lost on page advance.**
   `CursorPaginator.UpdateState` rebuilds `PaginationState` without copying
   `RecoveryFloor` / `WatermarkValue` / `WatermarkIds`, so `{{recovery.floor}}`
   renders empty from page 2 (live-verified vendor 400). Copy the fields forward;
   audit `BodyCursorPaginator` / `ScrollPaginator` / other paginators for the same slip.

2. **Extension — `merge_into` N:1 fan-in (opt-in `group: true`).**
   Flag off: today's single-slot last-wins path, byte-identical (qualys,
   insightvm-cloud unchanged). Flag on: accumulate ALL source records per key;
   `ApplyEmbed` writes a `JsonArray`; composes with `unmatched: keep` +
   `unmatched_value: []`. Live-verified failure: Spotlight returns one record per
   finding; embed kept 1 of N.

3. **New feature (existing idioms) — mapping `first_of` + `regex_extract` transform.**
   Promote merge-shape `first_of` into response mapping; register `regex_extract`
   in `FieldTransformRegistry` (same `transform: {type: …}` shape as existing
   transforms). Enables native aid derivation: `aid ?? device_id ?? parse
   32-hex suffix of composite id`. Live tenant: 240/267 hosts have no aid field.

4. **Extension — opt-in one-page prefetch (pagination flag).**
   Issue page N+1's request as soon as page N is parsed, before the enrichment
   sink processes page N; next iteration consumes the buffer. Flag off: current
   path untouched. Live-verified failure: a ~3-min-stale Discover after-token
   returned HTTP 200 with a silently truncated tail (17 records instead of 108;
   267/358 hosts).

5. **Extension (guard) — opt-in scroll-completeness assertion.**
   When a cursor scroll ends without a continuation cursor, compare collected
   records against the response's total path (Discover `meta.pagination.total`);
   fail loudly on shortfall instead of publishing a truncated lane.

## Then

- Update `cymulate-magic-integration/integrations/crowdstrike-falcon.yaml` to
  consume: group merge, first_of/regex_extract aid, prefetch + completeness guard
  on the spine; optionally re-enable `cursor_recovery` on the Spotlight merge-source
  op (item 1 unblocks it; expiry message substrings still unverified).
- Re-sync the engine schema to `cymulate-magic-integration/schemas/integration.schema.json`
  (ADR-0003 dual-home discipline).
- Re-run the parity collection (base date 2026-07-15, lab tenant) and produce a
  comparison report vs the native baseline.

## Reference paths

- Engine: `/Users/user/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters/Collectors/YamlCollector/Cymulate.Integration.Yaml.Engine/`
- Engine tests: `…/UnitTests/Collectors/Cymulate.Integration.Yaml.Engine.Test/`
- YAML: `/Users/user/Dev/cymulate-magic-integration/integrations/crowdstrike-falcon.yaml`
- Native baseline: `/Users/user/Dev/cymulate-integration-adapters/logs/published-batches/20260721-125550/collector-run`
  (357 hosts, 35,651 findings, 2 files [250+107], record contract
  `{aid, chunk, isLastChunk, findingsInChunk, host, findings[]}`)
- Parity harness: `/private/tmp/claude-501/-Users-user-Dev-cymulate-magic-integration/43fc3fb6-4144-424a-8908-bf8795124a3e/scratchpad/yamlload/`
  (creds read from LocalAdapterRunner `appsettings.local.json`; wire logging built in)

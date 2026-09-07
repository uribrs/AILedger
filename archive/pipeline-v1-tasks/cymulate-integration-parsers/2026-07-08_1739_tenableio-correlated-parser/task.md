# Task: Tenable.io Correlated Parser Alignment

Align the Tenable.io parser (parser_key `tenable-assets-findings`) to the new correlated collector output while keeping old-collector (split) input parsing intact under the same parser_key. Parser deploys BEFORE the collector, so both input generations must parse correctly at once.

## Target

`libs/packages/parsers/yaml_engine/specs/tenable-assets-findings.yaml` — today a pure delegate shell (`process_hook: delegate_to_original` → `parsers.deprecated.tenable.tenableAssetsAndFindings.TenableAssetsAndFindings`, `skip_post_shaping: true`).

## New input (collector v5.0.0)

Single lane `findings_NNNNNN.json`, one atomic object per vendor chunk. NDJSON records:

```
{ uuid, chunk, isLastChunk, findingsInChunk,
  host { full Tenable /assets/export shape: id, ipv4s, fqdns, hostnames,
         operating_systems, tags[{key,value,...}], network_name, first_seen,
         last_seen, agent_names, installed_software, sources, ... },
  findings [ { asset{...}, output, plugin{...}, port, scan, severity,
               severity_id, severity_default_id, severity_modification_type,
               first_found, last_found, state, indexed, source, finding_id } ] }
```

Real sample data: 17 GB, 2,168 files at
`/Users/user/Dev/cymulate-integration-adapters/logs/published-batches/20260706-102603/collector-run`.

A host spans multiple envelope records (chunk 0..N, `isLastChunk` terminates); records with zero findings still carry the host.

## Reference idiom

`specs/tenable-sc-assets-findings.yaml` + `parsers/tenable_sc/tenable_sc_yaml_fns.py` — the declarative correlated-envelope spec (asset struct + exploded findings, explicit additional_fields maps, per-parser transform-fns module). Mirror structure/style with one mandated deviation: NO `stamp_asset_id` (see decisions).

## Deliverables

- Mode-aware parser: split → existing delegate (byte-equivalent); hydrated → new declarative correlated lane.
- Declarative spec + per-parser fns module for the correlated shape.
- Fixture cut from real sample data; per-parser test + harness wiring; full repo tests green.
- Commit on `feature/tenableio-correlated-parser` (NO push without user go-ahead).

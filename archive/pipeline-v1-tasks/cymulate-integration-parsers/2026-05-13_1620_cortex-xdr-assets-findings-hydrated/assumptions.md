# Assumptions

## A1 — Asset↔finding linkage strategy
- Status: VALIDATED (operator sign-off 2026-05-13; matches unanimous repo pattern across 12 assets-and-findings parsers)
- Statement: New parser mints a `_row_asset_id` UUID per emitted record before exploding `vulnerabilities[]`, propagates it to each finding row as `asset_id`, and copies it to the asset row as `id`. Finding rows receive a fresh `id` UUID via `helpers.uuid_udf()`. The collector-provided composite `id` strings on vulnerabilities are NOT used for row identity. The asset's `finding_ids[]` column defaults to empty array.
- Addendum (operator decision): Preserve the collector's composite vulnerability `id` string inside `findings.additional_fields` (suggested key label: `"Collector Finding Id"`) for cross-system traceability — analogous to how tenable stashes plugin ids.

## A2 — Parser registry key
- Status: VALIDATED (operator sign-off 2026-05-13)
- Statement: New parser registers under the PARSERS key `cortex-assets-findings`. Existing `cortex-assets` continues to map to `CortexXdrAssets`.

## A3 — Input file format / load path
- Status: VALIDATED (operator sign-off 2026-05-13: "cortex is to be treated as other hydrated parsers")
- Statement: Hydrated emit arrives as a multiline JSON file at `options.findings_file_path` (with `options.assets_file_path` unused). Loader uses `helpers.fetch_df_from_file` in Glue env and a multiline-JSON loader analogous to `_load_multiline_json_local` in local env. Apply the `_row_asset_id` join-key pattern from `defenderVmAssetsFindings`. No façade / `input_mode` plumbing since Cortex has only one emit shape.

## A4 — Finding ID generation
- Status: OPEN
- Statement: Finding rows receive a fresh UUID via `helpers.uuid_udf()` after exploding `vulnerabilities[]`, matching `defenderVmAssetsFindings.process`. The collector-provided `id` string on each vulnerability is dropped; if downstream needs the original collector id, it should be stored under `additional_fields` (decision deferred — not in scope unless reviewer flags).
- Resolves before: implementation.

## A5 — Empty vulnerabilities array handling
- Status: OPEN
- Statement: Emissions where `vulnerabilities == []` produce an asset row and zero finding rows. The asset row must still be emitted (not filtered out by the explode step). The empty-explode pattern from `defenderVmAssetsFindings._explode_vulnerabilities` (which returns an empty dataframe with the explode schema) is the reference.
- Resolves before: implementation.

## A6 — `mac_address` and `group_names` / `site_names`
- Status: OPEN
- Statement: The hydrated sample has top-level asset field `mac_address` but no `group_names` / `site_names`. `mac_address` will be surfaced under `device_metadata.mac_addresses` (matching existing CortexXdrAssets pattern). `group_names` and `site_names` default to empty array (mandatory in DB schema with default; not enforced by base parser process_asset_mandatory_fields).
- Resolves before: implementation.

## A7 — `additional_fields` content for the hydrated parser
- Status: OPEN
- Statement: Because the collector already projects Cymulate-named fields, the parser's `asset_additional_fields` and `finding_additional_fields` may be minimal or empty (struct of nothing → empty jsonb). This is acceptable per the DB schema (`jsonb DEFAULT '{}'::jsonb`). If empty struct causes Spark `F.struct()` issues, fall back to a single placeholder column.
- Resolves before: implementation.

## A8 — Agent / device metadata
- Status: OPEN
- Statement: The hydrated emit does not carry Cortex's endpoint operational telemetry (endpoint_status, endpoint_version, etc.) that the existing `CortexXdrAssets.agent_metadata_fields` / `device_metadata_fields` rely on. The new parser's `agent_metadata` and `device_metadata` structs will be either empty or populated from any matching keys actually present in the hydrated emit. The collector may need to extend its emit later; that is OUT OF SCOPE for this task.
- Resolves before: implementation.

# Wiz — cloud_resource asset type and cloud-inventory columns

## Task

Switch the Wiz parser's asset type from `Host` to `cloud_resource`, and emit the
six staging columns EA reads to populate its cloud-inventory satellite
(`cybi.asset_cloud_resource`).

The type switch alone is insufficient. None of the six columns exist anywhere in
this repo today, so a `cloud_resource` asset would land with an empty satellite
and every cloud-inventory column and filter blank in the UI.

## Changes

1. Wiz asset `type` const → `cloud_resource`.
2. Carry `cloud_platform`, `type` (→ `sub_type`), `cloud_account_id` and
   `cloud_account_name` through `process()` as explicit columns.
3. Override `create_asset_source` in the Wiz parser to append the six cloud
   columns after `super()` shaping. `region` and `cloud_provider_url` emit NULL.
4. Add the six columns to the `parser_output_assets` DDL in `db_schema.py`.
5. Resolve whether the now-redundant `"Cloud Resource Type"`, `"Cloud Platform"`,
   `"Cloud Account"` and `"Subscription"` entries stay in `asset_additional_fields`.
6. Extend the Wiz test; prove one non-Wiz parser is unaffected; re-run the
   real-data simulation.

## Files

- `libs/packages/parsers/deprecated/wiz/wizAssetsAndFindings.py`
- `libs/packages/parsers/schemas/db_schema.py`
- `tests/test_wiz_assets_findings_split.py`

## Out of scope

`base_parser.py` and all golden snapshots (decision D-C). `region` and
`cloud_provider_url` source data, which requires collector work in another repo
(D-D). Asset `value` and its `(value, type)` collisions.

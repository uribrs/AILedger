# Assumptions

## A1 — `cloud_resource` is the exact enum spelling — VALIDATED

Read directly from the authoritative `cybi.asset_type` enum and from EA's
`AssetType.CLOUD_RESOURCE = 'cloud_resource'` (label "Cloud resource", cloud icon).
Lowercase, underscore. BaseParser lowercases `type` in `post_process` regardless, so
the const's casing in source is cosmetic.

## A2 — EA's satellite is fed by six named staging columns — VALIDATED

Read from EA's `create-entities-tp` insert into `cybi.asset_cloud_resource`. The
insert is gated on at least one of the six being non-null, explicitly so that
non-cloud connectors get no satellite row. This is why the Wiz-only override
(D-C) is safe: the other 23 parsers never emit the columns at all.

## A3 — Wiz source fields for four of the six — VALIDATED

Measured on the real STG run (correlation id `6a6f3e0ecfb2665b22b861d7`), assets
lane enumerated exhaustively over all 4,809 records:
`cloud_platform`, `type`, `cloud_account_id`, `cloud_account_name` all present.
`cloud_account_name` is `''` for some accounts — EA `NULLIF`s it, so the parser
may pass it through as-is.

## A4 — `region` is absent from the Wiz assets lane — VALIDATED

The 19-field assets schema contains no `region`. It exists only on the findings
lane as `asset_region`, from `vulnerableAsset.region`. The collector's
`cloudResourcesV2` query never requests it.

## A5 — Golden snapshots carry the exact `create_asset_source` select list — VALIDATED

`rows_to_dicts` uses `row.asDict(recursive=True)` and `dump_snapshot` json-dumps
with `sort_keys=True`. A sampled snapshot record has exactly the 24 keys the base
select emits. Confirmed 17 `*.expected_assets.json` files exist. This is the
evidence behind D-C.

## A6 — A Wiz-local `create_asset_source` override composes cleanly — VALIDATED (2026-08-03)

Closed by execution, and the hazard was LARGER than written. Appending after
`super()` skips lowercasing/casting as stated — but `super()`'s select is a fixed
column list, so it also DROPS the cloud columns outright. A naive append would have
failed with a missing column, not merely wrong casing. The override therefore lifts
`id` + the six columns off the input frame BEFORE calling `super()`, re-attaches on
`id`, then normalizes. Real data confirms lowercase per D-A: `cloud_platform='aws'`,
`sub_type='virtual_machine'`.

## A6 (original text, retained for provenance) — was OPEN

The override calls `super().create_asset_source(df)` and appends the six columns
to the returned frame. Risk: `super()` ends with `cast_columns_to_schema(df,
_ASSET_OUTPUT_SCHEMA)` and `_lower_string_values`, both of which iterate the frame
it returns — so columns appended *after* `super()` are NOT lowercased and NOT
schema-cast by the base.

- **Consequence if unhandled:** the six columns would keep vendor casing, which
  contradicts D-A (operator chose lowercase) and would make the output
  inconsistent with every other string column.
- **Required:** the override must explicitly lowercase/strip and cast the six
  columns itself so the result matches what the base would have produced.
- **How to close:** assert the real-data run yields `cloud_platform='aws'` and
  `sub_type='virtual_machine'`, i.e. lowercased.

## A7 — The source `type` column survives to the override — VALIDATED (2026-08-03)

Real-data non-null counts match the source exactly: sub_type 4809/4809,
cloud_platform 4789/4809, cloud_account_id 4752/4809, cloud_account_name 4752/4809.
No collision between the carried vendor `type` and the mandatory output `type`.

## A7 (original text, retained for provenance) — was OPEN

`process()` builds the asset frame with an explicit `select`, so any source column
not named there is dropped before `post_process`/`create_asset_source` run. The
four cloud source columns must be carried through that select (T2) or the override
will have nothing to read.

- **How to close:** the real-data run showing non-null counts matching the source.

# Assumptions

## A1 — Spark's string→timestamp cast is case-sensitive — OPEN

`BaseParser._lower_string_values` (`base_parser.py:632`) lowercases every
`StringType` column before `cast_columns_to_schema` (`:633`) casts to
`TimestampType`. The assumption is that `2026-07-24t01:47:16z` casts to NULL
because Spark's `stringToTimestamp` matches a literal `T` separator and `Z` zone
designator.

- **Not executed** — no JVM in this environment (`Java gateway process exited before sending its port number`).
- **Impact if wrong**: the fix is still correct — it aligns Wiz with every sibling split parser, all of which cast early. Only the severity of the pre-existing bug changes.
- **How to close**: one Spark-enabled run casting `'2026-07-24t01:47:16z'` to timestamp.

## A2 — JSON reader infers `first_seen`/`last_seen` as StringType — OPEN

`helpers.fetch_df_from_file` calls `context.read.option("multiline", ...).json(paths)`
with no `inferTimestamp` option. Spark 3.x defaults `inferTimestamp` to false, so
ISO-8601 strings load as `StringType`. Glue's `create_dynamic_frame` path is
assumed to behave the same.

- **Impact if wrong**: if the column already arrives as `TimestampType`, the added cast is a harmless no-op and A1 is moot.

## A3 — Wiz `tags` arrives as `array<struct<key,value>>` — VALIDATED

Confirmed against the live STG run output (correlation id `6a6f3e0ecfb2665b22b861d7`):
`"tags": [{"key": "Name", "value": "..."}, ...]`. Spark will infer
`array<struct<key:string,value:string>>`.

- **Residual risk**: a sparse batch where every asset has `tags: []` may infer a
  different element type. The implementation must tolerate the column being
  absent or non-struct, mirroring `_ensure_columns` and the defensive schema
  checks in `CortexXdrAssetsAndFindingsNotHydrated._build_tags_like_agent`.

## A4 — Wiz `projects` arrives as `array<struct<id,name>>` — VALIDATED

Confirmed against the same run: `"projects": [{"id": "...", "name": "AWS Research labs"}]`.
Same sparse-batch caveat as A3.

## A5 — `correlate(..., embed_as=...)` preserves primary cardinality — VALIDATED

`utilities/correlation.py` documents the embedded-join mode explicitly:
*"Secondary rows matching a given key are collected into an array-of-struct
column named `spec.embed_as`. The primary side is never duplicated — cardinality
is preserved."* This is the mechanism that makes "no duplicate assets" structural
rather than incidental.

## A6 — No existing convention for flattening structured tags — VALIDATED

Checked every parser that declares a `tags` mandatory field. Cortex, CrowdStrike
and the CSV connector consume tags that are already `array<string>`; CloudGuard
(`cloudGuardAssetsAndFindings.py:85`) and Active Directory
(`activeDirectoryAssets.py:200`) hardcode an empty array. No parser flattens
`{key, value}` structs. Wiz is the first, so the format is a new decision — see
`decisions.md` D3.

## A7 — `run: false` in the yaml spec stays correct — VALIDATED

`specs/wiz-assets-findings.yaml` sets `run: false` with
`run_skip_reason: "separate-lane split; covered by dedicated test ..."`. The
delegate contract (module + class) does not change, so the spec should not need
editing. If execution finds it does, that is a signal to stop and re-check scope.

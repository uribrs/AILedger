# Constraints

## Cross-repo

- Two repos: `cymulate-integration-parsers` (Python/PySpark/Glue) and `cymulate-integration-adapters` (C#).
- Branch new work off `master` in `cymulate-integration-parsers`. It currently sits on
  `fix/crowdstrike-correlated-asset-identity-guards`, which is unrelated; building on it
  pollutes the diff and `baseRef`.
- `cymulate-integration-adapters` is on `dev`. Follow that repo's own branching convention.
- Both trees were clean at contract time. Do not mix these changes with unrelated work.

## Collector (adapters) — non-negotiable

- The collector MUST stay FIELD-AGNOSTIC. It must not parse, name, inspect, or remap any
  vendor field. `TenableIoCorrelatedRecordWriter` writes vendor bytes verbatim via
  `WriteRawValue`; that property is the design and must not be broken.
- The byte budget may only sum the lengths of the `ReadOnlyMemory<byte>` findings the phase
  already holds.
- Wire format MUST NOT change: `{uuid, chunk, isLastChunk, findingsInChunk, host, findings[]}`.
- `host` continues to ride chunk 0 only. Do not start repeating it, and do not emit `"host": null`.
- `isLastChunk` must remain true on exactly the final slice of each asset.
- The byte budget must be a named constant, not a literal.

## Parser (parsers) — non-negotiable

- The Glue reader fallback must be SCOPED to the split-size failure. It must not become the
  default read path, and must not swallow unrelated `Py4JJavaError`s.
- Mirror the existing `_read_ndjson_via_text` fallback pattern already in `helpers.py`.
- `helpers.fetch_df_from_file` is shared by 33 call sites across 20+ parsers. Any change must
  be a no-op for every caller on the healthy path.

## Out of scope — flag, do not build

- Capping or truncating the Tenable `output` field. It reaches customers through the documented
  external API `additionalFields` response schema
  (`cymulate-exposure-analytics` `apps/external-api-server/.../exposures.controller.ts:36`)
  and `cybi.exposure_source.additional_fields`. Product decision required.
- Raising the Glue split size via `SparkConf` in `jobBase.py:474`. `JobBase` is a single ABC
  shared by EVERY integration's parser job; a larger split risks OOM repo-wide.
- Guarding against concurrent parser runs on one `instance_id` (two ran 8s apart here:
  `wr_348da5cf`, `wr_ee28eae2`).

## Testing

- Item 2 requires a unit test asserting emitted envelopes reconstruct the input findings list
  exactly once, in order, with no duplication and no loss. This is the primary regression risk:
  the current loop is pure index arithmetic (`offset = slice * cap`, provably disjoint) and a
  byte-accumulating loop is stateful.
- Item 1 requires a test covering both the healthy path and the fallback path.
- Do not weaken or delete an existing test to make a new one pass.

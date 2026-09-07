# Wiz parser — envelope correlation and mapping fixes

## Task

Complete the Wiz assets-and-findings split parser on branch `feat/wiz-parser`.

The parser already exists and is wired end to end: `preparation.py:22` registers
`wiz-assets-findings` in `DUAL_MODE_PARSERS`, `specs/wiz-assets-findings.yaml`
delegates to `WizAssetsAndFindingsParser`, and
`tests/test_wiz_assets_findings_split.py` exercises the split path. It runs to
completion today and writes both Postgres tables. This task fixes five defects in
what it writes and how it correlates.

## Changes

1. **Envelope correlation** — replace the inverted findings→asset lookup join
   (disjunctive `asset_id` OR `asset_provider_id`) with the sibling pattern:
   `correlate(assets, findings, CorrelationSpec("id", "asset_id", LEFT,
   embed_as="vulnerabilities"))`, `_row_asset_id` minted on the asset row, then
   explode. Drop the `asset_provider_id` fallback key.
2. **Timestamp cast** — cast `first_seen`/`last_seen` to `TimestampType` in
   `process()` on both frames, before `BaseParser` shaping runs.
3. **Tags** — map the source `array<struct<key,value>>` into the mandatory
   `tags` column as `array<string>`. Currently hardcoded empty.
4. **Group names** — map `projects[].name` into `group_names`. Currently unmapped.
5. **Dead mapping** — remove the `"Region"` entry from `asset_additional_fields`;
   no `region` field exists on the assets lane.

## Files in scope

- `libs/packages/parsers/deprecated/wiz/wizAssetsAndFindings.py`
- `tests/test_wiz_assets_findings_split.py`
- `libs/packages/parsers/yaml_engine/specs/wiz-assets-findings.yaml` (only if the
  delegate contract changes — it is not expected to)

## Out of scope

Asset `type` (stays `"Host"`) and asset `value` (stays
`coalesce(name, external_id, id)`) are settled operator decisions. See
`decisions.md`. Do not revisit.

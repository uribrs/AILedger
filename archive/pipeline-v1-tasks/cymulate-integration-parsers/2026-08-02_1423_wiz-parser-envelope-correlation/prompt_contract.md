# Prompt Contract — Wiz parser envelope correlation and mapping fixes

Role:
You are a senior PySpark data-engineering specialist working in the Cymulate
integration-parsers repo.

Goal:
Complete the Wiz assets-and-findings split parser on branch `feat/wiz-parser` by
implementing five bounded changes to `WizAssetsAndFindingsParser` and extending
its test, so the parser writes correct, non-duplicated, fully-populated rows to
`integration.parser_output_assets` / `parser_output_exposures`.

Context:

- The parser exists and is wired end to end. `preparation.py:22` registers
  `wiz-assets-findings` in `DUAL_MODE_PARSERS`;
  `specs/wiz-assets-findings.yaml` delegates to
  `parsers.deprecated.wiz.wizAssetsAndFindings.WizAssetsAndFindingsParser` via
  the generic `process_hook`; `input_resolver.py:77` resolves `input_mode="split"`
  from the `assets*.json` + `findings*.json` lane pair.
- The Wiz collector publishes two flat lanes. Assets are `cloudResourcesV2`
  records keyed by `id` (+ `external_id`); findings are `vulnerabilityFindings`
  records carrying `asset_id` (== the asset's `id`). Findings are NOT nested.
- The current implementation joins in the wrong direction: findings LEFT JOIN an
  asset lookup, on a disjunctive `asset_id == id OR asset_provider_id ==
  external_id`. Sibling split parsers instead build an asset spine and embed
  findings into it.
- Reference implementations to mirror:
  - `libs/packages/parsers/deprecated/cortex/CortexXdrAssetsAndFindingsNotHydrated.py:33,207`
  - `libs/packages/parsers/deprecated/cortex/cortexXdrAssetsAndFindings.py:_explode_vulnerabilities` (`:256-274`)
  - `libs/packages/parsers/deprecated/defender_vm/DefenderVmAssetsFindingsNotHydrated.py:21,53`
- `utilities/correlation.py` documents the embedded-join guarantee: *"The primary
  side is never duplicated — cardinality is preserved."*

Ground truth (live STG run, correlation id `6a6f3e0ecfb2665b22b861d7`, 2026-08-02):

- assets lane 4,809 records / 10 files; findings lane 107,653 records / 216 files.
- `findings.asset_id` resolves to `assets.id` for 100% of 7,153 sampled findings; 0 orphans.
- `assets.id` unique 4,809/4,809; `external_id` unique, 0 duplicates.
- Only 92 of 4,809 assets are `VIRTUAL_MACHINE` or `CONTAINER_IMAGE` — ~98% legitimately have zero findings.
- Findings are interleaved across files, not grouped by asset.
- `asset_provider_id` vs `external_id`: 85.2% ARN-vs-bare-id mismatch, 9.6% identical, 5.2% empty string.

Assets-lane schema (exactly these 19 fields, verified exhaustively):
`cloud_account_id, cloud_account_name, cloud_platform, cloud_provider,
external_id, first_seen, has_admin_privileges, has_high_privileges,
has_sensitive_data, id, is_accessible_from_internet, is_open_to_all_internet,
last_seen, name, projects, status, subscription_external_id, tags, type`

Constraints:

- See `constraints.md`. All of it is binding.
- Only `wizAssetsAndFindings.py`, `tests/test_wiz_assets_findings_split.py`, and
  (only if the delegate contract changes) `specs/wiz-assets-findings.yaml`.
- Never access `/Users/user/Dev/cymulate-exposure-analytics`.
- Asset `type` and `value` are settled — see `decisions.md` D1, D2, D2a. Do not revisit.
- Pure DataFrame transforms. `connection_manager` stays unused.
- Run only `tests/test_wiz_assets_findings_split.py`; local Spark cannot start.

Required changes:

1. **Envelope correlation.** Replace `process()`'s findings→lookup join
   (`wizAssetsAndFindings.py:242-273`) with:
   - build the asset spine from `_assets_src`;
   - `correlate(assets, findings, CorrelationSpec(primary_key="id",
     secondary_key="asset_id", join_type=JoinType.LEFT,
     embed_as="vulnerabilities"))`;
   - `fill_missing_array(..., "vulnerabilities")` so unmatched assets carry `[]`;
   - mint `_row_asset_id = helpers.uuid_udf()` on the ASSET row;
   - explode `vulnerabilities` back out, carrying `_row_asset_id` as the
     findings' `asset_id`;
   - handle the empty-findings-lane case without raising.
   Delete the `asset_provider_id` fallback entirely, plus the now-dead
   `_f_provider_id` / `_wiz_external_id` plumbing.
2. **Timestamp cast.** In `process()`, cast `first_seen`/`last_seen` to
   `TimestampType` on both the assets and findings frames, before
   `post_process()` runs.
3. **Tags.** Map the source `tags` (`array<struct<key,value>>`) into the mandatory
   `tags` column as `array<string>`, formatted `"key=value"` per `decisions.md` D3.
   Tolerate the column being absent, null, empty, or not a struct array.
4. **Group names.** Declare `group_names` in `asset_mandatory_fields`, sourced
   from `projects[].name` as `array<string>` (precedent:
   `defenderVmAssetsFindings.py:120-131`). Same tolerance as (3).
5. **Dead mapping.** Delete the `"Region"` entry from `asset_additional_fields`
   (`wizAssetsAndFindings.py:148`).
6. **Test.** Extend `tests/test_wiz_assets_findings_split.py` to cover: envelope
   linkage, zero-vuln asset survival, asset-row uniqueness, non-null
   `first_seen`/`last_seen` on both frames, populated `tags`, populated
   `group_names`. Remove the fallback-key case, which no longer exists.

Success Criteria:

- All five changes implemented, in the style of the sibling split parsers.
- Zero-vuln assets survive as asset rows (the ~98% case), demonstrably.
- The correlation cannot duplicate asset rows.
- Every emitted finding carries a non-null `asset_id` pointing at a surviving asset row.
- `first_seen`/`last_seen` are `TimestampType` on both frames before `BaseParser` shaping.
- `tags` and `group_names` are populated from source data.
- The dead `"Region"` mapping is gone.
- Test file updated to cover all of the above.
- No changes outside the in-scope files; no access to the exposure-analytics repo.
- Test-execution status reported honestly, including "could not run" if Spark cannot start.

Execution Rules:

- Do not assume missing data. Verify field names against the assets-lane schema above.
- Respect constraints strictly.
- Read the reference implementations before writing; mirror their structure and defensiveness.
- If a change appears to require touching a file outside scope, STOP and report.
- Do not "improve" the settled `type` / `value` decisions.

Output Format:

- Modified source files.
- `execution_notes.md` updated with what changed, per step id (S1–S6).
- A short summary: what changed, what was verified, what could not be verified.

Stop Conditions:

- Goal achieved and all success criteria met.
- A required change would need an out-of-scope file.
- The assets-lane schema contradicts a mapping this contract specifies.
- `state.json` conflicts with the markdown files.

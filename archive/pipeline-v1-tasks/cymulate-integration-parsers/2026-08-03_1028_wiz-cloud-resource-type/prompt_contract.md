# Prompt Contract — Wiz cloud_resource type and cloud-inventory columns

Role:
You are a senior PySpark data-engineering specialist working in the Cymulate
integration-parsers repo.

Goal:
Make the Wiz parser emit `type = cloud_resource` and the six cloud-inventory
staging columns EA reads to populate `cybi.asset_cloud_resource`, without
touching shared base-parser behaviour or any golden snapshot.

Context:

- The Wiz split parser was reworked in the predecessor task (commit `f230eee`):
  envelope correlation via `correlate(embed_as="vulnerabilities")`, timestamp
  casts, tags, group_names, spine dedup. None of that may regress.
- EA populates `cybi.asset_cloud_resource` (1:1 satellite, keyed by `asset_id`)
  from six `integration.parser_output_assets` columns:
  `cloud_platform`→cloud_provider, `sub_type`→cloud_resource_sub_type,
  `cloud_account_id`, `cloud_account_name` (EA applies `NULLIF(x,'')`),
  `region`, `cloud_provider_url`.
  The INSERT is gated on at least one being non-null, not on `asset.type`.
- None of the six exist in this repo: not in `db_schema.py`, not in
  `base_parser._ASSET_OUTPUT_SCHEMA`, not in `create_asset_source`'s select.
- Wiz's assets lane (19 fields, enumerated over all 4,809 real records) supplies
  four of them: `cloud_platform`, `type`, `cloud_account_id`,
  `cloud_account_name`. It has no `region` and no `cloud_provider_url`.

Constraints:

- See `constraints.md` — all binding. Highlights:
  - Modify ONLY `wizAssetsAndFindings.py`, `db_schema.py`,
    `tests/test_wiz_assets_findings_split.py`.
  - **`base_parser.py` must not be modified.** No snapshot may change.
  - No EA repo access. No git operations.
  - The cloud columns must NOT be declared in `asset_mandatory_fields`
    (that machinery silently drops unknown keys — predecessor D8).
  - The `create_asset_source` override must call `super()` and append, not
    re-implement the base select.

Required changes:

1. **T1** — asset `type` const `"Host"` → `"cloud_resource"`.
2. **T2** — carry `cloud_platform`, `type`, `cloud_account_id`,
   `cloud_account_name` through the `process()` asset select as explicit
   columns, so they survive to `create_asset_source`. Give the carried `type`
   column a non-colliding name: the output frame already has a mandatory `type`
   column holding `cloud_resource`, so the source vendor type must be carried
   under a distinct name (e.g. `sub_type`) — **verify no collision** before
   selecting.
3. **T3** — override `create_asset_source` on the Wiz parser:
   ```
   def create_asset_source(self, df):
       df = super().create_asset_source(df)   # base shaping, lowercasing, casting
       # append the six cloud columns here
   ```
   **Critical (assumption A6):** `super()` performs `_lower_string_values` and
   `cast_columns_to_schema` on the frame it returns. Columns appended afterwards
   receive neither. The override must therefore lowercase, strip control
   characters, and cast the six columns itself so their treatment matches every
   other string column. Per decision D-A the values ARE lowercased — `'AWS'`
   must land as `'aws'` and `'VIRTUAL_MACHINE'` as `'virtual_machine'`.
   `region` and `cloud_provider_url` are emitted as typed NULL string columns.
4. **T4** — add the six columns to `parser_output_assets_create_table_schema` in
   `db_schema.py` as nullable `text`.
5. **T5** — decide and apply: `"Cloud Resource Type"`, `"Cloud Platform"`,
   `"Cloud Account"` and `"Subscription"` in `asset_additional_fields` now
   duplicate real columns. Remove the redundant ones or justify keeping them in
   `execution_notes.md`. Do not silently leave duplicates.
6. **T6** — tests and proof:
   - Extend `tests/test_wiz_assets_findings_split.py`: assert
     `type == "cloud_resource"`, the four populated columns carry lowercased
     source values, and `region`/`cloud_provider_url` are NULL.
   - Run one non-Wiz golden-snapshot case (e.g. `-k qualys`) and confirm it
     passes with NO snapshot regeneration.
   - Re-run the real-data simulation and confirm the predecessor invariants hold
     (4,809 asset rows, 107,653 exposure rows, 0 duplicate ids, 0 null/dangling
     `asset_id`) plus non-null counts on the four cloud columns.

Success Criteria:

- Wiz assets emit `type = cloud_resource`.
- All six columns present on the Wiz asset output frame; four populated, two NULL.
- The four carry lowercased source values (D-A).
- `base_parser.py` unmodified; `git status` shows no snapshot file changed.
- A non-Wiz parser's golden-snapshot test passes unchanged.
- Wiz test file passes in full, actually executed.
- Real-data run reproduces every predecessor invariant and shows the expected
  non-null counts.
- No files touched outside the three in scope; no EA access; no git operations.

Execution Rules:

- Do not assume missing data. Verify column names against the real assets-lane
  schema before selecting them.
- Respect constraints strictly.
- If a required change would need `base_parser.py` or a snapshot, STOP and report
  — that is the signal the override approach has failed, and it is an operator
  decision whether to switch to the base-parser route.
- Resolve assumptions A6 and A7 by execution, and record the outcome.

Output Format:

- Modified source files.
- `execution_notes.md` updated per step id (T1–T6), including the T5 decision.
- Summary: what changed, what was executed, what remains.

Stop Conditions:

- Goal achieved and all success criteria met.
- A change would require an out-of-scope file.
- Any golden snapshot would change.
- `state.json` conflicts with the markdown files.

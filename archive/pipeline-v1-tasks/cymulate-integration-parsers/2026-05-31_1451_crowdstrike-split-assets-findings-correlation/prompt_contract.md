Role:
You are a senior PySpark data-engineering specialist working in the Cymulate
integration-parsers codebase.

Goal:
Refactor `libs/packages/parsers/crowdstrike/crowdstrikeAssetsFindings.py` into a split-only
parser where the dedicated assets feed is the asset spine and findings are correlated to
assets by top-level `aid`, mirroring the Defender VM split-mode parser. Fix the asset
over-count (one asset per finding row → one asset per host) without parsing the heavy
`host_info`/`apps`/`suppression_info` blocks of the findings feed.

Context:
- Today the parser derives one asset per finding row from embedded `host_info`, producing
  ~1,424× too many assets (37,034 vs 302 in the lab dump). The relationship is inverted.
- Two raw NDJSON feeds exist: `assets_*.json` (matches `crowdstrikeAssets.py`) and
  `findings_*.json` (carries `cve`, `remediation`, and a heavy `host_info`/`apps`).
- North-star technique: `libs/packages/parsers/defender_vm/DefenderVmAssetsFindingsNotHydrated.py`
  (assets → `correlate(... LEFT, embed_as="Vulnerabilities")` → explode → findings carry the
  parent `_row_asset_id`). Also see `cortex/CortexXdrAssetsAndFindingsNotHydrated.py`.
- Shared join utility: `libs/packages/utilities/correlation.py` (`correlate`, `CorrelationSpec`,
  `JoinType` — INNER/LEFT only). Do not modify it.
- Asset language source of truth: `libs/packages/parsers/crowdstrike/crowdstrikeAssets.py`
  (reads the identical assets feed). Do not modify it.
- `libs/packages/parsers/common/base_parser.py` `post_process` raises `FindingsWithoutAssets`
  if findings exist with zero assets.
- Lab data (read-only, real clients far larger):
  `/Users/user/Dev/cymulate-integration-adapters/logs/published-batches/20260531-140608/collector-run/`
  — `assets_000001.json` (302 rows; `aid` on 25 managed only) and
  `findings_000001.json … findings_000015.json` (37,034 findings; 22 distinct `aid`; 0 orphans).

Constraints:
- See `constraints.md`. Highlights:
  - Split-only; no hydrated branch remains.
  - `crowdstrikeAssets.py` and `correlation.py` unchanged.
  - All asset rows emit (incl. null-`aid` unmanaged/unsupported), typed `Host`, UUID identity.
  - Asset field mappings mirror/reuse `crowdstrikeAssets.py`; no `host_info.*` asset mappings.
  - Correlate on top-level `aid` via `correlate(LEFT, embed_as="vulnerabilities")` + explode.
  - Orphan findings dropped; no stub assets.
  - Findings read with an explicit projected schema omitting `host_info`/`apps`/`suppression_info`
    (NOT a post-read drop); `cve` kept whole.
  - Spark-native; no full-dataset collect.

Success Criteria:
- Asset count = number of asset-feed rows (302 in lab), all typed `Host`, including
  unmanaged/unsupported — NOT 37,034.
- Each finding (37,034 in lab) correlates to exactly one asset: `asset_id` = parent
  `_row_asset_id`, joined on `aid`.
- The findings read schema demonstrably excludes `host_info`, `apps`, `suppression_info`
  (verify via the DataFrame schema / read plan), and includes the whole `cve` struct.
- `crowdstrikeAssets.py` is byte-for-byte unchanged.
- No hydrated code path remains in `crowdstrikeAssetsFindings.py`.
- Existing crowdstrike-related tests pass; new/adjusted tests cover split correlation,
  all-assets-emit (incl. null-`aid`), and the projected read schema. Follow the conventions in
  `tests/test_cortex_xdr_assets_findings.py` and `tests/test_defender_vm_reconciliation.py`.
- The `FindingsWithoutAssets` guard is not tripped for valid split input.

Execution Rules:
- Do not assume missing data. First confirm the real crowdstrike path/strategy plumbing
  (`options.assets_file_path`/`findings_file_path` vs `*_strategy`, and whether `input_mode`
  exists) against `tests/test_input_resolver.py`, `tests/test_run_parsers.py`, and the loader
  code — do not copy Defender VM's strategy plumbing blindly.
- Respect constraints strictly. Reuse existing utilities (`correlate`, `helpers.uuid_udf`,
  `helpers.fetch_df_*`, `extract_module_data`, `safe_struct`) rather than reinventing.
- Match surrounding code style; keep the change proportional.

Output Format:
- Modified `crowdstrikeAssetsFindings.py` (and any minimal new split-handler module if the
  executor judges it cleaner, following the Defender VM/Cortex file split).
- New/updated tests under `tests/`.
- `execution_notes.md` recording: confirmed plumbing, files touched, how the projected schema
  is enforced, and verification results against the lab data.

Stop Conditions:
- When all Success Criteria are met and tests pass.
- If the real path/strategy plumbing contradicts the pinned design in a way that blocks
  correlation (e.g. no separate assets path is actually available to this parser) — stop and
  surface, do not work around silently.

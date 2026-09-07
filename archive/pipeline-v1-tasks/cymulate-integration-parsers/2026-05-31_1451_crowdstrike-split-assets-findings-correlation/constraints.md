# Constraints

- Split-only: no hydrated code path may remain in `crowdstrikeAssetsFindings.py`.
- `crowdstrikeAssets.py` must not be modified.
- `libs/packages/utilities/correlation.py` must not be modified (no RIGHT/FULL join added).
- Asset spine = the full assets feed; ALL asset rows emit, including unmanaged/unsupported (null `aid`).
- Every asset typed `Host`.
- Asset identity = per-row UUID (`_row_asset_id`), never `aid` (null-`aid` rows must still get a stable id).
- Asset field mappings must mirror `crowdstrikeAssets.py` (paths: `platform_name`, `os_version`, `current_local_ip`, `fqdn`, `site_name`, `hostname`, `tags`, `first_seen_timestamp`, etc.). Prefer reuse/import over re-typing to prevent drift.
- Correlation anchor = top-level `aid` on both feeds (NOT `host_info.aid`, which is null in findings).
- Correlate via `correlate(assets, findings, CorrelationSpec(primary_key=aid, secondary_key=aid, join_type=LEFT, embed_as="vulnerabilities"))` then explode; finding `asset_id` = parent `_row_asset_id`.
- Orphan findings (finding `aid` with no asset row) are dropped — no synthetic stub assets.
- Findings read MUST use an explicit projected schema that omits `host_info`, `apps`, `suppression_info`. A post-read `.drop()` is NOT acceptable (it still parses).
- Keep the `cve` struct whole/as-is in the findings read schema.
- Findings parser consumes only: `aid`, `vulnerability_id`, `status`, `created_timestamp`, `updated_timestamp`, `cve` (whole), `remediation`.
- Finding mandatory fields/transforms unchanged (severity from `cve.severity`, status map, `cve_ids` from `cve.id`, description from `cve.description`, mitigation from `remediation.entities[0].action`, name/display_name from `vulnerability_id`).
- `finding_ids` on the asset stays an empty array (`F.array()`).
- Spark-native end to end — no Python-side collect of the full dataset.
- Must not break the existing `base_parser.post_process` `FindingsWithoutAssets` guard.

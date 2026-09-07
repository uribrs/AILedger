# Constraints

## Scope

- Only these files may be modified:
  - `libs/packages/parsers/deprecated/wiz/wizAssetsAndFindings.py`
  - `tests/test_wiz_assets_findings_split.py`
  - `libs/packages/parsers/yaml_engine/specs/wiz-assets-findings.yaml` (only if the delegate contract changes)
- Never read from or write to `/Users/user/Dev/cymulate-exposure-analytics`. Its schema facts are captured in this contract.
- No unrelated refactors, renames, or reformatting.

## Behaviour

- Zero-vuln assets MUST survive as asset rows. ~98% of the real inventory (4,717 of 4,809) has no findings.
- The correlation MUST NOT duplicate asset rows.
- Every emitted finding MUST carry a non-null `asset_id` pointing at a surviving asset row.
- `asset` `type` stays const `"Host"`. Do not change.
- `asset` `value` stays `coalesce(nonblank(name), nonblank(external_id), id)`. Do not change.
- Drop the `asset_provider_id` fallback join key entirely.
- No ordering assumptions: findings are interleaved across files, not grouped by asset.

## Implementation

- Pure DataFrame transforms only. Only Spark / the Glue job touches Postgres.
- `connection_manager` is stored and never used (`wizAssetsAndFindings.py:101`) — keep it unused.
- No direct SQL, no DB access, no new dependencies.
- Match the style of the sibling split parsers (Cortex, Defender VM, CrowdStrike).
- `first_seen`/`last_seen` must be `TimestampType` on both frames before `BaseParser.post_process` shaping runs.

## Verification

- Run ONLY `tests/test_wiz_assets_findings_split.py`. Never the full suite.
- Local Spark cannot start (no JVM): pyspark raises `Java gateway process exited before sending its port number`.
- If tests cannot execute, say so plainly. Never claim they passed.

## EA output contract (already verified — do not re-derive)

- `asset.type` in `cybi.asset_type`; `host` is valid.
- `asset.os_type` null or in `cybi.asset_host_os_type` (`windows`, `windows-server`, `linux`, `mac`, `macos`, `android`, `ios`, `gcp`, `aws`, `azure`, `kubernetes`, `active-directory`, `ubuntu`, `debian`, `other`).
- `exposure.type` in `cybi.exposure_type` (`vulnerability`); `exposure.status` in `cybi.exposure_status` (`opened`/`resolved`/`reopened`); `exposure.severity` null or in `cybi.exposure_source_severity` (`critical`/`high`/`medium`/`low`/`info`).
- Exposures INNER JOIN `cybi.cve_details` on `lower(cve_id)` where `status != 'rejected'` — CVE-less or uncatalogued findings are dropped by EA.
- Exposures INNER JOIN the enriched assets on `asset_id` — an exposure whose asset did not survive enrich is discarded.

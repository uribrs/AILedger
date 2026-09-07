# Assumptions

- [VALIDATED] The findings feed carries a usable top-level `aid`; `host_info.aid` is null. (Confirmed against lab `findings_000001.json`.)
- [VALIDATED] The assets feed schema matches `crowdstrikeAssets.py` source paths. (Confirmed: `id`, `aid`, `platform_name`, `os_version`, `first_seen_timestamp`, `current_local_ip`, `fqdn`, `site_name`, `hostname`, `tags` all present.)
- [VALIDATED] `aid` is present only on `managed` assets (25); `unmanaged`/`unsupported` (277) have null `aid`. They must still emit as assets. (Confirmed in lab dump.)
- [VALIDATED] 0 orphan findings in the lab dump (all 22 finding `aid`s exist in the assets feed). Orphan handling = drop, per established convention. (Confirmed via correlation analysis + Explore sweep of all parsers.)
- [VALIDATED] `correlation.py` supports only INNER and LEFT joins; embed path is asset-spine LEFT. No parser preserves orphan findings. (Confirmed via Explore sweep.)
- [VALIDATED] `utilities.correlation` resolves to `libs/packages/utilities/correlation.py`; `utilities` is the import root. (Confirmed via defender_vm imports + file location.)
- [OPEN] Exact path/strategy plumbing for crowdstrike: whether it uses `options.assets_file_path`/`findings_file_path` or `options.assets_strategy`/`findings_strategy` (as Defender VM does). Executor (S1) must confirm against `tests/test_input_resolver.py`, `test_run_parsers.py`, and the options/loader code rather than assume.
- [OPEN] Whether crowdstrike has an `input_mode` wired at all; if not, the façade omits mode resolution and goes straight to split logic. Executor to confirm.
- [OPEN] Exact crowdstrike test fixtures under `test_files/crowdstrike/{assets, assets_and_findings}` and whether existing tests reference `crowdstrikeAssetsFindings`. Executor (S7) to inspect and follow `tests/test_cortex_xdr_assets_findings.py` / `tests/test_defender_vm_reconciliation.py` conventions.

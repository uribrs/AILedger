# Decisions

- Split-only façade; remove all hydrated logic (Falcon ships split feeds exclusively).
- Asset spine = full assets feed; emit every asset row (managed + unmanaged + unsupported).
- Asset identity = per-row UUID `_row_asset_id`, decoupled from `aid` so null-`aid` rows still get a stable id.
- Reuse `crowdstrikeAssets.py` asset field definitions rather than re-typing them, to prevent drift.
- Correlation anchor = top-level `aid`; correlate via shared `correlate(LEFT, embed_as="vulnerabilities")` then explode.
- Orphan findings dropped (matches every existing parser; no correlation.py change, no stub assets).
- Findings read with an explicit projected schema omitting `host_info`/`apps`/`suppression_info` (~69% parse/shuffle saved); `cve` kept whole. Performance lever for hundreds-of-millions scale.
- `finding_ids` stays an empty array; correlation carried by `findings.asset_id` (matches Cortex/Defender VM).
- `crowdstrikeAssets.py` and `correlation.py` untouched.

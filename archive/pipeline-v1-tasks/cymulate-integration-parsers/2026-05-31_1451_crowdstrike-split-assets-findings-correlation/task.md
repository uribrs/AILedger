# Task: CrowdStrike split assets-and-findings correlation

Refactor `libs/packages/parsers/crowdstrike/crowdstrikeAssetsFindings.py` so that assets are
the spine and findings are correlated to them, mirroring the Defender VM split-mode parser.

## Problem

Today the parser extracts one asset per finding row from the embedded `host_info` struct.
With N findings for a host, it emits N "assets" — a ~1,424× over-count in the lab dump
(37,034 findings → 37,034 "assets" vs 302 real asset rows). The asset/finding relationship
is inverted and the asset count is meaningless.

## Correct model

- Assets come from the dedicated assets feed (`assets_*.json`), one row per host.
- Findings come from `findings_*.json`, correlated to their host by top-level `aid`.
- CrowdStrike/Falcon only ships split feeds — there is no hydrated path.

## Outcome

A split-only façade: build the asset spine from the full assets feed (all rows emit, typed
`Host`, per-row UUID identity), read findings with a projected schema that never parses the
heavy `host_info`/`apps`/`suppression_info` blocks, correlate findings to assets by `aid`
via the shared `correlate(... LEFT, embed_as=...)` utility, and explode so each finding
carries its parent asset id. `crowdstrikeAssets.py` is not modified.

# Constraints

- `parser_key`, `flow_name`, `integration_setting_id`, `integration_setting_flow_id`, `zip_file_key` unchanged — same integration flow.
- Split-mode behavior byte-equivalent: the deprecated `TenableAssetsAndFindings` delegate stays untouched and keeps handling `input_mode == "split"`.
- `stamp_asset_id` / `link_strategy: stamped_uuid` FORBIDDEN for this spec (multi-chunk hosts would mint duplicate assets).
- No engine (`yaml_engine`) changes unless a small generic feature is genuinely required; prefer spec + per-parser fns module + a small router hook at existing extension seams.
- Do not touch other specs or other parsers.
- Python house style mirrors neighbors (SC fns module pattern; plain repo test conventions).
- All repo tests green before commit.
- Commit at the end on `feature/tenableio-correlated-parser`; DO NOT push without explicit user go-ahead.
- Task artifacts maintained in this directory per pipeline conventions; codex-state mirror updated at close.

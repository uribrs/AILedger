Role:
You are a senior Python / PySpark engineer working in the `cymulate-integration-parsers` repo. You know the `BaseParser` lifecycle (`pre_process` → `process` → `post_process`) and the established hydrated-mode pattern in `libs/packages/parsers/defender_vm/defenderVmAssetsFindings.py`.

Goal:
Add a new parser class that consumes the hydrated Cortex XDR collector emit (`{asset, vulnerabilities[]}` envelope) and emits both `assets_df` and `findings_df` matching the existing `parser_output_assets` and `parser_output_exposures` DB schemas. Existing `CortexXdrAssets` stays untouched.

Context:
- Sample input: `/Users/user/Dev/AgentService/Source/CybiCollectors/CortexXdrCollector/Examples/emitted_finding_sample.json`.
- Existing parser package: `libs/packages/parsers/cortex/` (contains `cortexXdrAssetsParser.py` and `__init__.py`).
- Reference implementation: `libs/packages/parsers/defender_vm/defenderVmAssetsFindings.py` — `_row_asset_id` join-key pattern, `process` and `post_process` shape, `finding_mandatory_fields` set, vulnerability-explode behavior.
- DB schemas: `libs/packages/parsers/schemas/db_schema.py` (`parser_output_assets`, `parser_output_exposures`).
- Asset mandatory-field validator: `BaseParser.process_asset_mandatory_fields` in `libs/packages/parsers/common/base_parser.py:76-123` (will RAISE if any of the 14 required keys is missing).
- Collector emits Cymulate-named fields directly — this parser is a pass-through normalizer, NOT a field mapper.

Constraints:
- See `constraints.md` in this task directory.
- All OPEN items in `assumptions.md` must be resolved before any code-bearing step starts.

Success Criteria:
- A new class (e.g. `CortexXdrAssetsAndFindings`) exists under `libs/packages/parsers/cortex/`.
- The class implements all four abstract field-set properties: `asset_mandatory_fields`, `asset_additional_fields`, `finding_mandatory_fields`, `finding_additional_fields`.
- `asset_mandatory_fields` returns the 14 required keys with `path` values that line up with the hydrated sample's `asset.*` keys (or `default_value` where the sample lacks them); validator does not raise.
- `finding_mandatory_fields` returns the 12 required keys with `path` values that line up with the hydrated sample's `vulnerabilities[*].*` keys.
- `process()` produces `assets_df` and `findings_df` carrying every column required by the corresponding DB schema, using the `_row_asset_id` join-key pattern.
- `post_process()` returns `(assets_df, findings_df)` after the standard `BaseParser.post_process()` plus the same four shaping calls used in `defenderVmAssetsFindings.post_process` (asset tags, asset source, unified finding name for vulnerability, finding source).
- The class is exported from `parsers/cortex/__init__.py`.
- The class is registered in `parsers/__init__.py` `PARSERS` map under a new key (proposed `cortex-assets-findings`).
- Existing `cortex-assets` → `CortexXdrAssets` mapping is unchanged.
- All four `__init__.py` and parser files import cleanly (no circular import, no missing symbol).
- Verifier and code-reviewer subagents both run and both pass (or repairs are made until they do).

Execution Rules:
- Do not assume missing data — resolve via the OPEN assumptions list or surface to operator.
- Respect constraints strictly.
- Do not modify `base_parser.py`, `db_schema.py`, `preparation.py`, or `cortexXdrAssetsParser.py`.
- Do not introduce a façade, `input_mode` selector, or strategy plumbing for Cortex.
- Match coding style of existing Cortex / defender_vm parsers (snake_case fields, `ColumnSchema(path=…, default_value=…, transformation=…)`).

Output Format:
- Code: new parser file under `libs/packages/parsers/cortex/`, updated `parsers/cortex/__init__.py`, updated `parsers/__init__.py`.
- Notes: `execution_notes.md` appended with each step's outcome.
- Reviews: `review/verifier-N.md`, `review/code-reviewer-N.md`.

Stop Conditions:
- An OPEN assumption cannot be resolved without operator input.
- Verifier or code-reviewer reports an unresolvable defect.
- A required constraint cannot be satisfied without modifying out-of-scope files.

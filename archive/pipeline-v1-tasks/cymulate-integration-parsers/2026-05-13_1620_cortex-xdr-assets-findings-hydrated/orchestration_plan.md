# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: One coherent execution unit — a single new parser class plus two single-line registry/export edits. Worker boundaries would be artificial. All OPEN assumptions resolved before this phase.

## Research Decisions
- None needed. All OPEN external-behavior items resolved via operator sign-off (A1, A2, A3) and unanimous repo-pattern analysis (verified across 12 assets-and-findings parsers via grep). No vendor APIs or library internals require investigation.

## Worker Plan
- Not applicable — direct path.

## Synthesis Approach
- Not applicable — direct path.

## Verification Obligations
- Cross-check against `prompt_contract.md` Success Criteria.
- Confirm the 14 asset mandatory fields and 12 finding mandatory fields are all present.
- Confirm `_row_asset_id` join-key pattern is used and `findings.asset_id` is populated.
- Confirm the collector's composite vulnerability `id` is preserved in `findings.additional_fields["Collector Finding Id"]` per A1 addendum.
- Confirm `cortex-assets` mapping in `parsers/__init__.py` PARSERS is unchanged.
- Confirm `CortexXdrAssets` class is unchanged in `cortexXdrAssetsParser.py`.
- Confirm no edits to `base_parser.py`, `db_schema.py`, `preparation.py`.
- Confirm `parsers/cortex/__init__.py` exports the new class.
- Confirm `parsers/__init__.py` registers the new key.
- Confirm imports resolve (no circular import, no missing symbol).

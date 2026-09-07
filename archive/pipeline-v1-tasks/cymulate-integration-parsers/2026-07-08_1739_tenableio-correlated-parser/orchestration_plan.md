# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: one coherent change set (mode-router hook + declarative spec + fns module + fixtures/tests) with tight coupling between the spec and the hook; worker boundaries would be fuzzy and decomposition would mostly create coordination overhead.

## Research Decisions
- None needed. All external-system assumptions (A1-A6) were VALIDATED with file:line evidence during session recon; the OPEN items (A7 legacy-hydrated support, A8 field parity map, A9 fixture strategy) are internal repo/data inspections owned by execution, not external-behavior research.

## Worker Plan
Not applicable — direct path.

## Verification Obligations
- Cross-check against prompt_contract.md Success Criteria (all bullets).
- Multi-chunk dedup: exactly one asset row per host.id across chunk envelopes in the fixture parse.
- Join correctness: every finding row carries the asset_id of its host's surviving asset row.
- Zero-vuln survival: zero-findings envelopes still yield asset rows.
- Split-mode regression: input_mode == "split" still routes to the untouched deprecated delegate.
- A7 resolution recorded from repo evidence (legacy-hydrated shape-sniff or documented replacement).
- Field parity vs deprecated TenableAssetsAndFindings baseline, deltas documented in execution_notes.md.
- No stamp_asset_id/stamped_uuid anywhere in the new spec.
- Full repo test suite green; no other specs touched; no push.

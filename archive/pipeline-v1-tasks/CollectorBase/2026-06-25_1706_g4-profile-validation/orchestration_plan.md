# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: all changes in ProfileLoader.Validate (one guard term + one shared helper replacing two inline checks) + tests. Single coherent surface.

## Research Decisions
- None needed. Code-grounded.

## Worker Plan
Not applicable — direct path.

## Verification Obligations
- SC1–SC4.
- The new rules must NOT reject any shipped/test profile (verify by loading shipped profiles + full suite green).
- New negative tests: accumulate_list `__`-key; cursor_watermark missing next_token_at/watermark_field; next_url missing next_token_at. Positive: valid cursor_watermark/next_url still load.
- Verifier runs the suite + confirms a real shipped profile (falcon cursor_watermark, defender/qualys next_url) still validates.

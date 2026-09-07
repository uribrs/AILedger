# Decisions

- accumulate_list keys join capture/capture_list under the reserved `__`-guard (same dict, same collision risk).
- A single `ValidatePagination(PaginationSpec, at, errors)` helper replaces the two inline cursor-only checks (sugar + ValidateFetchStep) — DRY and consistent.
- Required-field rules: cursor→next_token_at; cursor_watermark→next_token_at+watermark_field; next_url→next_token_at. Other strategies unconstrained.
- Validation-only; the engine runtime is untouched. Shipped profiles must remain valid (verified, not assumed).

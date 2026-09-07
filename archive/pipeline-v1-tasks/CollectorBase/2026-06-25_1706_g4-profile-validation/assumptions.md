# Assumptions

- A1 — Shipped profiles already satisfy the new pagination rules (falcon cursor_watermark sets next_token_at +
  watermark_field; defender/qualys next_url set next_token_at). STATUS: OPEN — LEAN true; execution VERIFIES by
  the full suite + an explicit load of each shipped profile.
- A2 — `none`/`offset`/`page_number` need no required token path (offset uses {{offset}} tokens). STATUS: OPEN —
  LEAN true; ValidatePagination adds rules only for cursor/cursor_watermark/next_url.
- A3 — The reserved-`__` rule should apply to accumulate_list keys identically to capture/capture_list (same
  CaptureLists dict, same `__drained_<n>` collision risk). STATUS: OPEN — LEAN true.

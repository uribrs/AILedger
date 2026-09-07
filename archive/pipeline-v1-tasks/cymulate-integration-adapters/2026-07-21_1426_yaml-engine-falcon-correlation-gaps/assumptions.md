# Assumptions

- A1 — VALIDATED — Mapping `first_of` mirrors merge-shape semantics (first non-empty
  wins, entries are record paths). Validated by user constraint "reuse existing idioms".
- A2 — OPEN — `regex_extract` semantics: applies a regex with one capture group to a
  string source value; no match / non-string ⇒ null (falls through `first_of`).
  Executor finalizes exact fields (`pattern`, optional `capture` group name/index)
  to match existing transform config style.
- A3 — OPEN — Prefetch v1 scope: implemented for cursor-strategy pagination (the
  merge-target need). Other strategies may reject or ignore the flag in v1 —
  executor decides, loader must fail loudly rather than silently ignore.
- A4 — OPEN — Completeness assertion config shape: a pagination-level opt-in
  (e.g. `expected_total_path`) evaluated when a cursor scroll terminates without a
  continuation cursor; shortfall ⇒ operation failure (retryable), not silent publish.
  Tolerance for live drift (total may move between page 1 and scroll end): executor
  decides between exact-match-with-refetched-total or >= semantics; document choice.
- A5 — OPEN — Re-enabling `cursor_recovery` on the Spotlight merge-source op is a
  stretch goal: `expiry_message_contains` substrings remain unverified against live
  Falcon 404 bodies. A miss degrades to today's hard failure (never worse).
- A6 — VALIDATED — Vendor totals drift live: parity is measured against the vendor
  total AT RUN TIME (meta.pagination.total), not the frozen 358/35,651 numbers.
- A7 — OPEN — The scratchpad parity harness still exists and builds. If the
  scratchpad was cleaned, rebuild it: console app referencing the engine csproj,
  WorkflowRunner.RunAsync + local NDJSON file sink + wire-logging DelegatingHandler,
  creds mapped clientId/clientSecret/apiEndpoint→baseUrl.
- A8 — VALIDATED — Native baseline files at
  `logs/published-batches/20260721-125550/collector-run` are the comparison anchor
  for host set / per-host finding counts (modulo live drift between the two runs).

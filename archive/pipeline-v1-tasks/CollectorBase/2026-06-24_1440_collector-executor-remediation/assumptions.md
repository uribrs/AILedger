# Assumptions

- A1 — VALIDATED: The three audit findings are accurate and located as cited (CursorPaginationStrategy ~:20,
  CursorWatermarkStrategy ~:37, NextUrlPaginationStrategy ~:22 for 1a; Preflight/RunAsync duplication for 2;
  FetchSignal/Signal write-only for 3). Confirmed by three independent reviewers in the audit.
- A2 — OPEN: Nothing reads RunContext.Signal / FetchSignal (the reset works via the returned Paginator.Step).
  The reviewers assert this; the executor MUST confirm by search before deleting (constraint).
- A3 — VALIDATED: A non-string next-token is realistic — some vendors return numeric cursors/offsets at the
  next_token path; coercing to string is the correct tolerant behavior (the token is interpolated as text).

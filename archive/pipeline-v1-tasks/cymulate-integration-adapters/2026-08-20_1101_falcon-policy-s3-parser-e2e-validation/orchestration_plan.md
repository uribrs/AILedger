# Orchestration plan

## Decision

Decompose because the validation crosses four repositories and two runtime boundaries (S3 and PostgreSQL). Work remains read-only with respect to product source.

## Workstreams

### W1 — actual collector artifact and parser replay

- Inputs: completed collector log, manifest, and SHA-256-identified final NDJSON.
- Run the merged CrowdStrike correlated parser through its production option-preparation path.
- Record only aggregate assets/findings/policies counts, output columns/types, policy status/type distributions, and deterministic integrity checks.
- Do not emit customer identifiers or policy bodies.

### W2 — isolated PostgreSQL persistence

- Start a local-only PostgreSQL container/database.
- Apply the parser-owned DDL and persist the W1 policy frame using the production preparation/JDBC path.
- Query row counts, UUID/null validity, JSONB types, edge counts, and scope columns back.
- Remove or stop only task-created runtime resources after evidence is captured.

### W3 — receiving repository source contracts

- Refresh/read `origin/master` for `cybi-db-models` and `cymulate-exposure-analytics` without edits.
- Compare migration columns/indexes and consumer SQL/model expectations to W1/W2 output.
- Report source readiness separately from deployment readiness.

## Dependencies

- W1 requires the immutable downloaded S3 object.
- W2 consumes W1's policy frame and can share the same containerized execution.
- W3 is independent except for the final schema comparison.
- Final verification begins only after W1-W3 evidence files exist.

## Verification and repair

- Independent verifier checks the contract, evidence, assumptions, and decision drift.
- This is non-code-bearing validation; no isolated implementation code review is required unless product source changes unexpectedly occur.

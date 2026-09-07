# Decisions

- The adapters repository is the canonical location for this cross-repository validation task.
- Validation proceeds in data-flow order: collector termination, S3 object inspection, parser execution, PostgreSQL verification, then downstream contract inspection.
- S3 evidence must include the resolved bucket/prefix, object inventory, and redacted inspection of policy-bearing payloads.
- A true parser execution is preferred only after the database endpoint is proven non-production; otherwise use a faithful replay and record the blocked live boundary.
- PostgreSQL success requires both schema compatibility and observed rows attributable to this run; successful connection or table existence alone is insufficient.
- Receiving-repository readiness is assessed from their current `master`/`origin/master` source and reported separately from deployment status.
- If the live collection failed or produced incomplete artifacts, stop downstream replay that would create misleading evidence and report the exact failed gate.
- Resolved: the operator identity safely listed/read the exact run prefix and downloaded the final object read-only.
- Resolved: task-local PostgreSQL 15.18 was created on `127.0.0.1:55432`; 19 attributable policy rows were persisted and queried back.
- Resolved: replayed parser commit `8362681` is an ancestor of refreshed parser `origin/master` `743293d`.
- Rollout decision: validation is complete, but STG promotion is a NO-GO until edge identity and native rule JSON semantics are repaired and replayed.

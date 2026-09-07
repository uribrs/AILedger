# Decisions

- **New task directory, not a continuation of `2026-08-19_1626_tenableio-parser-postgres-load`.** That
  task is closed and explicitly scopes this incident out: "The 2026-08-18 17:12 burst (174 workflow
  runs in 8 min) is a genuinely concurrency-driven failure and is a *separate* problem."
- **Scope is item #4 only.** The three exposure-analytics / finding-api errors are handled in their own
  repos. Bundling them would mix three connection stacks and two languages into one contract.
- **The incident is treated as historical, not live.** Zero connection errors in stg from 2026-08-20
  through 2026-08-27. This sets urgency, not scope: the defects are still latent.
- **Alert fidelity is in scope as a finding even though the fix may not be in this repo.** The alert
  sent the operator to the wrong line; that cost is real and worth recording.
- **The Tenable.io `additional_fields` payload defect is excluded.** It is the prior task's subject and
  has its own fix path. This task must not re-litigate it, but must note where payload width interacts
  with connection hold time.
- **Proceeding on unverified: the ~1000 `max_connections` ceiling.** Inferred from both instances
  capping at 967/968, not read from the parameter group. If wrong: the arithmetic in S4 still shows a
  breach, but the headroom figure any concurrency cap is sized against is wrong, and a repo-side cap
  could be set to a number that does not actually protect the cluster.
- **Proceeding on unverified: an RDS Proxy sits in front of stg Aurora.** Measured by the prior task
  (457 proxy connections, 536 session-pinning warnings), not confirmable by this role. If wrong: the
  connection-path model is wrong and per-run connection cost must be re-derived against a direct
  cluster endpoint.
- **Proceeding on unverified: the Spark 3.3.0 `savePartition` rollback leak is reachable here.** From
  the prior task's research against Spark v3.3.0 `JdbcUtils.scala:741-752`. If wrong: the leak is not
  an amplifier and the burst is pure arrival-rate, which changes which fix layer matters most.
- **Recon before options.** No candidate fix is ranked until S1-S3 have read the actual connection
  lifecycle. The prior task's ranking was revised twice by measurement; this one starts from code.

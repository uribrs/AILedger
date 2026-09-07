# Constraints

- **Read-only against every live environment.** AWS (Glue, S3, RDS/PI, CloudWatch), Mongo stg,
  Elastic/ISB. No writes, no `aws glue start-job-run`, no workflow trigger/resume, no DB mutation,
  no Mongo update.
- **No parser code changes in this task.** Diagnosis only; a fix is a separate contract.
- Do not run the `trigger-epc-workflow` or `glue-workflow-rerun` skills — both mutate live state.
- STG = AWS account 118330362824, region `us-east-1`.
- Evidence hierarchy: source `file:line`, Glue log lines, S3 object listings/byte counts, PI/CloudWatch
  metrics, Mongo documents. Community/blog reasoning about Postgres or Spark ranks below these.
  Anything not backed by one of those must be labelled **speculation** inline.
- Every quantitative claim in the report carries its source (log line, object count, code path, metric).
- **Anchor the clock explicitly.** `aws s3 ls` prints local time (IDT, UTC+3); `date -u` is UTC.
  Anchor with `head-object` or an explicit UTC timestamp before comparing any two clocks.
- **Never conclude "no duplication" from a shard prefix.** Sampling must be strided across the whole
  key space, and any sampled inference must be validated against at least one fully-read shard.
- Code comments and docstrings are not evidence of runtime behavior.
- Elastic ingest can lag 5-8 minutes on a busy pod; do not call a run dead or complete from
  `MAX(@timestamp)` alone — cross-check S3 object mtimes or the Glue run record.
- Claude cannot refresh AWS credentials (`aws-azure-login` is interactive). If a call returns
  `ExpiredToken`, stop and ask the operator rather than working around it.
- STG runs the wheel built from **`master`**, not from the local branch. Any claim about which code
  path executed must be tied to what is in the deployed wheel, not to the working tree.
- The parser writes both `integration.parser_output_assets` and `..._exposures`; keep the two
  separate in every count.

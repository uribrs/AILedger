# Task: What did this repo contribute to the 2026-08-18 parser connection-exhaustion burst?

CA-77018 "Parsers - Closed Connection Errors". Establish what in `cymulate-integration-parsers`
caused or amplified the 2026-08-18 stg incident in which the `stg-cybi-parser` Glue job failed
en masse with Postgres connection errors, separate repo-owned defects from infrastructure-owned
ones, and produce a plan with success criteria.

**This pass produces understanding and a plan. No code changes.** The operator decides the fix layer.

## Scope

In scope: item #4 of the Slack digest only — the `glue-integration-parsers` error. This repo.

Out of scope, and owned elsewhere:
- `exposure-analytics-cron-service` — pg-pool `Connection terminated unexpectedly` on
  `PgNativeCoreService.replicaQueryRaw`.
- `exposure-analytics-server` — `HTTP 404` on `finding-api/msfinding/api/auto-remediation/bulk`.
- `exposure-analytics-create-task-cymulate-service` — Mongoose `cybi_task_rules.find()` buffering
  timeout.

The operator will handle those in their own repos. They are named here only so a later reader knows
they were triaged and deliberately excluded, not missed.

## The incident

On 2026-08-18 between roughly 17:12 and 17:45 IDT, a Tenable.io batch fan-out drove a large number of
concurrent `stg-cybi-parser` Glue runs. `stg-pg-cybi-aurora-cluster` connection count climbed from a
~430 baseline to 967 on instance-0, which was then killed; the load moved to instance-1, reached 968,
and that instance was killed too. Parser runs in flight failed with a mix of psycopg2 and pgjdbc
connection errors.

The alert that produced the ticket points at `sparkDAL.py:60` inside `delete_query` — which is a
**handled** path. The run's fatal error was the Spark JDBC write. Both facts need to be carried
forward, because the alert misdirects.

## Evidence already in hand

Recorded in `prompt_contract.md` Context with its evidence tier and citation. Nothing in this task
should re-derive it; the orchestrator's recon extends it.

## Questions this task must answer

1. How many Postgres connections does one parser run actually open, when in the run, and for how long?
2. Does the `append` + 3-retry write path duplicate rows, and on which lanes?
3. Can `SparkDAL._ensure_connection` detect the failure class it exists to guard against?
4. What is the arithmetic from batch fan-out to the 967/968 ceiling, and which terms are unknown?
5. Which candidate fixes are repo-owned and which are infrastructure-owned?
6. Why did a handled recovery become a Slack "Controller Exception", and what here controls that?

## Not in scope for this task

- Any code change, migration, deploy, or job re-trigger.
- The Tenable.io `additional_fields` payload-width defect — owned by
  `ai/active/2026-08-19_1626_tenableio-parser-postgres-load`, which is a separate, already-closed task.

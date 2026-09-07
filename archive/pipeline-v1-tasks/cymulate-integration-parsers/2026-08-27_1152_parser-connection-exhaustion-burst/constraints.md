# Constraints

## This pass
- No code changes, no migrations, no deploys, no Glue job re-triggers. Stop after `orchestration_plan.md`.
- The fix layer is the operator's decision. The plan must not presume which layer is chosen.
- Deliverable must be readable by the operator as understanding, not only as machine state.

## Blast radius
- `libs/packages/dal/sparkDAL.py` and `libs/packages/dal/dbManager.py` are shared by every parser and
  by the Glue entry point. Any change there affects all parsers in all four environments.
- `libs/packages/parsers/common/base_parser.py` affects every parser's output shape. Do not touch.
- `jobs/cybi-parser/script.py` is the single Glue entry point for every `parser_key`.
- The parser ships as a wheel loaded by zipimport, not a container. A change is only live once
  Jenkins rebuilds and `s3 cp`s `parsers.whl`; a red `Local Tests` stage means the old wheel is still running.

## Tooling
- Package manager is `uv`, never bare `pip`. `uv sync --extra test` before tests that import
  `jsonschema` / `pyyaml`.
- Tests run as `PYTHONPATH=libs/packages uv run pytest`, against real local Spark (~10-20s startup).
  Use targeted `-k <key>` while iterating.
- Commits use `--no-verify`. Currently on `master`, clean tree — branch before any commit.
- `self.options.logger` is the log4j logger: `info`/`warn`/`error` only, single string, no `%s` lazy
  args. No Python `warning()`.

## Environment / access limits (do not plan around reading these)
- Glue CloudWatch retention is **3 days** on `/aws-glue/jobs/error`, `/output`, `/logs-v2`. The
  2026-08-18 driver traceback is gone.
- AccessDenied for the `AAD-Platform` SSO role: `rds:DescribeDBParameters`,
  `rds:DescribeDBClusterParameters`, `rds:DescribeEvents`, `rds:DescribeDBProxies`.
- `s3://cym-glue/stg/libs/parsers.whl` GetObject is 403 to this role (ListBucket is allowed).
- Mongo stg is unreachable from this SSO identity (Atlas MONGODB-AWS, role not a mapped Atlas user).
- No `postgresql` log export enabled on `stg-pg-cybi-aurora-cluster`.

## Evidence discipline
- Prior-art rows from the ledger enter as OPEN assumptions, never as facts.
- Per-run JDBC write concurrency is `min(partitions, task slots)`, not the partition count. Any
  arithmetic in this task uses measured task slots.
- Do not tune against the pgjdbc `08003` message; it can mask the primary exception.

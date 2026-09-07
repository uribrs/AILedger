# Phase 0 — Frozen run identity (shared surface)

Every worker uses these values verbatim. Do not re-derive them.

## Clock anchor (A7 — VALIDATED)

- Local machine / operator screenshots / `aws s3 ls` / `aws glue` output = **IDT = UTC+3**.
- The PI graph axis 15:44-16:14 is IDT → **12:44-13:14 UTC**, 2026-08-19.
- The load spike 15:58-16:07 IDT → **12:58-13:07 UTC**.
- Evidence: `aws glue get-job-runs --job-name stg-cybi-parser` returns
  `StartedOn 2026-08-19T15:41:25.406000+03:00` for the run whose window brackets the spike.

## The run

| field | value |
|---|---|
| Env / account / region | STG, 118330362824, us-east-1 |
| Glue workflow run | `wr_5b88342c5b28610367f1e255dd570199512b396264aaf48c9cdb785dec4d7da4` (stg-epc) |
| Workflow window | 2026-08-19 15:39:07.808 → 16:03:44.989 IDT = **12:39:07 → 13:03:44 UTC** |
| Parser job run | `jr_36b7a68d3fd8305d4118918547486317ea17850b2041d8c66ae060e0eabbefb2` (job `stg-cybi-parser`) |
| Parser window | 2026-08-19 15:41:25.406 → 16:03:44.989 IDT = **12:41:25 → 13:03:44 UTC**; ExecutionTime **1319 s** |
| Parser outcome | **FAILED** — `Error Category: UNCLASSIFIED_ERROR; An error occurred while calling o1845.jdbc. This connection has been closed.` |
| Capacity | Glue 4.0, `WorkerType G.1X`, `NumberOfWorkers 10`, `MaxCapacity 10.0` |
| Trigger | `stg-epc-parser-start` |
| FLOW_NAME | `Tenable.io - Tenable.io Assets and Findings` |
| CLIENT_ID | `613f3842d8ddf900117fccfa` |
| INSTANCE_ID | `21f14fe6-ca31-42a7-87aa-c343bbbb777d` |
| INTEGRATION_SETTING_ID | `0c90af0f-e52e-4808-a728-a62f9bce94ec` |
| INTEGRATION_SETTING_FLOW_ID | `6c4fdda1-4e33-404f-b59f-8666b0271d12` |
| CLIENT_INTEGRATION_ID | `d6d4258a-7671-4268-aaca-33c44d388f55` |
| CLIENT_INTEGRATION_FLOW_ID | `c4c23289-c1bb-4045-8b9d-2a6950056e9b` |
| READ_BUCKET_NAME | `cybi-data` |
| ZIP_FILE_KEY (S3 prefix) | `stg/raw-data/613f3842d8ddf900117fccfa/0c90af0f-e52e-4808-a728-a62f9bce94ec/21f14fe6-ca31-42a7-87aa-c343bbbb777d` |
| Wheel in use | `s3://cym-glue/stg/libs/parsers.whl` (`extra-py-files`) |
| Glue log group | `/aws-glue/jobs` (streams `.../<jr_id>` and `<jr_id>-driver`) |
| RUNNING_ENV | `eks_stg`; Mongo secret `mongo-atlas-eks-stg-yH52mK` |
| `IS_BATCH` | **not set** on this run (it IS set on the Active-Directory run at 13:31 IDT) |

Operator-supplied Mongo `_id` `6a8593320f3d4281bd8a0df0` = ObjectId time **2026-08-19 11:27:46 UTC**
(14:27:46 IDT) — ~74 minutes before the parser started; consistent with a collector run that published
first. Still to be confirmed against Mongo stg.

## Timeline so far (UTC)

| time | event |
|---|---|
| 11:27:46 | Mongo `_id` 6a8593320f3d4281bd8a0df0 created (collector run, unconfirmed) |
| 12:39:07 | stg-epc workflow run starts (dex node) |
| 12:41:25 | stg-cybi-parser starts |
| 12:58 | Postgres load begins climbing off a ~5 AAS baseline (17 min into the parser run) |
| 13:03:44 | parser FAILS: `o1845.jdbc ... This connection has been closed` |
| 13:04-13:07 | load stays at peak, then collapses to baseline by 13:08 |

## Other stg-cybi-parser runs today (IDT), for baseline work

- 15:41-16:03 **FAILED** — Tenable.io (this run), 1319 s
- 15:01-15:03 **FAILED** — Qualys Assets and Findings, 82 s (different client 6745d74c…)
- 13:33-13:39 SUCCEEDED — Active Directory Assets, 324 s
- 09:50-09:54 SUCCEEDED, 01:45-01:49 SUCCEEDED (flows not yet resolved)
- 2026-08-18 17:42-17:54: a burst of ~13 concurrent runs, mostly FAILED (relevant to "is concurrency
  normal here")

# Assumptions

- VALIDATED — The local Falcon run terminated successfully and exposed its exact S3 prefix/object set in logs.
- VALIDATED — The current operator AWS identity listed and read every object needed from the run prefix.
- VALIDATED — All 91 emitted correlated chunks contained the expected version-1 `device_policies` envelope and stable policy content per host.
- VALIDATED — The replayed parser commit `8362681` is an ancestor of refreshed parser `origin/master` `743293d`.
- VALIDATED — The isolated Python 3.10/OpenJDK 11/Spark runtime consumed the actual 324 MB artifact without production writes.
- VALIDATED — Task-local PostgreSQL 15.18 on `127.0.0.1:55432` accepted 19 policy rows through production preparation/JDBC code.
- REJECTED — The assumption that the master source contracts are mutually compatible is false: policy edge identity and nested rule-value representation disagree, despite physical schema compatibility.
- NEVER-TESTED — The exact Exposure Analytics production deployment revision was not queried. The user's colleague confirmed production presence; DB snapshots independently confirm the schema, not the EA binary revision.

## Prior Art

- NEVER-TESTED — No Falcon device-batch error occurred in this run. source: lessons.md#L-16fed2de (executor, 2026-07-26)
- NEVER-TESTED — No Spotlight cursor-expiry body occurred in this run. source: lessons.md#L-882455c1 (researcher, 2026-08-18)
- NEVER-TESTED — The successful local JDBC write did not exercise a dropped PostgreSQL connection. source: lessons.md#L-9a2a1c4d (researcher, 2026-08-19)
- NEVER-TESTED — No deployed parser wheel was read; the exact merged source commit was run locally. source: lessons.md#L-727045fe (executor, 2026-08-19)

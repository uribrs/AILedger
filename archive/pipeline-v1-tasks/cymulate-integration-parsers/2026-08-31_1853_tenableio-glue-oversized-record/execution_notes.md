# Execution Notes

Appended during execution. Empty at contract time.

## Contract-phase record

- Prior-art recall run against `~/Dev/AILedger/lessons.md`. 7 rows matched after head-filtering
  (`L-57acd253` dropped, superseded by `L-9c94d22d`). No truncation. 3 task directories followed.
- Four prior-art `verify:` commands re-run at contract time; all re-confirmed:
  - `L-b345971b` — `tenableAssetsAndFindings.py:560,569,573` present; `output` is a root field
    outside the exclusion set, so it does land in `additional_fields`.
  - `L-9c94d22d` — `TenableIoCollector.cs:34` `IResumableAdapter`, `:448` `CanResumeFrom`,
    `:463` `TenableIoCorrelatedCheckpoint.CanResumeFrom`. Tenable.io IS resume-capable.
  - `L-2186c9a2` — `CrowdstrikeAssetsFindingsCorrelated.py:231` `partitionBy("aid")` present.
  - `L-70bf7cf8` — command re-run but the shell glob failed; not a refutation, not re-confirmed either.
- baseRef captured for both repos in `state.json` under `crossRepo`.

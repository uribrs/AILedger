# Retain a failed collector's checkpoint by marking it terminal

A collector run whose reported terminal status is `failed` stops deleting its checkpoint. The row is
marked `CheckpointStatus.Failed` instead, its claim released and its credentials stripped. The
recovery sweep ignores terminal rows; the cleanup job deletes them once they are older than the
retention window.

## The whole change

1. On a reported `failed` done: `Status = Failed`, claim released, credentials stripped from
   `platform_event_json` — one UPDATE, replacing the existing delete.
2. `GetRecoverableAsync` gains `&& Status != Failed`.
3. `DeleteExpiredAsync` becomes `Status == Failed && UpdatedAtUtc < now - retention`.

`CheckpointStatus.Failed` already exists and is written by nothing. `UpdatedAtUtc` is set by the
status write, so it is the failure timestamp — no new column, no migration.

## Not in this change

The retry/resume trigger; anything outside this repo; the 23h collector staleness bound;
`AdapterDoneMessage`.

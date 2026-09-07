# Retain failed collectors' checkpoints on a `retain_until_utc` clock

Add a nullable `retain_until_utc` (timestamptz) column to `adapter_checkpoints`, filled only when
a collector reports a failed status, set to now + one week. The column is the single source of
truth for retention: no `CheckpointStatus` enum change, no status transition, no CAS write.

## Behaviours

1. On a collector-reported failure, do not delete the checkpoint — stamp `retain_until_utc`.
2. The recovery sweep skips any row whose `retain_until_utc` is set, so retained rows are not
   re-dispatched every five minutes.
3. The cleanup job deletes a retained row once `now > retain_until_utc`, and keeps applying the
   existing `Checkpoint:TtlHours` cutoff to rows where the column is null.
4. The unhandled-exception delete path must not destroy a row that carries a retention stamp.

## Not in this change

The resume/retry endpoint; anything in IntegrationInfra or cymulate-integration-adapters; the 23h
`RecoveryParsingHelper` bound; `AdapterDoneMessage`. This ships from ISB alone.

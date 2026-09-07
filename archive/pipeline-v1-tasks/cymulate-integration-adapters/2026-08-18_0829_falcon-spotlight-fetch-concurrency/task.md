# Falcon Spotlight fetch concurrency

Make the Falcon correlated-findings flow fetch Spotlight vulnerabilities for several AID batches
concurrently, while every published object continues to be written by a single consumer in the same
order as today.

## Current behaviour

`FalconFindingsFlow` Phase 2 walks the frozen key list one batch at a time:

1. read a staged host page from S3
2. scroll Spotlight for that batch's AIDs (`FalconSpotlightBatchScroller.EmitBatchRecordsAsync`)
3. correlate into per-host accumulators
4. publish one NDJSON object, write the checkpoint, `AdvancePage`
5. next batch

Exactly one HTTP request is in flight at any moment for the whole run.

## Target behaviour

N batches scroll Spotlight concurrently into a bounded buffer. One consumer drains that buffer and
performs publish → checkpoint → `AdvancePage` in frozen-key-list order, byte-identical to today.
N comes from configuration.

## Out of scope

- Parallel publish, and any checkpoint format change (stays v4).
- Raising `AidBatchSize` above 4.
- Reading vendor rate-limit response headers.
- Any change to the assets flow.

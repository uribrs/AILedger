# Task: Recovery budget — progress-anchored stuck detection

Make the Shared deferred-recovery retry budget bound **consecutive no-progress** recoveries instead of **total lifetime** recoveries.

## Problem

`_resilience.recovery.attemptCount` is a single shared lifetime counter in checkpoint `AdapterState`. It is not partitioned by failure type, and it resets only on full-scan success — never on forward progress. Every healthy page re-persists the stale count. A long, healthy scan with sporadic-but-recovered transients (5xx / cursor-expiry / 401) accumulates to `MaxRetries` and is terminally failed near completion, despite making real progress.

## Fix

Anchor the budget to a durable **progress coordinate** derived from checkpoint state. Reset the counter when the coordinate advances; increment only when consecutive deferred recoveries make no progress. Add a generous whole-collection backstop (total deferrals OR recovery age) so progress-anchoring cannot livelock forever.

## Scope

Shared resilience layer only. Global change — affects every collector. No per-collector flow-loop changes.

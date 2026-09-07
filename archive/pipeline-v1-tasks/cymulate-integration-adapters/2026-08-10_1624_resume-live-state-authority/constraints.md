# Constraints

## Repos and bases

- Two repos change: `IntegrationInfra` and `cymulate-integration-adapters`.
- Both branch from freshly pulled `origin/dev`.
  - Adapters base: `6f8b58b3` (`CollectorVersion` 6.1.1 on dev).
  - Infra base: `6dccb87`.
- `IntegrationServiceBus` must not be modified. Its lease/heartbeat defects are a separate branch and PR.

## Layer ownership

- The substrate owns sequencing, session lifecycle, and state persistence. The collector owns vendor
  request logic, pagination, checkpoint *formats*, and flow exception classification.
- The fix for state authority belongs in the substrate, not in each collector.
- A collector recovery hook may only re-anchor vendor-side continuation (a dead cursor, a query floor).
  It must never restore position, totals, or watermarks.

## Invariant to establish

- `AdapterProgressContext.AdapterState` is the single live authority for collector state on every leg,
  fresh or resumed, from the first instruction.
- Nothing may write a value into `AdapterState` that is older than what is already there.
- The typed leg-start state object (`WorkItem` / `StrategyExecutionState.Current`) is advisory input to
  the flow, never a source for persistence.

## Behaviour that must not regress

- A pre-publish deferral on a resumed leg must persist the collector state as loaded, not a stripped blob.
- A post-publish deferral must persist the advanced position.
- The recovery-budget stuck gate and whole-collection backstop must behave unchanged. The progress
  coordinate derives from `CurrentPage`/`ProcessedItems`/`ProcessedFindings`, not from `AdapterState`.
- The Falcon assets flow must still drop its dead `after` cursor across a scheduled wait exceeding the
  120 s CrowdStrike cursor TTL, and must still decline when no watermark exists to continue from.
- Zero-finding hosts must still emit their chunk-0 record (asset spine completeness).
- All eleven hookless collectors must keep working without per-collector changes.

## Out of scope

- The rolling ~50 MiB host-atomic publisher. No host is provably complete before batch end
  (`FalconSpotlightBatchScroller.cs:156-158` flushes partial chunks mid-scroll; `:183-187` seals hosts at
  batch end), so this needs a host-aware publisher or per-host staging.
- Moving the authoritative traversal coordinate into `current_page`. External review established this
  does not by itself reject a stale write; safety would require `current_page` to be the sole authority
  plus a set/advance-to primitive that increment-only `AdvancePage` does not provide.
- Any change to output object naming, addressing, or placement.

## Delivery

- Infra ships as a NuGet package; adapters must bump the pinned Infra version. Version coordination is
  part of the deliverable, not a follow-up.
- Full Falcon test assembly must pass. 143 Falcon tests are currently discovered.
- No test may assert reference identity between a recovery-hook result and the leg-start state object.

# Recovery outcome shape

Give the checkpoint and recovery mechanism one coherent shape across IntegrationInfra,
IntegrationServiceBus and cymulate-integration-adapters, so that a checkpoint is authoritative
state-zero at resume and requires no interpretation.

## Why this exists

Collections and queries run for hours or days on Kubernetes pods that are volatile by definition.
Interrupted work must be picked up from a checkpoint that states exactly what was completed and must
not be redone. The checkpoint IS state zero at resume — there is no room for consideration.

Checkpoint shapes differ per collector because each vendor has its own collection strategy. That is
intended and must be preserved. IntegrationServiceBus does not impose a uniform shape; it gives
collectors the means to communicate their case.

## The insight that drives the design

A recorded position is only meaningful if everything before it was flushed. If half a unit was
collected but not published, the position is wrong in one of two directions: re-collect what was
already published (waste), or skip what was not (data loss).

Interruptions therefore split in two:

- **Cooperative** — cancellation, deferral, transient error, graceful shutdown, lost claim. The run
  can flush what it holds and then record an exact position. Waste: zero.
- **Non-cooperative** — SIGKILL, node loss, out-of-memory. Nothing can be done. The last recorded
  position stands and work since then is redone. Waste: bounded by checkpoint frequency.

Today every interruption is treated as non-cooperative.

## The six points

1. One outcome vocabulary in IntegrationInfra that every layer maps onto: Completed, Deferred(until),
   InterruptedRecoverable, FailedTransient, FailedPermanent, Cancelled, Superseded, Incompatible.
2. Flush on cooperative yield, so the recorded position is exact.
3. Checkpoint-write refusals carry a reason and are actionable. A worker that has lost its claim must
   stop, not keep publishing against a row it no longer owns.
4. State-version compatibility is checked before claiming a checkpoint, not after dispatch.
5. One owner for position: the collector decides, the host stores.
6. Measure resume waste, so flush-on-yield can be shown to work and a regression is visible.

## Prerequisite work inside this task

- The Falcon test assembly does not compile (11 errors, v3 to v4 migration debt). Nothing in the
  adapters repo can be tested until it does.
- An uncommitted change in `FalconCollector.cs` makes the refused-resume guard emit a partial-success
  result instead of throwing. It builds clean but has no test.

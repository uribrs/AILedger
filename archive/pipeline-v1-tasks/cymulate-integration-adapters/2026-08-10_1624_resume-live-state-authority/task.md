# Resume legs must treat live progress state as the single authority

## What is wrong

A collector leg resumed from a checkpoint holds two representations of its state: the typed object
deserialized at leg start, and the live `AdapterProgressContext.AdapterState` that the flow updates as
it publishes. On a deferral, whichever of the two is in `AdapterState` when
`PersistDeferredWaitSnapshot` fires is what gets written to `adapter_checkpoints.adapter_state_json`,
full-replace.

Two defects follow from that ambiguity.

**Defect A — Falcon overwrites live state with the leg-start object.**
`FalconRecoveryContinuationBuilder.BuildFindingsRecoveryState` returns `context.WorkItem`, and
`FalconCollectorRecoveryHandlers.cs:116` applies it over live state. `WorkItem` is
`StrategyExecutionState.Current`, which is get-only and assigned once at leg start. Every deferral on a
resumed leg therefore persists that leg's *start* position, discarding every page it published. The
persisted position cannot preserve advancement across accepted resumed deferrals.

**Defect B — Infra never seeds collector state onto a resumed leg.**
`CollectorResumeSetup.cs:69` seeds only the recovery-budget keys. Collector keys are deliberately
excluded on the reasoning that the flow re-derives them; the flow re-derives them into local
variables, and nothing writes them back into `AdapterState` until the first publish. A deferral that
fires on a resumed leg *before* its first batch therefore persists a blob containing budget keys and
nothing else, wiping the collector's checkpoint.

Falcon is the only collector that supplies a recovery hook. The other eleven resume runners declare
`recoverAsync = null`, so Defect B is unmitigated for all of them. Falcon's hook is a mitigation for
Defect B, and it is how Falcon acquired Defect A.

## What to do

Fix it once, in the substrate, so no collector has to decide which representation is current.

1. Seed the persisted collector state keys into `AdapterState` at resume. Live state becomes populated
   and authoritative from the first instruction of the leg.
2. Delete the Falcon findings recovery hook — with (1) in place it restores state that is no longer
   missing.
3. Reduce the Falcon assets recovery hook to what only it can do: drop the dead `after` cursor and
   re-anchor the query floor. It must not restore position or totals.
4. Repair the tests and harness doubles that currently specify the defective behaviour.

## Scope boundary

`IntegrationServiceBus` is not touched. The rolling ~50 MiB host-atomic publisher is not built. The
authoritative traversal coordinate is not moved into the `current_page` column.

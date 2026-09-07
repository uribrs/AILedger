# Operator-initiated resume of a failed collection — backend side

An operator viewing a failed collection run in Admin presses a control that resumes it from its
retained IntegrationServiceBus checkpoint instead of recollecting from the beginning.

## What already exists downstream

ISB is complete and out of scope. It retains a failed collector's checkpoint for 7 days
(`Checkpoint:FailedRetentionDays`), accepts an optional boolean `forceResume` at the ROOT of the
`collectors.run` message, and keys the checkpoint on `correlationId` — which equals
`String(instance._id)` of the `CybiClientIntegrationInstance` document. It reports nothing new on
`collectors.done`.

## The change

1. **Admin** — a Resume control on a failed instance row, in the instances table's status column
   beside where Stop renders. Offered only while the checkpoint can still exist.
2. **cymulate-integrations** — revive the *same* instance document, same `_id`, back to `pending`
   with the resume marker set. The scheduler is a poller, so reviving the document *is* the dispatch.
3. **cymulate-integrations** — copy the marker onto the wire at the envelope root as a real JSON
   boolean.

## The single constraint everything else serves

The checkpoint is keyed on `String(instance._id)`. A new instance document is a new correlationId and
cannot find the checkpoint. Every part of this task exists to re-dispatch under the original `_id`.

## Not in this change

Any ISB change. `collectors.done` and its consumer, beyond one guard that stops a concurrent
same-flow failure from cancelling a revived run. The agent-dispatched population, which has its own
poller in the separate Dispatcher app and holds no ISB checkpoint.

# Constraints

## Identity — the constraint the feature depends on

- The revived run MUST keep the original `_id`. `correlationId == String(instance._id)` is the ISB
  checkpoint key; a new document is a new key and silently full-collects.
- Never mint a new instance document for a resume. `runClientIntegrationFlow`, the EA run path and
  `scheduleNextInstance` all create fresh documents — none of those shapes may be copied here.

## Instance revival

- Revive with a conditional update, not read-then-write:
  `findOneAndUpdate({ _id, status: 'failed' }, { $set: {...} })`. A null return IS the
  "already revived by another operator" answer. No lock — the precondition is the idempotency.
- `inQueue: false` is mandatory. The poller matches
  `{ inQueue: false, startedAt: { $lte: now }, status: CybiStatus.Pending }`; the failed dispatch set
  `inQueue: true` and nothing clears it.
- `endedAt` must not survive the revival — a stale `endedAt` makes a running row read as finished.
- `startedAt` must satisfy `$lte: now` at the next tick.

## Do not touch `triggerType`

- `collection-scheduler.service.ts` computes `isCollectNow = triggerType === 'collect_now'`; when
  false it calls `scheduleNextInstance`, which builds the next occurrence as `{ ...instance, ... }`.
- Changing `triggerType` for a resume would BOTH spawn an unwanted scheduled recurrence AND inherit
  the resume marker into that scheduled run.
- `triggerType` stays `'collect_now'`. The marker lives on its own dedicated field.
- Strip that field inside `scheduleNextInstance` alongside the existing `delete newInstance._id`.

## Wire contract — strict, and fails silently when missed

- Key is exactly `forceResume`, case-sensitive.
- Placed at the ROOT of the envelope, beside `correlationId`. Not in `payload`, not in `metadata`.
  ISB reads the root only.
- Value must be a JSON boolean literal `true`. The string `"true"` and the number `1` are both read
  as false by ISB's `AdapterRunMessage.ReadForceResume` (`JsonValueKind.True` only).
- Never derive the flag from a string without converting to a real boolean.
- `prepareCollectorEnvelope`'s root is a closed literal — a field not explicitly copied does not
  travel, even though the instance document reaches the function intact.

## Scope

- No change to IntegrationServiceBus.
- No change to `collectors.done` or its consumer beyond the minimal `cancelPendingInstances` guard.
- Do not touch the agent-dispatched population or the Dispatcher app. Resume is cloud-to-cloud only:
  `findCandidates` filters `connectionMode: 'direct'` via a `$lookup` on the parent
  `CybiClientIntegration`, and agent runs hold no ISB checkpoint.

## Admin control

- Enabled only when `status == 'failed'` AND `(now - endedAt) < 7 days`.
- The 7 is hardcoded with a comment naming `Checkpoint:FailedRetentionDays` on the ISB side —
  changing one without the other makes the control lie.
- Enforce the same conditions server-side. The UI gate is convenience only, as it already is for the
  developer-only delete.

## Repo conventions

- `cymulate-integrations` has no repo-level CLAUDE.md; follow the surrounding code's idiom.
- `Admin` CLAUDE.md covers only the Prisma/`cybi-db-models` submodule. This task is Mongoose, not
  Prisma, so no submodule work is expected. If a Prisma error appears, that is the submodule, not
  this change.

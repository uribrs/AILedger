Role:
You are a senior full-stack engineer working across two Cymulate repositories: the NestJS/Nx monorepo
`cymulate-integrations` (primary) and the Fastify/EJS `Admin` app.

Goal:
Let an operator resume a failed cloud-to-cloud collection from its retained IntegrationServiceBus
checkpoint, by pressing a control on the failed run's row in Admin. The resumed run must re-dispatch
under the ORIGINAL instance `_id`, carrying `forceResume: true` at the root of the `collectors.run`
message.

Context:
- ISB is complete and out of scope (branch `feat/failed-checkpoint-retention-ttl`, its own archived
  task, verified 47/47 against real Postgres). It retains a failed collector's checkpoint for 7 days
  (`Checkpoint:FailedRetentionDays`), accepts an optional boolean `forceResume` at the message ROOT,
  and keys the checkpoint on `correlationId == String(instance._id)`.
- The scheduler is a poller, not a calendar. `CollectionSchedulerService.tick` runs every 10 seconds
  and `findCandidates` matches `{ inQueue: false, startedAt: { $lte: now }, status: Pending }` joined
  to a parent integration with `connectionMode: 'direct'`. Reviving the document IS the dispatch —
  there is no new queue and no new endpoint to build.
- `prepareCollectorEnvelope` in `libs/collectors-helpers/src/lib/collectors-helpers.service.ts`
  builds the wire message. Its root object is a closed literal.
- Admin's `Stop` is the closest precedent for an operator action on an existing instance:
  `stopClientIntegrationInstance` in
  `Application/app/controllers/exposureAnalyticsIntegrations.controller.js`, routed from
  `Application/app/routes/client.routes.js`, with its button rendered inside the instances
  DataTable's status column in
  `Application/views/exposure_analytics_client_integration_details.ejs` and its click handler in the
  same file.
- Symbol names are the reliable anchors, not line numbers: `prepareCollectorEnvelope`,
  `findCandidates`, `processCandidate`, `scheduleNextInstance`, `cancelPendingInstances`,
  `stopClientIntegrationInstance`, `buildInstanceRow`, `CybiClientIntegrationInstance`,
  `connectionMode`.

Constraints:
* Keep the original `_id`. Never mint a new instance document for a resume.
* Revive with `findOneAndUpdate({ _id, status: 'failed' }, { $set: {...} })`. A null return IS the
  "already revived" answer. No lock, no read-then-write.
* `inQueue: false` is mandatory — nothing clears it on failure.
* `endedAt` must not survive the revival; `startedAt` must satisfy `$lte: now` at the next tick.
* `triggerType` stays `'collect_now'`. The marker is its own field. Strip it inside
  `scheduleNextInstance` beside the existing `delete newInstance._id`.
* Wire key is exactly `forceResume`, case-sensitive, at the envelope ROOT beside `correlationId` —
  not in `payload`, not in `metadata`. Value must be a JSON boolean literal `true`; `"true"` and `1`
  are read as false by ISB.
* Guard `cancelPendingInstances` so a concurrent same-flow failure cannot cancel a revived resume.
  One predicate, not a redesign.
* Admin control enabled only when `status == 'failed'` AND `(now - endedAt) < 7 days`, hardcoded with
  a comment naming `Checkpoint:FailedRetentionDays`. Re-check both server-side.
* No ISB change. No change to `collectors.done` or its consumer beyond the one guard. Do not touch
  the agent-dispatched population or the Dispatcher app.
* `CybiStatus.Failed == "failed"` (lowercase), from `cy-shared-db-models/models/attack/attack.enums`.
* Follow the surrounding code's idiom in each repo; `cymulate-integrations` has no repo CLAUDE.md and
  `Admin`'s covers only the Prisma submodule, which this task does not touch.
* Do not commit, push, or open a PR. Show what changed and wait.

Success Criteria:
* A failed instance revived by the control is picked up by the poller within one tick (10 seconds).
* The emitted `collectors.run` message carries `forceResume: true` at the ROOT, as a JSON boolean,
  and the ORIGINAL `_id` as `correlationId`.
* Pressing the control twice dispatches once; the second attempt is refused by the `status: 'failed'`
  precondition, not by a lock.
* A failed run older than the retention window offers no control, in the UI and at the server.
* An ordinary (non-resume) dispatch is byte-for-byte unchanged — no `forceResume` key on the wire.
* A resume never spawns a scheduled recurrence, and no scheduled occurrence inherits the marker.
* A concurrent same-flow failure does not cancel a revived resume.
* Tests cover: the revive precondition including the second-press refusal; the wire shape
  (root placement, exact key, boolean type, absent when not a resume); the retention-window gate.
* Affected projects lint, test and build. Report anything Docker-gated or otherwise not run.

Execution Rules:
* Do not assume missing data
* Respect constraints strictly
* Confirm A6, A7 and A9 by reading the code before relying on them — each is cheap and each changes
  the implementation if it is false
* Surface, do not work around, any contradiction with the settled design
* If the marker cannot persist without a shared-schema change, say so before making one

Output Format:
Working tree changes in both repos, plus `execution_notes.md` recording: files changed per repo,
where the marker is set and read, how the wire shape was verified, what the `cancelPendingInstances`
guard does, and lint/test/build results with anything not run called out explicitly.

Stop Conditions:
* When the goal is achieved
* When a constraint cannot be satisfied without violating another
* When the change would require touching ISB, the Dispatcher app, or a `cy-shared-db-models` schema
  release

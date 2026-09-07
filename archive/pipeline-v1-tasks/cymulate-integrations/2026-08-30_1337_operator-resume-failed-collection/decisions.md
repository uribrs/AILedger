# Decisions

- **The marker is its own field, named `forceResume`, and is not `triggerType`.** The downstream
  brief suggested carrying it on the trigger string; that field gates `isCollectNow` in the
  scheduler, and changing it would spawn a scheduled recurrence carrying the marker. Naming the
  instance field the same as the wire field removes a translation step and with it the string→bool
  conversion the wire contract warns about.

- **Revival reuses the failed document rather than creating a new one.** This costs the failure's
  visibility in the instances table — the row leaves `failed` and becomes `pending`. Accepted,
  because the alternative (new document, old `_id` emitted as correlationId) splits run identity
  between the instance and the checkpoint and would leave `collectionDone` routing ambiguous.

- **The gate is duplicated in the UI and the controller.** The UI gate is convenience; the server
  re-checks status and the retention window. Matches the existing developer-only delete, where the
  comment says the same thing.

- **The retention window is hardcoded in Admin, not read from ISB.** ISB exposes no endpoint for it.
  A comment naming `Checkpoint:FailedRetentionDays` is the only link between the two numbers, so the
  comment is load-bearing and must name the key exactly.

- **The `cancelPendingInstances` guard is in scope, narrowly.** The race is pre-existing but only
  becomes reachable because this change parks a revived instance in `pending` with `inQueue: false`.
  Shipping a control that another flow's failure can silently cancel is not acceptable. The guard is
  one predicate, not a redesign of finalization.

- Proceeding on unverified: no collector re-checks staleness inside `ResumeAsync`, so the forced
  resume actually resumes. If wrong: for those vendors the run fails loudly with
  `RESUME_DECLINED_RETAINED_CHECKPOINT` rather than restarting — visible, not silent, and the
  operator can fall back to Run Now.

- Proceeding on unverified: the marker persisted on the instance is harmless after finalization.
  If wrong: a later ordinary dispatch of the same document would force a resume nobody requested.
  Mitigated cheaply by clearing the marker when the run is claimed, if the executor finds a claim
  path that can carry it.

- **Surfacing the resume in the Admin table is optional and deferred to execution.** The original
  brief wanted resumes visible in the trigger column "for free"; with a separate marker field that is
  a small addition to `buildInstanceRow`'s composed trigger string. Include it only if it stays a
  one-line change to an existing column — do not add a column.

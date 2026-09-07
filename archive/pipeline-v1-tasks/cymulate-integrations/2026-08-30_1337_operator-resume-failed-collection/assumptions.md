# Assumptions

Every entry is OPEN. This skill holds no evidence and may not write any other status.

## Prior Art

Tags: `cymulate-integrations`, `integration-service-bus`, `checkpoint-resume`, `cross-repo-contract`.

Matched 28 rows across those tags; head-filtered `L-57acd253` (superseded by `L-9c94d22d`); triaged
the newest 10; followed 1 pointer (`2026-08-30_1130_force-resume-on-operator-dispatch`, which carries
three of the top hits and was read in full this session).

- **A1** OPEN — Adding an optional field to an inbound wire message is additive and cannot break the
  consumer. source: lessons.md#L-645966cc (verifier, 2026-08-30). ISB tried a bound typed `bool` and
  removed it: a non-boolean value failed deserialization of the *whole* message and settled the
  delivery as Retry. We are the producer this row is about. Bears directly on the wire-shape work.
- **A2** OPEN — A plain retry on the same correlation id is harmless when a retained checkpoint
  exists. source: lessons.md#L-e9b2e21e (verifier, 2026-08-30). REFUTED downstream: taking the
  execution lock commits Failed→Idle before the resume decision. Bears on what happens if our marker
  fails to arrive — the failure is loud (`RESUME_DECLINED_RETAINED_CHECKPOINT`), not a silent
  recollect.
- **A3** OPEN — Bypassing `CanResumeFrom` is sufficient to force a resume, because no collector
  re-checks staleness inside `ResumeAsync`. source: lessons.md#L-7448468a (verifier, 2026-08-30),
  status UNTESTED, evidenced for Falcon and CloudGuard only. Bears on the end-to-end success
  criterion: for some vendors a forced resume may still be declined and fail.
- **A4** OPEN — A Mongo `_id` handed over as a run identifier resolves to its run document.
  source: lessons.md#L-ab924833 (verifier, 2026-08-19), status UNTESTED.

## Task assumptions

- **A5** OPEN — `prepareCollectorEnvelope` receives the instance document with the marker field
  readable on it (not a projection that drops unknown fields). The envelope builder is reached from
  `processCandidate`, which passes the aggregation result from `findCandidates`.
- **A6** OPEN — Credentials are recomputed at dispatch from current config and never replayed off the
  instance, so ISB stripping credentials from the retained checkpoint is safe and a resumed run
  authenticates normally. Cheap to confirm by reading `prepareIntegrationAction`.
- **A7** OPEN — `collectors.done` writes a fixed `$set` list with no `$unset` and no document
  replace, so a marker set on the instance survives run finalization. Confirmable by reading
  `collectionDone`'s update calls.
- **A8** OPEN — A marker left set on a finalized instance causes no harm on a later ordinary
  dispatch of that same document. Related to A7: if the marker persists and the document is ever
  re-dispatched by another path, it would force a resume nobody asked for.
- **A9** OPEN — Adding a field to the instance document does not require a `cy-shared-db-models`
  schema change to persist. `stopRequested` precedent uses `{ strict: false }` on the update, which
  suggests undeclared fields ARE dropped by default. This decides whether the marker needs
  `strict: false`, a schema change, or reuse of an already-declared field.
- **A10** OPEN — If the checkpoint was deleted or never existed, a forced dispatch simply starts
  fresh and the control degrades safely without erroring.
- **A11** OPEN — The instances DataTable's status-column render receives enough of the row to decide
  the retention window. `buildInstanceRow` currently emits `endedAt` at a fixed column index; the
  render function reads `row[0]` for the id, so other indices are reachable the same way.
- **A12** OPEN — No other consumer of `CybiClientIntegrationInstance` treats a `failed`→`pending`
  transition as impossible. Reviving a terminal row is a lifecycle change that other readers may not
  expect.

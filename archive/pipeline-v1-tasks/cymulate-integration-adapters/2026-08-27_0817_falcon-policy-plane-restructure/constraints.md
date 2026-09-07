# Constraints

## Direction is settled — do not re-open

- Prevention assignments MUST keep using `POST /devices/entities/devices/v2`
  against the frozen host list. Do NOT replace it with a policy-members sweep.
- `GET /policy/combined/prevention-members/v1` is offset-paginated and Falcon
  rejects `limit + offset > 10000` (400 `"limit + offset must be less than
  10000"`; falconpy discussions #536, #1146; psfalcon #487). It cannot enumerate
  97,332 devices. The device-entities call has no ceiling because we supply ids.
- Prevention-only. Do not widen to sensor-update / device-control / firewall
  planes.

## Structural

- The Phase 1 manifest MUST be written after the Discover scroll and before any
  policy work. It is the completion proof for the scroll and the denominator
  source (`hostCount`, `pageKeys`) for every later stage.
- Staged host pages are immutable after freeze. No stage may rewrite
  `hosts_NNNNNN.json`.
- Policy definitions MUST NOT be embedded per host record.
- Policy definitions live under their own generation prefix
  (`_staging/policies/pgen_<id>/`), never under the host generation, so a
  Discover re-spool cannot discard them.
- Every traversal unit writes its object even when the content is empty.
- Every skip/drop/reject is counted, and the counts land in the manifest ledger.

## Gate semantics

- Spotlight gates on TRAVERSED, never on SUCCEEDED. Traversed means every unit
  in the frozen inventory has a recorded outcome.
- A policy stage that ends empty MUST still unblock spotlight.
- The only hard stop is a Discover freeze that never completed.

## Preserve

- The two pre-existing uncommitted branch changes (config default false,
  CollectorVersion 6.3.4) must remain intact.
- Existing asymmetry stays: an assignment is the relationship, a definition is
  supplemental. A definition outage degrades to partial/unavailable; it does not
  fail the unit.
- `FalconDevicePoliciesEnvelope` wire vocabulary (`schema_version`,
  `collection_status`, `definition_status`) must not drift — the parser reads it.

## Evidence

- Do not record any batch bound as validated without the vendor `errors` payload
  from a failing run (see L-16fed2de).
- Do not parse vendor prose to identify a rejected AID. Derive the skipped set by
  comparing requested ids against returned ids.

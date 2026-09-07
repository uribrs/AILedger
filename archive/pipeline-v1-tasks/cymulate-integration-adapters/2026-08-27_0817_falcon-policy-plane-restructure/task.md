# Task — Falcon Policy Plane Restructure

Restructure CrowdStrike Falcon prevention-policy collection in FalconCollector's
correlated `CollectFindings` flow so that policy work is a stage over a frozen
host inventory rather than an enrichment inside the Discover spool.

## Scope

- Phase 1 manifest is written immediately after the Discover scroll, before any
  policy work, and becomes a staged ledger of per-stage terminal state plus
  coverage counters.
- Staged host pages are immutable after freeze. No stage rewrites them.
- Policy output becomes a plane: definitions once, per-host edges separately.
- Prevention policy definitions come from the policies lane, under their own
  generation folder, independent of the host generation.
- Prevention assignments continue to come from the host-directed device-entities
  call against the frozen list.
- Partial-rejection, probe, classifier, mid-scroll-500, and empty-unit defenses.

## Non-goals

- Phase 2 throughput.
- Non-prevention policy planes.
- Restoring `EnablePreventionPolicyEnrichment` to default true.

## Branch state on entry

Two uncommitted changes exist on the branch and are NOT part of this task. They
must survive it:

- `FalconCollectorConfiguration.cs:179` — `EnablePreventionPolicyEnrichment`
  default `true` -> `false` (operator's production unblock).
- `Collectors/Directory.Build.props` — `CollectorVersion` `6.3.3` -> `6.3.4`.

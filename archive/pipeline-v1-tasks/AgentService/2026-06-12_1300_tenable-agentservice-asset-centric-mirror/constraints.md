# Constraints

- Adapter collector + its `Documentation/` folder are the authoritative spec; mirror the behavior, do not re-derive the design.
- Behavioral mirror, NOT a line-for-line copy — adapt to the AgentService engine (15-concurrent chunks + enrichment lane, batchful upload).
- Modify only `/Users/user/Dev/AgentService/Source/CybiCollectors/TenableCollector` (+ its docs). Do not touch the adapter or parser repos.
- Add `/assets/export` lane with `filters.last_assessed` (30-day window); emit full inventory incl. no-vuln hosts.
- `/vulns/export`: remove the info-severity filter (collect ALL severities incl. info); keep `state=[OPEN,REOPENED]` (skip FIXED).
- Remove the per-asset `/assets/{id}` enrichment hunt entirely.
- v3 ACR comes from the bulk assets feed `ratings.acr.score` (null tolerated when absent).
- Emit raw, un-joined feeds — correlation/field-mapping is the parser's job (collector stays "dumb").
- Both lanes must co-locate for one combined collection run (so the split parser receives `assets*` + `findings*` together); an assets-only run emits assets only.
- Preserve the agent-service collector's existing resumability / batchful-upload guarantees; do not regress them.
- Do not introduce duplicate records on resume (mirror C1 intent if the analogous exposure exists).
- Must build. Local execution is not possible — correctness validated in STG by the user.
- Document the mirror equivalently to the adapter `Documentation/`.

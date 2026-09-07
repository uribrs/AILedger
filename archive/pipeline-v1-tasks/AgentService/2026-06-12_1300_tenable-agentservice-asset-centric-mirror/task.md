# Task: Mirror Tenable.io asset-centric design into the AgentService collector

Port the asset-inventory-centric Tenable.io design — already built, verified, and documented in the integration **adapter** — into the legacy **AgentService** collector, adapting to that codebase's architecture (behavioral mirror, not a line-for-line copy).

## Repos
- **Reference / spec (read-only):** `/Users/user/Dev/Uri/cymulate-integration-adapters/.../Collectors/TenableIoCollector` + its `Documentation/` folder (the canonical design map). Committed `da393e5`.
- **Target (modify):** `/Users/user/Dev/AgentService/Source/CybiCollectors/TenableCollector/TenableIoCollector.cs` (~1144 lines), branch `tenable-to-assets-first`.

## Target current state (old vulns-centric design)
- Info-severity filter ON.
- Only `/vulns/export` (no `/assets/export` lane).
- Per-asset `/assets/{id}` enrichment lane present (`enrichmentTask`, `Task.WhenAll(chunkProcessing, enrichment)`).
- 15-concurrent chunk engine + separate enrichment lane — different architecture from the adapter's progressive single-pump engine.

## What changes (mirror from the adapter)
1. Add an `/assets/export` lane (`filters.last_assessed` 30d) → full inventory incl. no-vuln hosts, as its own raw feed.
2. `/vulns/export`: drop the info-severity filter (ALL severities); keep `state=[OPEN,REOPENED]` (skip FIXED).
3. Remove the per-asset `/assets/{id}` enrichment; take v3 ACR from the bulk assets feed (`ratings.acr.score`).
4. Co-locate both lanes for one collection run so the split parser receives them together (assets-then-findings into one egress; assets-only run = assets only).
5. Mirror the C1 mid-chunk-resume duplication fix **intent** where the agent-service engine has the analogous exposure (open — engine differs).
6. Document the agent-service mirror equivalently to the adapter `Documentation/`.

## Out of scope
- Re-deriving the design (the adapter + its Documentation/ are the spec).
- Local validation of the agent service (not runnable locally; STG-tested by the user).
- Parser changes (already done in the parser repo).

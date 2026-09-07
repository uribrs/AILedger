# Decisions

- The adapter `TenableIoCollector` + its `Documentation/` folder are the authoritative spec; this task mirrors them. No design re-derivation.
- Behavioral mirror adapted to the agent-service architecture — explicitly NOT a line-for-line port (the engines differ).
- Endpoints/filters are settled (validated against live data): `/assets/export` with `filters.last_assessed` (30d); `/vulns/export` with `filters.since` + `state=[OPEN,REOPENED]`; ALL severities incl. info.
- Drop the per-asset `/assets/{id}` enrichment; v3 ACR comes from the bulk assets feed `ratings.acr.score` (null allowed).
- Collector stays "dumb": emit raw un-joined feeds; correlation + field mapping is the parser's job.
- Dual lanes must co-locate for one combined run (split parser requires both lanes together; assets-only is unsafe for the findings parser).
- C1 fix is mirrored as INTENT, not as a code port — only if the agent-service engine has the analogous mid-chunk-resume duplication exposure (to be scoped).
- The agent-service mirror is documented equivalently to the adapter `Documentation/`.
- **In-repo template = the sibling FalconCollector.** AgentService's FalconCollector already runs assets-then-findings co-located via `ICybiBatchUploader` with no enrichment — mirror its structure, NOT the SDK adapter's pipeline/checkpoint machinery (which doesn't exist in AgentService).
- **No C1 work.** The legacy collector has no checkpoint/resume, so mid-chunk-resume duplication can't occur; the C1-adjacent fix is to stop swallowing non-cancellation exceptions (silent truncation currently reports success).
- **Add `state=[OPEN,REOPENED]` to the `/vulns/export` create body** — today it sends only `since`, so FIXED vulns are currently pulled; the mirror must add the state filter (and never a severity filter).

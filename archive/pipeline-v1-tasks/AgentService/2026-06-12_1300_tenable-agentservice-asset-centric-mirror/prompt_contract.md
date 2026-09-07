# Prompt Contract — Mirror Tenable.io asset-centric design into AgentService

## Role
You are a senior .NET engineer porting a validated collector design across two
different codebases: from a modern integration adapter (the reference) into a
legacy AgentService collector with a different engine, preserving behavior while
adapting to the target's architecture.

## Goal
The AgentService Tenable.io collector emits the same asset-inventory-centric data
the adapter does — a full asset inventory lane plus an all-severity findings lane,
co-located for one collection run — with no info-severity filter and no per-asset
enrichment, ACR taken from the bulk assets feed, and no regression to resumability.

## Context
- Reference/spec (read-only): adapter `Collectors/TenableIoCollector` + its
  `Documentation/` (README, 01-collection-strategy, 02-decision-making,
  03-current-concerns, 04-architecture, 05-testing). Committed `da393e5`.
- Target (modify): `/Users/user/Dev/AgentService/Source/CybiCollectors/TenableCollector/TenableIoCollector.cs`
  (~1144 lines), branch `tenable-to-assets-first`. Currently the OLD vulns-centric
  design (info filter on; only `/vulns/export`; per-asset enrichment lane;
  15-concurrent chunk engine + separate enrichment lane; batchful upload).
- The adapter design was validated against live data (104,141 assets +
  5,018,338 findings, zero duplicates, parser success in split mode).

## Constraints
See `constraints.md` (authoritative). Headline: mirror behavior not lines; add
`/assets/export` (last_assessed 30d, incl. no-vuln); drop info filter (all
severities, keep state=[OPEN,REOPENED]); remove `/assets/{id}` enrichment; ACR
from `ratings.acr.score`; co-locate both lanes for a combined run; stay "dumb"
(raw feeds, no in-process join); preserve resumability/batchful upload; build;
document. Modify only the AgentService TenableCollector.

## Success Criteria
- AgentService emits an assets lane (`/assets/export`, last_assessed 30d) carrying
  the full inventory incl. no-vuln hosts, plus a findings lane (`/vulns/export`,
  all severities, state=[OPEN,REOPENED]).
- No info-severity filter remains; no `/assets/{id}` enrichment remains.
- v3 ACR is sourced from `ratings.acr.score` on the assets feed (null tolerated).
- A combined run co-locates both lanes (assets-then-findings into one egress);
  an assets-only run emits assets only — matching the adapter's dispatch intent.
- Resumability / batchful upload is preserved (no regression); resume does not
  duplicate records (C1 intent mirrored where the engine has the exposure).
- The collector builds.
- An agent-service `Documentation/` (or equivalent) mirrors the adapter docs.

## Execution Rules
- The adapter + its `Documentation/` are the spec — confirm scope in one line; do
  not AskUserQuestion to re-derive settled design.
- Mirror behavior; adapt to the AgentService engine. No line-for-line copy.
- Resolve OPEN assumptions A1/A3/A4 against the actual AgentService codebase
  (engine, resume/checkpoint, egress/batchful upload, run-mode dispatch) before or
  during implementation; do not guess the engine's shape.
- Do not regress existing resumability or upload guarantees.
- Do not modify the adapter or parser repos.

## Output Format
- Code changes in the AgentService TenableCollector.
- Agent-service mirror documentation (equivalent to the adapter `Documentation/`).
- `execution_notes.md` updated (scope, files touched, decisions, residual risk),
  with explicit notes on how each adapter behavior mapped to the agent-service
  engine and how the C1 exposure was scoped/handled.

## Stop Conditions
- The agent-service engine cannot co-locate two lanes in one run without a
  structural change beyond this collector → stop, surface.
- Removing enrichment / adding the assets lane requires changes outside the
  TenableCollector (shared host contracts) that are not scoped → stop, surface.
- The C1 analogous exposure exists but cannot be mirrored without redesigning the
  engine → surface as accepted risk, do not silently ship a known dup path.
- Goal achieved and documented.

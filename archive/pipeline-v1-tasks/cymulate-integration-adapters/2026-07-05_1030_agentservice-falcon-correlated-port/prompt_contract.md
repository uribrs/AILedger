# Prompt Contract — AgentService Falcon correlated port

Role: senior .NET engineer porting a validated collection strategy between codebases.

Goal: AgentService's Falcon findings flow emits the same correlated record contract as the
platform collector v5.0.0, adapted to AgentService idioms, with no separate assets stage in
the findings flow.

Context: strategy fully settled and validated (see adapters repo
ai/active/2026-07-02_1705_falcon-correlated-findings-redesign/{decisions.md,constraints.md}).
The downstream parser (cymulate-integration-parsers feature/crowdstrike-correlated-findings)
auto-detects the correlated shape, so agent output parses once that deploys.

Constraints:
- Record contract identical: {aid, chunk, isLastChunk, findingsInChunk, host, findings[]};
  chunk cap 2000; zero-finding hosts emit empty findings; no host_info facet on Spotlight;
  strip apps + suppression_info per finding; sort remediation.entities canonically.
- Traversal identical: Discover hosts scroll (limit 1000, last_seen_timestamp.asc, filter =
  user FQL + last_seen>=baseDate, epoch fallback when empty) with next-page PREFETCH before
  join work; aid batches of 250; per batch one Spotlight scroll (limit 2500,
  updated_timestamp.asc, suppression filter + status:['open','reopen','closed'] +
  updated_timestamp>=baseDate + aid list); group per page by aid; flush chunks at cap.
- Cursor discipline: after-tokens expire 120s, never trusted across stalls. In-memory
  re-anchor on expiry (404 body "'after' key no longer valid"): Discover from whole-second
  last_seen watermark + boundary-aid dedupe; Spotlight per batch from updated_timestamp floor
  + seen-finding-id dedupe. Spotlight scroll terminates only on empty after. No persisted
  checkpoints (AgentService runs to completion in-process).
- Aid extraction for the batch keys keeps the existing ancestor logic (aid ?? device_id ??
  TryExtractAidFromCombinedHostId) — sensorless hosts without an AID-like id still emit a
  zero-finding envelope but are not sent to Spotlight.
- Findings flow no longer runs the separate assets stage (assets_*.json); correlated
  envelopes are the spine. Standalone CollectAssetsAsync unchanged.
- Batch upload: keep ICybiBatchUploader + findings_NNN.json naming, but flush by accumulated
  serialized bytes (~48MB target) instead of a 5000-record count — correlated records are
  orders of magnitude fatter than raw vulns; never buffer unbounded records.
- CollectionStats: TotalAssets = envelopes emitted; TotalVulnerabilities = findings emitted;
  AssetsWithoutVulnerabilities = zero-finding envelope count (now exact).
- Keep the ancestor's public API surface (IAsyncFindingsCollector etc.), token-refresh call
  path, dry-run semantics (single probe, no publication), logging style, and FQLParser usage.
- No new dependencies. Newtonsoft JSON idioms as in the existing files.

Success Criteria:
- FalconCollector project builds (and the AgentService solution if buildable locally).
- CollectFindingsAsync emits only correlated findings batches; record contract verified by
  reading emitted JSON in whatever test harness the repo has (add/adapt unit tests if a test
  project covers this collector; otherwise provide a reviewable manual-verification note).
- Traversal/re-anchor/chunking logic mirrors the adapters implementation observably
  (side-by-side reviewable).
- Stats fields populated per the mapping above.

Execution Rules: mirror the reference implementations, do not re-derive strategy; respect
ancestor idioms; do not touch other collectors in the repo.
Output Format: code changes on branch feature/falcon-correlated-findings in AgentService +
summary of files changed and how verified.
Stop Conditions: goal achieved; or an ancestor-infra constraint conflicts with the contract
(surface, don't work around).

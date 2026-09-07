# Prompt Contract: Qualys assets-first switch

## Role
You are a senior engineer working across two C# security-integration collectors and a PySpark parser, implementing a data-perspective change end-to-end.

## Goal
Switch Qualys collection from findings-perspective to assets-first so that EVERY host becomes an asset (including hosts with no active detections), while detections/exposures are left-joined onto their asset and remain bidirectionally correlated. Apply to both collectors (parity) and the SPARK parser.

## Context
- Loss occurs at 3 points, all must change:
  - (a) Detection API status filter (`New,Active,Re-Opened`) only returns hosts with matching detections.
  - (b) Collector parsers drop empty `DETECTION_LIST` (A: `QualysFindingsXmlParser.ParseDetectionHosts`; B: detection-parse path).
  - (c) SPARK `pre_process` `explode(DETECTION_LIST)` drops findings-less hosts and derives assets post-explode.
- Combined feed `{HOST_DETAILS, DETECTION_LIST}` → one parser → both assets + findings. Keep this; left-join, don't add an assets flow.
- Host list `?action=list&details=All&show_asset_id=1` is a superset of detection HOST_DETAILS — use as uniform asset spine.
- Collector A: `IFindingsCollectorAdapter`, System.Text.Json, has resume/checkpoint + 1960 concurrency handling.
- Collector B: `/Users/user/Dev/AgentService/Source/CybiCollectors/QualysCollector`, `IAsyncFindingsCollector`, Newtonsoft JObject, `Cymulate.Agent.*` infra, `AdaptiveConcurrencyLimiter`. Independent code — confirm before editing.
- Reference shapes (live fixtures): `IntegrationProbes` → `combined.ndjson`, `asset_with_exposures.json`, `exposureless_asset.json`.
- See constraints.md, assumptions.md, decisions.md in this directory.

## Constraints
See constraints.md (authoritative). Highlights: preserve combined feed; emit findings-less hosts with `DETECTION_LIST: []`; stream pages (bounded memory); SPARK decouple-not-`explode_outer`; preserve resume + rate-limit behavior; A/B parity; extra fields → existing `additional_fields`.

## Success Criteria
- Collector A emits every host as a `{HOST_DETAILS, DETECTION_LIST}` record; findings-less hosts present with empty `DETECTION_LIST`; vulnerable hosts unchanged in content.
- Collector B produces behaviorally equivalent output (same hosts emitted, same correlation), respecting its own infra/JSON lib.
- Both collectors stream the host list page-by-page; no full-run materialization of host details (bounded memory).
- SPARK parser: produces one asset per host (incl. empty-`DETECTION_LIST`), dedup preserved; produces findings only from real detections (no junk null-detection findings); extra host fields land in `additional_fields`.
- Bidirectional correlation intact: `asset.finding_ids[]` ↔ `exposure.id`; `exposure.asset_type/asset_value` join to `asset.type/value`.
- Asset identity `value` = NETBIOS else IP; identity-less hosts surfaced (logged/counted), not silently malformed.
- Parser output validated against the live probe fixtures.
- Existing tests pass in both collector repos; new tests cover the findings-less path in A and B; parser has coverage for the empty-`DETECTION_LIST` row.
- No regression to Qualys 1960 concurrency handling, retry/rate-limit, or resume/checkpoint.

## Execution Rules
- Do not assume missing data; verify each collector independently against current code.
- Respect constraints strictly; if a fix causes more errors than it resolves, revert and report.
- Resolve OPEN assumptions (Collector B downstream/parser parity; findings-less host field sparsity; resume interaction) via research or targeted inspection before committing the dependent change.
- Run tests at phase boundaries; do not poll-loop a hung `dotnet test` (stop and move to verifiers).

## Output Format
- Code changes in the three targets.
- `execution_notes.md` appended with per-target changes, decisions, and test results.
- `state.json` updated as steps/assumptions/verification change.

## Stop Conditions
- An OPEN assumption that would change the implementation cannot be resolved by inspection/research → surface to user.
- A change would break resume, rate-limit, or A/B parity and cannot be reconciled.
- Collector B's downstream turns out NOT to be the same combined-feed parser (would change B's output contract).

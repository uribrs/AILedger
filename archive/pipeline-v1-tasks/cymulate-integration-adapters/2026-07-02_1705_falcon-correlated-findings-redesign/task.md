# Falcon Correlated Findings Redesign — Production Implementation

Productionize the validated correlated-findings model for the Falcon collector, fix the parser to consume it, and add an egress page-hash observability aid.

## Workstreams

- **W1 — Collector flow rewrite** (`src/Cymulate.Integration.Adapters/Collectors/FalconCollector`):
  asset-driven traversal — Discover hosts page (last_seen watermark axis, `sort=last_seen_timestamp.asc`) → aid batches → aid-scoped Spotlight scroll (`sort=updated_timestamp.asc`, one pass, `status:['open','reopen','closed']`, not-suppressed, updated>=baseDate) → per-page group-by-aid → chunked self-complete records `{aid, chunk, isLastChunk, findingsInChunk, host, findings[]}`, findings-per-record cap ~2000, zero-finding hosts emit empty `findings`. No `host_info` facet. Strip `apps` + `suppression_info` per finding. Sort `remediation.entities` at emission. Publish on the findings lane only (`findings_*.json`).
- **W2 — Machinery retirement + checkpoint reshape**: remove status lanes (`FalconSpotlightLaneRunner`/`StageSequencer`/`LaneState`), month segments, status-stage yields; keep the AID filter machinery only where user-FQL scoping needs it. New checkpoint: assets `last_seen` watermark + pending-aid batch position (+ per-batch `updated_timestamp` floor). Cursor expiry (both scrolls) → watermark re-anchor through existing Resilience layer. Checkpoint version bump; major collector version bump.
- **W3 — Parser** (`/Users/user/Dev/cymulate-integration-parsers`): auto-detect input shape (legacy split pair vs correlated records) and support both; hydrated path maps `host` → asset row, `findings` → `vulnerabilities`, asset emits once per aid (chunk==0), findings from all chunks; retire the split-mode dual-lane load + Spark correlate + schema projection for the correlated shape (keep for legacy shape until it retires).
- **W4 — Shared Egress hash** (`Shared/.../DataPipeline/Egress`): streaming content hash per published logical object, emitted in the existing publish-completion log line, all collectors.
- **W5 — Docs/skills sync**: FalconDocs/CollectorDocs, Collectors/README.md, ai/skills content describing lanes/segments.
- **W6 — End-to-end verification**: LocalAdapterRunner collection on the lab tenant → fixed parser ingests it; comparison methodology from the prototype phase re-usable.

## Reference implementation

Validated prototype: `/Users/user/Dev/Uri/localprojects/IntegrationProbes/Integrations/Falcon/FalconCorrelatedFindingsProbe.cs` (treat as spec for traversal, chunking, re-anchor semantics).

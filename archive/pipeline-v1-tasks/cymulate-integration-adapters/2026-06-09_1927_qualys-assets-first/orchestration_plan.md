# Orchestration Plan

## Complexity Decision
- Path: decompose
- Rationale: Three independent code targets in three repos (Collector A adapters, Collector B agent-service, SPARK parser) with disjoint file sets → safe parallelism. A and B must end behaviorally consistent (synthesis/parity check in main thread). User explicitly opted into multi-agent. Collector B body unread → a discovery phase must precede implementation so workers get a precise spec.

## Research Decisions
- External (vendor/docs) research: NOT needed. The risky external-API facts (host-list `details=All&show_asset_id=1` field superset; FQDN nested at DNS_DATA.FQDN; pagination) were already validated LIVE this session via the IntegrationProbes harness against a real tenant. Fixtures are the source of truth.
- Remaining OPEN assumptions are codebase-INTERNAL (B's downstream/parser parity; B's resume + AdaptiveConcurrencyLimiter interaction with page-streaming; field sparsity of real findings-less hosts) → resolved by read-only repo exploration (Phase 0), not technical-researcher.
- One assumption is empirical-only (findings-less host identity on a real tenant) and cannot be resolved without a populated tenant → handled by defensive design (surface/count identity-less hosts; do not emit malformed assets).

## Worker Plan

### Phase 0 — Discovery (parallel, read-only Explore agents)
- E1 — scope: map Collector A exact change surface. inputs: adapters QualysCollector dir + tests + Shared egress/recovery touchpoints. output: change-surface map (files, methods, page-streaming insertion point, parser drop-guard location, checkpoint impact, test files). dependencies: none.
- E2 — scope: map Collector B exact change surface in the agent-service single-file collector + how it parses/enriches/stores/publishes, resume support, AdaptiveConcurrencyLimiter, and (critically) its DOWNSTREAM output shape/consumer. output: change-surface map + answer to "same combined-feed/parser as A?". dependencies: none.

### Phase 1 — Implementation (parallel; disjoint repos)
- W1 — scope: Collector A assets-first (stream host-list pages, left-join detections, keep findings-less hosts, remove empty-DETECTION_LIST drop, surface identity-less hosts) + tests. inputs: E1 map, contract, fixtures. output: code + tests + execution_notes. dependencies: E1.
- W2 — scope: Collector B same behavior, parity with A, respecting its infra/JSON lib/resume/concurrency. inputs: E2 map, contract, fixtures, W1 behavior. output: code + tests. dependencies: E2 (and W1 behavior reference for parity).
- W3 — scope: SPARK parser — decouple asset extraction from explode (assets pre-explode + dedup; findings inner-explode); emit asset for empty DETECTION_LIST; no junk findings; FQDN from DNS_DATA.FQDN with DNS fallback; extra fields → additional_fields. inputs: parser file, fixtures. output: code + tests. dependencies: none (parser is downstream-independent; validate against fixtures).

## Synthesis Approach
Main thread reassembles: (1) confirm A and B emit the same hosts + same correlation shape (parity matrix); (2) confirm parser consumes both collectors' output identically (or flag divergence from E2); (3) reconcile naming/field differences. Resolve contradictions before verification.

## Verification Obligations
- Cross-check every Success Criterion in prompt_contract.md.
- Findings-less host → insertable asset (value=NETBIOS|IP) + empty exposures, both collectors.
- Bidirectional correlation intact (asset.finding_ids[] ↔ exposure.id; exposure.asset_type/value join).
- Bounded memory: page-streaming, no full-run materialization, in both collectors.
- No regression: 1960 concurrency, retry/rate-limit, resume/checkpoint.
- Parser: asset for empty DETECTION_LIST; zero junk findings; validated against probe fixtures.
- Existing tests pass; new findings-less coverage added.

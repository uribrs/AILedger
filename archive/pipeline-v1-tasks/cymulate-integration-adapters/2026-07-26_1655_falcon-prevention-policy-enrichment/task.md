# Task — Falcon Prevention Policy Enrichment

Implement Prevention Policy enrichment inside every Discover host published by
FalconCollector's `CollectAssets` and correlated `CollectFindings` flows.

## Authoritative documents

Copies live in this task directory under `source_docs/`:

- `source_docs/FALCON_PREVENTION_POLICY_IMPLEMENTATION_PLAN.md` — authoritative
  design.
- `source_docs/FALCON_PREVENTION_POLICY_IMPLEMENTATION_PROMPT.md` — consolidated
  implementation prompt.
- `source_docs/FALCON_POLICY_COLLECTION_PLAN.md` — superseded broad research.
  Must not define delivery scope.

## Deliverables

- Prevention assignment and definition clients plus enricher under
  `FalconCollector/Flows/Policies/`.
- One shared enrichment component invoked by both flows at their existing
  bounded units — not a standalone flow, not duplicated per-flow logic.
- Policy envelope inside Assets records and inside the Findings record's `host`.
- Default-enabled configuration switch and extended staged access probing.
- Asymmetric assignment/definition failure semantics.
- Run-scoped definition cache.
- Contract, flow, failure, cache, and regression tests.
- Targeted build/test evidence and final collector review.
- Updated task state and touched documentation.

## Success

The implementation satisfies every acceptance criterion in the authoritative
plan without adding another policy family, a standalone flow, or a production
finding-shape change beyond the enriched `host`.

# Assumptions

- **VALIDATED — data parity.** Correlated traversal reproduces the production collector byte-for-byte on identical scope: 334==334 assets, 59,040==59,040 findings, 0 set/count/per-envelope diffs; findings identical after sorting `remediation.entities` (all 4,864 raw diffs were array order); assets differ only in 13 live observation fields. (Lab tenant us-2, base 2026-04-01, runs 6 min apart.)
- **VALIDATED — parser never reads `host_info`/`apps`/`suppression_info`.** It builds schema projections specifically to skip them (`CrowdstrikeAssetsFindingsNotHydrated._HEAVY_FINDING_FIELDS`).
- **VALIDATED — parser drops orphan findings today.** LEFT join on the asset spine (`correlation.py` has no RIGHT/FULL); window-boundary orphans never reach the platform, so asset-window-scoped fetching is behavior-preserving end-to-end.
- **VALIDATED — Discover host record supersedes `host_info`** field-for-field on live data; sole loss is host-group names (IDs remain). Cloud triplet verified on a real AWS host.
- **VALIDATED — FQL bracket lists** for `aid` (250 entries, repo-proven) and `status` work against the live API (multiple clean runs).
- **VALIDATED — vendor cursor/token lifetimes** (120s after-token both endpoints, ~30min OAuth) and the three long-haul failure modes (slow join kills prefetched cursor; hung socket 29min kills mid-scroll cursor; token expiry) — all observed live and recovered via watermark re-anchor + refresh.
- **OPEN — aid-list length beyond 250** is unverified (URL-length bound, undocumented). Not needed for the design; do not raise without testing.
- **OPEN — customer-scale traversal cost.** Lab tenant has 23 finding-bearing hosts of 334; a dense tenant (all hosts bearing) raises Spotlight page counts (~16 pages per 250-aid batch at 2500/page) — request math is fine (100 req/s), but join-work-per-Discover-page will exceed 120s routinely, so re-anchor is a hot path, not an edge case. Verify re-anchor behavior under sustained use in tests.
- **OPEN — FalconRecoverySimulation scenarios** (`cursor-expired-fallback`, `month-boundary-after`, etc.) are coupled to the retiring lane/segment model; scope of rework unknown until inspected.
- **REJECTED — remediation entity ids are query-unstable.** Disproved: ids identical across runs; only array order differs. (Sorting at emission still adopted — fixes parser `entities[0]` nondeterminism.)

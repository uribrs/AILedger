# Assumptions

- **A1 — Vuln export chunks are asset-complete.** Status: VALIDATED. Official docs (`num_assets`: "The exported data of a chunk is the sum of all the vulnerabilities for each asset in that chunk"; job-handler allocate-then-populate model, developer.tenable.com) + empirical probe on the largest tenant: 150 chunks / 7,486 assets / 526,519 findings, ZERO assets in >1 chunk, assets/chunk min 48 max 50. Design still tolerates violation via the miss lane (a straddle second-appearance emits thin-host records).
- **A2 — Spool sizing.** Status: VALIDATED. 5,000-asset probe on the largest tenant: avg 4,025B raw/asset, p50 2,952B, p95 6.9KB, max 59KB; per-record gzip 2.9×; 108K-asset projection 435MB raw / 152MB compressed. Fat fields: network_interfaces 20.6%, tags 17.8%, installed_software 6.4%.
- **A3 — Chunk sizes at num_assets=50.** Status: VALIDATED. Measured 10–43MB, p50 22MB per chunk; densest asset 842 findings.
- **A4 — Chunks stream during PROCESSING.** Status: VALIDATED. Official docs + existing collector already downloads progressively.
- **A5 — Export concurrency.** Status: VALIDATED. 10 concurrent exports/container; 429 + retry-after on excess; duplicate-filter exports deduped server-side (409 active_job_id path already handled).
- **A6 — Chunk retention.** Status: VALIDATED (conservative). Docs state 24h in one place, 3 days in another; collector's existing ~24h staleness rule is the conservative bound — keep.
- **A7 — Phantom allocations are benign.** Status: VALIDATED. 14/7,500 slots (~0.2%) allocated assets yielded zero rows (under-50 chunks scattered across size distribution, never >50/chunk ⇒ not size cutoff). Absence from vuln export == zero-vuln case; handled by sweep.
- **A8 — Reference tenant scale.** Status: VALIDATED. 90-day window: 2,168 chunks ≈ 108K assets, ~7.6M findings projected.
- **A9 — Parser-side per-aid aggregation absorbs duplicate/thin+hydrated chunk-0 records.** Status: VALIDATED by design precedent (Falcon correlated parser replay-idempotence); the TenableIo correlated parser (later phase) must preserve this property.
- **A10 — Assets-export re-creation on resume with same filters is safe.** Status: OPEN (internal). New snapshot deltas land in the miss lane / extra empty envelopes; formally absorbed only once the later-phase parser is in place. Accept for collector-side task; document in output notes.
- **A11 — LocalAdapterRunner needs no structural changes to run the new flow.** Status: OPEN (internal). Verify during execution; adjust runner wiring only if the flow signature changes.

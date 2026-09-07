# Assumptions

- **A1 — Split-model feed shape.** — STATUS: **VALIDATED** (research/split-model-and-legacy.md). Split = **DUAL_MODE separate-file**: `preparation.py:16-20` + `input_resolver.py:27` auto-detect `assets*.json` + `findings*.json` (else hydrated single file). Tenable key `tenable-assets-findings` must be registered in DUAL_MODE. Mirror Defender VM/Cortex (assets from own lane + LEFT-join findings); NOT Qualys (single-file).
- **A2 — Batchful upload mechanism.** — STATUS: **VALIDATED**. Legacy = 1000-row `findings_{NNN}.json` batches; the adapter's `CollectorNdjsonPublisher` already does bounded page-batching with `assets`/`findings` output names. Mirror the concept; no new mechanism needed.
- **A3 — ~3× findings volume tolerance.** — STATUS: OPEN (mitigated). Egress already batches; remaining concern is Spark/postgres throughput on ~6.8M. Sanity-check at verify; not a blocker for the collector/parser code.
- **A4 — No-vuln asset survival (SPARK-explode trap).** — STATUS: **VALIDATED (design)**. DUAL_MODE loads assets from their own lane and LEFT-joins findings (Defender VM pattern), so findings-less assets survive and no phantom findings are created. Must still verify end-to-end against prototype feeds.
- **A5 — Resumability parity.** — STATUS: **VALIDATED**. `/assets/export` is the same create→poll→chunk export model; mirror `TenableIoFindingsCheckpointState` with a sibling assets checkpoint.

## Cycle 2
- **A6 — Two separate flows produce co-located lanes.** — STATUS: **REJECTED**. CollectAssets + CollectFindings as separate platform invocations write separate batch dirs; the split parser never sees both lanes together and input_resolver throws on assets-only. Superseded by the one-run-two-lanes façade (decisions.md, Cycle 2).
- **A7 — CollectAssets-only routing.** A standalone CollectAssets batch (assets lane only) is unsafe for the split findings parser (input_resolver hard-throws on an assets-only lane); safe only if CollectAssets feeds an asset-inventory consumer. — STATUS: OPEN (non-blocking, platform-routing fact to confirm).

## Validated (carried from this session's prototype — see decisions.md)
- v3 ACR bulk availability via `ratings.acr.score` — VALIDATED (probe: 994/1000).
- Field presence across both feeds (no missing data, only name deltas) — VALIDATED (probe field audit).
- Asset population ≈ ~102K via `last_assessed` 30d — VALIDATED (collector measurement).

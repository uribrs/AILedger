# Task: TenableIo asset-centric split-model rework

Convert the TenableIo collector from vulns-centric to **asset-inventory-centric**, emitting two independent "dumb" feeds, and align the Tenable parser to the **split data model** so those feeds parse correctly.

## Scope (three repos)
- **Legacy reference (read-only):** `/Users/user/Dev/AgentService/Source/CybiCollectors/TenableCollector` — prior implementation; supports **batchful uploads**. Reference for asset+finding shaping and the batchful upload mechanism.
- **Target collector (.NET 8, modify):** `/Users/user/Dev/Uri/cymulate-integration-adapters/src/Cymulate.Integration.Adapters/Collectors/TenableIoCollector`.
- **Parser (Python/PySpark, align):** `/Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/tenable/tenableAssetsAndFindings.py` and `.../parsers/preparation.py`.

## What changes
- Collector emits **two feeds** with no in-process join/enrichment:
  - **assets** — `POST /assets/export` (`filters.last_assessed`, 30d) → full inventory **including no-vuln assets**.
  - **findings** — `POST /vulns/export` (`filters.state=[OPEN,REOPENED]`, **all severities incl. info**).
- **Remove** the legacy info-severity filter.
- **Drop** the per-asset `/assets/{id}` enrichment hunt — v3 ACR is bulk-available on the assets feed (`ratings.acr.score`).
- Parser aligned to the split model: read both feeds, correlate `findings.asset.uuid == assets.id`, map ACR from `ratings.acr.score`, handle asset field-name deltas.

## Out of scope
- Reproducing the Tenable UI "Hosts / Last Seen 30d" count exactly (impossible via export; `last_assessed` ≈ 99.2% is accepted).
- Entity-creation stage (not ours).

## Cycle 2 (current) — corrected wiring
Cycle 1 built two separate flows (separate batches) — wrong for the split parser, which needs both lanes co-located. Cycle 2 re-wires the collector so a single `CollectFindings` run emits both `assets_*.json` + `findings_*.json` into one batch (assets flow then findings flow, shared egress), with a phase-aware checkpoint (Falcon-style). Endpoints/filters/parser unchanged. See prompt_contract.md "Cycle 2 Addendum".

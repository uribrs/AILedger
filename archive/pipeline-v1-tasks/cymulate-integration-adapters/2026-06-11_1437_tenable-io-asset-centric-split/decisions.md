# Decisions (validated this session via the IntegrationProbes prototype on the cf4e tenant)

- Asset-centric = **two independent dumb feeds**, not an in-collector join. — mirrors the Qualys assets-perspective precedent.
- Assets feed = `POST /assets/export`, `filters.last_assessed` (30d). — only path that returns no-vuln assets.
- Findings feed = `POST /vulns/export`, `filters.state=[OPEN,REOPENED]`, severity all 5 incl info. — FIXED skip allowed; info removal recovers ~20K assets.
- **v3 ACR is bulk-available** on the assets feed via `ratings.acr.score` (measured 994/1000) and `acr_score` (float-string). — eliminates the per-asset `/assets/{id}` hunt (~11h tail).
- `acr_score_v3` field name is `/assets/{id}`-only and **absent from the export**. — parser must read `ratings.acr.score` instead.
- Correlation key = `findings.asset.uuid == assets.id`. — vulns embeds asset as `uuid`; assets export uses `id`.
- Asset field-name deltas (assets-export → parser `asset.*`): `id`→`uuid`, `ipv4s[0]`→`ipv4`, `fqdns[0]`→`fqdn`, `netbios_names[0]`→`netbios_name`, `operating_systems`→`operating_system` (both arrays), `tags{uuid,key,value}`→`tags{tag_uuid,tag_key,tag_value}`. `first_seen`/`last_seen` exact.
- Finding dimension maps **1:1** to today (`severity_id`, `state`, `first_found`, `last_found`, `plugin.name/solution/cve/description`). — no finding-path changes.
- Parser `risk_score` ← `ratings.acr.score` (currently `asset.acr_score_v3` → null). — satisfies CA-72614; FR3 permits null.
- Cannot reproduce UI 102,617 exactly; `last_assessed` (~101,810, 99.2%) is the accepted proxy. — export has no `type=Host`/`last_seen` filter; workbench API (capped 5,000) is the only exact source.
- Parser is already null-safe on the ACR additional fields. — missing ACR fields won't break parsing.

## Cycle 2 — corrected wiring (supersedes the two-flow shape)
- The split parser needs BOTH lanes (assets*.json + findings*.json) co-located in ONE batch; input_resolver hard-throws on an assets-only lane. — separate-invocation feeds are wrong.
- Thin façade by run topic: CollectAssets → assets flow only; CollectFindings → assets flow THEN findings flow, both publishing to ONE shared AdapterProgressContext/egress so both lanes co-locate in one batch. — this is the fix for cycle-1's separate-batch miss.
- Phase-aware checkpoint spans the combined CollectFindings run (mirror FalconCollector Recovery/: per-flow checkpoint writers + phase keyed off FlowName); resume continues the correct phase and never re-pulls the completed assets export.
- Keep TenableIoAssetsFlow + TenableIoFindingsFlow SEPARATE; no shared-engine extraction (accepted duplication, lower risk given the delay).
- Endpoints/filters/enrichment-removal from cycle 1 are validated and preserved unchanged; parser split-mode alignment stays as committed.

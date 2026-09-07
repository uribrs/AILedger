# Integration Catalog Permutations

Sources:
- **SEED**: `/Users/user/Dev/dbMigrations/Application/data/integrations/integrationsData.js` — BAS/indicators catalog (60 entries). Schema: `name`, `category`, `type` (internal key). No `vendor` field, no `flows` array.
- **LIVE**: `/Users/user/Dev/Uri/ClientLogs/2026-06-22-dlqued-collector/cymulate.cybiIntegrationSetting.csv` — Cybi/Exposure Analytics collector catalog (17 data rows). Schema: `_id`, `name`, `vendor`, `category`, `flows[N].id`, `flows[N].name`, `flows[N].type`, `integrationCodeId`.

---

## Master Name Table

All distinct `name` values across both sources.

| name | company vendor | category | flow names / types | source |
|------|---------------|----------|--------------------|--------|
| Active Directory | Microsoft | Identity Management | Active Directory Assets (CollectAssets), Authentication Validation (Connection) | live |
| AWS Guard Duty | — | SIEM | — (indicators only) | seed |
| BlackBerry Cylance OPTICS | — | EDR | — (indicators only) | seed |
| BlackBerry Cylance PROTECT | — | EDR | — (indicators only) | seed |
| Carbon Black | — | EDR | — (indicators only) | seed |
| Check Point Harmony Endpoint | — | EDR | — (indicators only) | seed |
| Cisco Secure Endpoint | — | EDR | — (indicators only) | seed |
| CloudGuard | Palo Alto Networks *(data error — Check Point product)* | CloudGuard | CloudGuard Assets and Findings (CollectFindings), Authentication Validation (Connection) | live |
| Cortex XDR | Palo Alto Networks | Endpoint Detection and Response | Cortex Assets (CollectAssets), Cortex Assets and Findings (CollectFindings), Authentication Validation (Connection) | live |
| Crowdstrike Falcon | Crowdstrike | Endpoint Detection and Response | Assets (CollectAssets), Spotlight Vulnerability Management (CollectFindings), Authentication Validation (Connection) | live |
| CrowdStrike Falcon | — | EDR | — (indicators only) | seed |
| Cymulate API | — | cymulate | — (indicators only) | seed |
| Cybereason | — | EDR | — (indicators only) | seed |
| Cynet | — | EDR | — (indicators only) | seed |
| Defender Endpoint | Microsoft | Endpoint Detection and Response | Defender Endpoint Assets and Findings (CollectFindings), Authentication Validation (Connection) | live |
| Defender VM | Microsoft | Vulnerability Management | Defender VM Assets (CollectAssets), Defender VM Assets and Findings (CollectFindings), Authentication Validation (Connection) | live |
| Devo | — | SIEM | — (indicators only) | seed |
| Elastic SIEM | — | SIEM | — (indicators only) | seed |
| Exabeam SIEM | — | SIEM | — (indicators only) | seed |
| Falcon LogScale (previously known as Humio) | — | SIEM | — (indicators only) | seed |
| Falcon NGSIEM | — | SIEM | — (indicators only) | seed |
| Fortinet FortiEDR | — | EDR | — (indicators only) | seed |
| Google Chronicle | — | SIEM | — (indicators only) | seed |
| Google Security Operations | — | SIEM | — (indicators only) | seed |
| Guardicore | Akamai | Network Security | Guardicore Assets (CollectAssets), Authentication Validation (Connection) | live |
| IBM QRadar | — | SIEM | — (indicators only) | seed |
| IBM Qradar SOAR | — | SOAR | — (indicators only) | seed |
| Import from CSV | Upload file and configure | CSV | CSV Assets (CollectAssets), CSV Exposures (CollectFindings) | live |
| InsightIDR | — | SIEM | — (indicators only) | seed |
| InsightVM | Rapid7 | Vulnerability Management | InsightVM Assets and Findings (CollectFindings), Authentication Validation (Connection) | **both** |
| InsightVM Cloud | Rapid7 | Vulnerability Management | InsightVM Assets and Findings (CollectFindings), Authentication Validation (Connection) | live |
| Jira | — | Ticketing System | — (indicators only) | seed |
| JIRA | — | Ticketing System | — (indicators only) | seed |
| Kaspersky EDR | — | EDR | — (indicators only) | seed |
| LogRhythm | — | SIEM | — (indicators only) | seed |
| Mcafee ESM | — | SIEM | — (indicators only) | seed |
| MicroFocus ArcSight | — | SIEM | — (indicators only) | seed |
| Microsoft Azure Sentinel | — | SIEM | — (indicators only) | seed |
| Microsoft Defender for Cloud | — | CDR | — (indicators only) | seed |
| Microsoft Defender for Endpoint | — | EDR | — (indicators only) | seed |
| Microsoft Defender for Office 365 | — | Email Gateway | — (indicators only) | seed |
| Microsoft Defender TVM | — | Vulnerability Management | — (indicators only) | seed |
| Microsoft Entra ID | Microsoft | SIEM | Entra ID Assets (CollectAssets), Authentication Validation (Connection) | live |
| Netskope | — | Web Gateway | — (indicators only) | seed |
| Nessus | Tenable | Vulnerability Management | Nessus Assets and Findings (CollectFindings), Authentication Validation (Connection) | live |
| oauth2 | — | oauth2 | — (indicators only) | seed |
| Palo Alto Cortex XDR | — | EDR | — (indicators only) | seed |
| Palo Alto Cortex XSIAM | — | SIEM | — (indicators only) | seed |
| Palo Alto Networks Cortex XSOAR | — | SOAR | — (indicators only) | seed |
| Palo Alto Networks Next Generation Firewall | — | firewall | — (indicators only) | seed |
| PantherSiem | — | SIEM | — (indicators only) | seed |
| Qualys | Qualys | Vulnerability Management | Qualys Assets and Findings (CollectFindings), Authentication Validation (Connection) | live |
| Qualys VM | — | Vulnerability Management | — (indicators only) | seed |
| RSA Netwitness | — | SIEM | — (indicators only) | seed |
| Secureworks Taegis | — | EDR | — (indicators only) | seed |
| Securonix Snypr | — | SIEM | — (indicators only) | seed |
| SentinelOne | — | EDR | — (indicators only) | seed |
| SentinelOne Singularity Data Lake | — | SIEM | — (indicators only) | seed |
| SentinelOne Singularity XDR | SentinelOne | Threat Detection and Response | SentinelOne Assets (CollectAssets), Authentication Validation (Connection) | live |
| Service Now | — | Ticketing System | — (indicators only; appears twice with types `service_now` and `service_nowCloud`) | seed |
| Service Now Cmdb | ServiceNow | CMDB | ServiceNow CMDB Inventory Assets (CollectAssets), Authentication Validation (Connection) | live |
| Snowflake | — | SIEM | — (indicators only) | seed |
| Sophos | — | EDR | — (indicators only) | seed |
| Splunk | — | SIEM | — (indicators only) | seed |
| Sumo Logic Siem | — | SIEM | — (indicators only) | seed |
| Symantec | — | EDR | — (indicators only) | seed |
| Tanium | — | EDR | — (indicators only) | seed |
| Tenable.io | Tenable | Vulnerability Management | Tenable.io Assets and Findings (CollectFindings), Authentication Validation (Connection) | **both** |
| Tenable.sc | TenableSc *(vendor field value)* | Vulnerability Management | Tenable.sc Assets and Findings (CollectFindings), Authentication Validation (Connection) | **both** |
| Trellix | — | EDR | — (indicators only) | seed |
| Trellix HX | — | EDR | — (indicators only) | seed |
| Trend Micro Vision One | — | EDR | — (indicators only) | seed |
| Wiz | — | CDR | — (indicators only) | seed |

**Total distinct names: ~70** (60 seed + 17 live, ~7 overlap).

---

## Topic / Category Derivation

### How a flow maps to collector vs indicator vs validation

The LIVE CSV is the only source with explicit `flows` arrays. Each flow has a `type` field that is the canonical routing signal:

| flow.type | meaning | bus topic equivalent |
|-----------|---------|----------------------|
| `CollectAssets` | Collector: pull host/asset inventory from vendor | `CollectAssets` |
| `CollectFindings` | Collector: pull vulnerabilities/findings from vendor (may also co-emit assets) | `CollectFindings` |
| `Connection` | Authentication/connectivity validation — not a data flow | *(not routed as a data topic)* |

The SEED file has no `flows` array at all. The `type` field in the seed is an **internal type key** (e.g. `crowdStrikeFalcon`, `nexpose`) used for indicator routing, not a flow type. Category gives a coarse hint (`EDR`, `SIEM`, `Vulnerability Management`) but does not distinguish CollectAssets vs CollectFindings.

**Examples from LIVE:**

- `Crowdstrike Falcon` → flows: `CollectAssets` ("Assets") + `CollectFindings` ("Spotlight Vulnerability Management") + `Connection` — both collector topics active.
- `Qualys` → flows: `CollectFindings` ("Qualys Assets and Findings") + `Connection` — only findings (assets are co-emitted within the findings flow, as per architecture).
- `SentinelOne Singularity XDR` → flows: `CollectAssets` only + `Connection` — no vulnerability findings.
- `Defender VM` → flows: `CollectAssets` + `CollectFindings` + `Connection` — both.
- `InsightVM Cloud` → flows: `CollectFindings` ("InsightVM Assets and Findings") + `Connection` — single combined flow.

**Derivation rule**: The bus `vendor` field = the catalog `name` field. The topic being dispatched = the `flow.type` of the triggered flow (`CollectAssets` or `CollectFindings`). The `Connection` flow is a validation ping, not a collector dispatch.

---

## Seed vs Live Discrepancies

### Names present in LIVE but NOT in SEED (collector-only entries, no BAS indicator counterpart)

| name | note |
|------|------|
| Active Directory | no seed entry |
| CloudGuard | no seed entry |
| Cortex XDR | seed has "Palo Alto Cortex XDR" — different name |
| Crowdstrike Falcon | seed has "CrowdStrike Falcon" — capitalization differs |
| Defender Endpoint | seed has "Microsoft Defender for Endpoint" — different name |
| Defender VM | seed has "Microsoft Defender TVM" — different name |
| Guardicore | no seed entry |
| Import from CSV | no seed entry |
| InsightVM Cloud | no seed entry — **seed only has "InsightVM" (on-prem)**  |
| Microsoft Entra ID | no seed entry |
| Nessus | no seed entry |
| Service Now Cmdb | seed has "Service Now" — different name |
| SentinelOne Singularity XDR | seed has "SentinelOne" and "SentinelOne Singularity Data Lake" — different names |
| Tenable.sc | seed has "Tenable.sc" — **match** (both present) |

### Names present in SEED but NOT in LIVE (indicator-only entries, no collector)

The majority of seed entries (50+ of 60) have no live counterpart — all the pure SIEM/EDR/SOAR/ticketing indicators: Splunk, IBM QRadar, Microsoft Azure Sentinel, Carbon Black, CrowdStrike Falcon (indicator), SentinelOne (indicator), Palo Alto Cortex XDR (indicator), etc.

### Names present in BOTH sources

| seed name | live name | match quality |
|-----------|-----------|---------------|
| InsightVM | InsightVM | exact |
| Tenable.io | Tenable.io | exact |
| Tenable.sc | Tenable.sc | exact |
| Qualys VM *(seed)* | Qualys *(live)* | **name mismatch** — seed appends "VM" |
| CrowdStrike Falcon *(seed)* | Crowdstrike Falcon *(live)* | **capitalization mismatch** — "CrowdStrike" vs "Crowdstrike" |
| Microsoft Defender TVM *(seed)* | Defender VM *(live)* | **name mismatch** — different token entirely |
| Microsoft Defender for Endpoint *(seed)* | Defender Endpoint *(live)* | **name mismatch** |
| Palo Alto Cortex XDR *(seed)* | Cortex XDR *(live)* | **name mismatch** |
| SentinelOne *(seed)* | SentinelOne Singularity XDR *(live)* | **name mismatch** |
| Service Now *(seed)* | Service Now Cmdb *(live)* | **name mismatch** |

> Note: The seed and live catalogs serve different products (BAS vs Exposure Analytics). Name mismatches are expected — they are NOT the same integration object. The bus `vendor` field will carry the live CSV `name`, not the seed `type`.

---

## Vulnerability / Collector Names of Interest

How each VM/collector product appears as a `name` value across sources:

| product | seed name | live name | live flows |
|---------|-----------|-----------|------------|
| InsightVM (on-prem) | `InsightVM` | `InsightVM` | CollectFindings ("InsightVM Assets and Findings"), Connection |
| InsightVM Cloud | *(absent)* | `InsightVM Cloud` | CollectFindings ("InsightVM Assets and Findings"), Connection |
| Qualys | `Qualys VM` | `Qualys` | CollectFindings ("Qualys Assets and Findings"), Connection |
| Tenable.io | `Tenable.io` | `Tenable.io` | CollectFindings ("Tenable.io Assets and Findings"), Connection |
| Tenable.sc | `Tenable.sc` | `Tenable.sc` | CollectFindings ("Tenable.sc Assets and Findings"), Connection |
| Defender VM | `Microsoft Defender TVM` | `Defender VM` | CollectAssets + CollectFindings ("Defender VM Assets and Findings"), Connection |
| Defender Endpoint | `Microsoft Defender for Endpoint` | `Defender Endpoint` | CollectFindings ("Defender Endpoint Assets and Findings"), Connection |
| SentinelOne (collector) | *(absent as collector)* | `SentinelOne Singularity XDR` | CollectAssets only, Connection |
| SentinelOne (indicator) | `SentinelOne` | *(absent)* | indicator routing only |
| Cortex XDR (collector) | *(absent as collector)* | `Cortex XDR` | CollectAssets + CollectFindings, Connection |
| Cortex XDR (indicator) | `Palo Alto Cortex XDR` | *(absent)* | indicator routing only |
| CrowdStrike Falcon (collector) | *(absent as collector)* | `Crowdstrike Falcon` | CollectAssets + CollectFindings, Connection |
| CrowdStrike Falcon (indicator) | `CrowdStrike Falcon` | *(absent)* | indicator routing only |
| Cisco Secure Endpoint | `Cisco Secure Endpoint` | *(absent)* | indicator only — no collector |
| Cisco Umbrella | *(absent)* | *(absent)* | not present in either source |
| Nessus | *(absent)* | `Nessus` | CollectFindings ("Nessus Assets and Findings"), Connection |

Key observations:
- "InsightVM Cloud" is **live-only** — no seed entry. Its flow name ("InsightVM Assets and Findings") is identical to the on-prem InsightVM flow name. The only distinguishing token is the catalog `name` field itself.
- "Qualys VM" (seed) vs "Qualys" (live) — the bus routing token is `Qualys`, not `Qualys VM`.
- SentinelOne splits into two unrelated catalog entries: `SentinelOne` (BAS indicator) and `SentinelOne Singularity XDR` (collector). Same vendor, completely different `name` tokens.
- CrowdStrike Falcon splits similarly: `CrowdStrike Falcon` (indicator, seed) vs `Crowdstrike Falcon` (collector, live) — capitalization differs.

---

## Names That May Not Map Cleanly to a Known PlatformType

| name | concern |
|------|---------|
| `Crowdstrike Falcon` (live) vs `CrowdStrike Falcon` (seed) | Capitalization divergence — if PlatformType uses the seed value, case-sensitive matching will miss collector events. |
| `Qualys` (live) vs `Qualys VM` (seed) | Token mismatch — adapter keyed on "Qualys VM" would not match bus messages carrying "Qualys". |
| `Defender VM` (live) vs `Microsoft Defender TVM` (seed) | Completely different tokens for the same product. |
| `Defender Endpoint` (live) vs `Microsoft Defender for Endpoint` (seed) | Same issue. |
| `Cortex XDR` (live) vs `Palo Alto Cortex XDR` (seed) | Prefix dropped in live catalog. |
| `SentinelOne Singularity XDR` (live) vs `SentinelOne` (seed) | No shared token. |
| `CloudGuard` | vendor field says "Palo Alto Networks" but CloudGuard is a Check Point product — likely a data entry error in the live CSV. |
| `oauth2` (seed) | Internal/infrastructure entry, not a real integration vendor. Would not map to any PlatformType. |
| `Import from CSV` (live) | Synthetic/virtual integration. `integrationCodeId` is all-zeros. No real vendor. |
| `Microsoft Entra ID` (live) | Category is "SIEM" which seems wrong for an identity provider; may cause mis-routing on category-based dispatch. |
| `Service Now` (seed, appears twice) | Duplicate name with different `type` values (`service_now`, `service_nowCloud`) — two entries sharing a `name`. |
| `JIRA` vs `Jira` (seed) | Two entries with different casing for the same product. |

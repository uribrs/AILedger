# Cross-artifact consistency review — Qualys SPARK parser vs. fixtures + collector

Anchor: `libs/packages/parsers/qualys/qualysAssetsAndFindings.py` (working-tree diff).
Question: does the parser correctly consume (1) the prototype reference fixtures and
(2) the record shape the collector emits, producing the intended assets + findings?

## Inputs examined

- Parser diff: `qualysAssetsAndFindings.py` + new tests `tests/test_qualys_assets_first.py` (untracked).
- Base: `libs/packages/parsers/common/base_parser.py`, `libs/packages/utilities/helpers.py`.
- Prototype mapper (assets-first reference shape): `IntegrationProbes/Integrations/Qualys/QualysAssetExposureMapper.cs`.
- Prototype fixtures:
  - `QualysAssets_20260609_145715_071Z/combined.ndjson` — 7 hosts, RAW collector shape (`{HOST_DETAILS, DETECTION_LIST}`).
  - `QualysAssets_.../asset_with_exposures.json` — prototype-mapper OUTPUT (one asset + 17 exposures), NOT parser input.
  - `QualysExposurelessAsset_.../exposureless_asset.json` — prototype-mapper OUTPUT, empty `exposures`. Synthesized.
- Collector emit: `Collectors/QualysCollector/Flows/Findings/QualysFindingsXmlParser.cs`, `QualysFindingsFlow.cs`.

> Clarifying note on the fixtures. `asset_with_exposures.json` / `exposureless_asset.json` are the
> *prototype mapper's own output* in the assets-first target shape — they are NOT what the SPARK
> parser ingests. The parser ingests the RAW `{HOST_DETAILS, DETECTION_LIST}` shape, which is
> `combined.ndjson` and what the collector publishes. The review treats `combined.ndjson` /
> collector output as parser input, and the two `*.json` mapper outputs as the target spec to
> compare the parser's result against. This distinction is load-bearing for findings 1, 2, 4.

---

## 1. Exposureless host → exactly one asset, zero findings — CORRECT

The decoupling is structurally sound.

- Assets frame is built pre-explode: `assets_source_df = full_df.selectExpr("HOST_DETAILS.*")`
  (`qualysAssetsAndFindings.py:244`). One row per host regardless of `DETECTION_LIST`. An
  empty/null detection list host survives as an asset. ✔
- Findings frame uses INNER `explode(DETECTION_LIST)` (`:251-256`). Inner explode emits zero rows
  for an empty/null array, so the exposureless host contributes zero findings and no
  null-detection junk row. ✔
- Test `test_qualys_empty_detection_host_becomes_asset_with_zero_findings`
  (`test_qualys_assets_first.py:97`) exercises exactly this and asserts both: 2 assets, findings
  only from the host that had detections.

Severity: none. This is the core fix and it is correct.

One caveat (LOW): the test always pairs the exposureless host with a host that HAS detections.
That's deliberate (comment at `:100` — keeps the array element type concrete so Spark infers a
struct, not `array<null>`). If a batch ever contains *only* exposureless hosts, `DETECTION_LIST`
infers as `array<void>` / `array<string>` and `explode(...).selectExpr("DETECTION.*")` could fail
schema resolution. The collector's batching makes an all-exposureless page unlikely but not
impossible (a page of hosts that all had detections filtered out upstream). Not exercised by any
test. Worth a one-line guard or a dedicated test.

## 2. asset_with_exposures.json — identity + correlation model — CORRECT, model differs by design

Against the underlying RAW host (`combined.ndjson` row 0, the source of this mapper output):

- Asset identity `value` = `NETBIOS` else `IP` (`:54-56`), then lowercased by the base-class wrap
  (`base_parser.py:118`). Host 0 → `value = "qualys-agent-01"`. ✔ Matches mapper's
  `FirstNonEmpty(netbios, ip)` (`QualysAssetExposureMapper.cs:54`).
- One finding per detection via inner explode → 17 findings for this host (pre-cve-filter; see §4).
- Finding `asset_value` = `NETBIOS` else `IP` (`:201-207`), `asset_type` = `"Host"` (`:196-200`).

Correlation model — the mismatch to note, and it is benign:

- Prototype mapper expresses correlation **two ways**: `asset.finding_ids[] <-> exposure.id`
  (an explicit ID list on the asset) AND `exposure.asset_value/asset_type`
  (`QualysAssetExposureMapper.cs:59,93-94`).
- The SPARK parser does NOT consume `finding_ids` from input and does NOT use `exposure.id` to
  join. It joins on **`(asset_type == type) AND (lower(asset_value) == value)`**
  (`qualysAssetsAndFindings.py:340-344`), assigns its own UUID asset `id` (`:305`) and finding
  `id` (`:349`), and writes `finding.asset_id = asset.id`. The asset's `finding_ids` is seeded
  empty (`_asset_mandatory_fields_raw "finding_ids" default F.array()`) and is NOT re-populated
  in this parser's `process`/`post_process` (the base helper `add_finding_ids_to_asset_df` exists
  but is not called here).

So: the parser uses the `asset_value/asset_type` half of the prototype's correlation model and
ignores the `finding_ids <-> exposure.id` half. The join is **value-based, not id-based**. That is
internally consistent (finding→asset via `asset_id`), and it is the correct half to rely on,
because the mapper's `finding_ids`/`exposure.id` are mapper-synthesized strings
(`<INSTANCE_ID>:189763865:1983789889`) that do not exist in the RAW collector input the parser
actually reads — they could not be joined on anyway.

- Case handling is correct: asset `value` is lowercased at extraction; `asset_value` is not, but
  the join lowercases it inline (`F.lower(F.col("asset_value"))`), so `QUALYS-AGENT-01` (finding
  side) matches `qualys-agent-01` (asset side). ✔

Severity: LOW / informational. No bug. Note for downstream: if any consumer expects the asset row
to carry a populated `finding_ids` array (as the prototype mapper output shows), it will be empty
from this parser — `finding_ids` is realized only as `finding.asset_id` back-references, not as a
forward list on the asset.

## 3. Field-name / nesting assumptions — MOSTLY CORRECT, one dead path + KB-only fields

Verified against `combined.ndjson` actual keys.

RAW `HOST_DETAILS` keys present: `ID, ASSET_ID, IP, TRACKING_METHOD, DNS, DNS_DATA{HOSTNAME,
DOMAIN, FQDN}, NETBIOS, OS, QG_HOSTID, LAST_BOOT, SERIAL_NUMBER, HARDWARE_UUID, FIRST_FOUND_DATE,
LAST_ACTIVITY, AGENT_STATUS, CLOUD_AGENT_RUNNING_ON, LAST_VULN_SCAN_DATETIME,
LAST_VM_SCANNED_DATE, LAST_VM_SCANNED_DURATION, LAST_VM_AUTH_SCANNED_DATE`.

RAW `DETECTION` keys present: `UNIQUE_VULN_ID, QID, TYPE, SEVERITY, SSL, RESULTS, STATUS,
FIRST_FOUND_DATETIME, LAST_FOUND_DATETIME, TIMES_FOUND, LAST_TEST_DATETIME, LAST_UPDATE_DATETIME,
IS_IGNORED, IS_DISABLED, LAST_PROCESSED_DATETIME` (+ `PORT, PROTOCOL, LAST_FIXED_DATETIME` on some).

| Parser reads | Source | In RAW fixtures? | Verdict |
|---|---|---|---|
| `NETBIOS`, `IP`, `OS` (host) | HOST_DETAILS top-level | yes | ✔ |
| `DNS_DATA.FQDN` → fallback `DNS` (`:114-119`) | nested + top-level | yes (`DNS_DATA.FQDN=""`, `DNS="qualys-agent-01"`) | ✔ fallback fires correctly |
| `QID, SEVERITY, STATUS, FIRST_FOUND_DATETIME, LAST_FOUND_DATETIME` (`:164-184`) | DETECTION | yes | ✔ |
| `VULNERABILITY_INFO.TITLE / .SOLUTION / .DIAGNOSIS / .CVE_LIST.CVE` (`:161-195`) | DETECTION (KB enrichment) | **NO** in fixtures | KB-only — see §4 |

Findings:

- **`fqdn` flat-path was dead before this diff (now fixed).** The old `"path": "FQDN"` resolved
  against `HOST_DETAILS.*`, which has no top-level `FQDN` (it's nested under `DNS_DATA`). It
  always produced `""`. The diff correctly moves to `DNS_DATA.FQDN` with `DNS` fallback,
  matching `QualysAssetExposureMapper.ResolveFqdn` (`.cs:115-127`). ✔ This is a real
  pre-existing bug the diff repairs. (MEDIUM-value fix.)
- **`VULNERABILITY_INFO.*` is absent from the RAW fixtures** (`grep VULNERABILITY_INFO combined.ndjson`
  → 0). It is NOT a host or base-detection field — it is **KB-enrichment** attached
  post-hoc by the collector. `extract_module_data` handles the missing nested path gracefully
  (`base_parser.py:362` falls through to default when path not in schema), so `name`,
  `mitigation`, `description` → `""`/null and `cve_ids` → empty array. No crash, but see §4 for
  the downstream consequence.
- `asset_additional_fields` / `finding_additional_fields` properties (`:138-151`, `:211-224`)
  define a `qualys_was`-style mapping that is **not used anywhere** in `process`/`post_process` —
  additional_fields is built generically from `*_source_df.columns` (`:285-288`, `:320-323`).
  Pre-existing dead code, unrelated to this diff. (LOW.)

Severity: the live `fqdn` fix is correct and material; `VULNERABILITY_INFO` dependence is the
real risk, carried to §4.

## 4. Empty-cve_ids drop — ZERO findings from the unenriched fixtures — HIGH for "findings from fixtures", LOW for the assets-first goal

This is the most important cross-artifact finding.

`base_parser.post_process` (`base_parser.py:226-235`) drops every finding where
`type == "vulnerability"` AND `cve_ids` is empty:

```python
self.findings_df = self.findings_df.filter(
    ~( (F.col("type") == "vulnerability") &
       (F.size(F.expr("filter(cve_ids, x -> x is not null AND x != '')")) == 0) ) )
```

All Qualys findings are `type = "vulnerability"` (hard-coded default, `:163`). `cve_ids` comes
solely from `VULNERABILITY_INFO.CVE_LIST.CVE` (`:185-194`).

Consequence against the RAW fixtures / unenriched collector output:

- `combined.ndjson` and the host behind `asset_with_exposures.json` carry **no `VULNERABILITY_INFO`,
  no `CVE_LIST`** → every detection's `cve_ids` is empty → **all 17 detections are dropped →
  ZERO findings emitted** for that asset. Same for all 7 hosts in `combined.ndjson` (78 detections
  total, all dropped).
- This exactly matches what the prototype mapper labels at `QualysAssetExposureMapper.cs:90`
  (`cve_ids = []` "KB enrichment omitted") and the mapper's exposures all carry `cve_ids: []`
  (`asset_with_exposures.json:79` etc.). So the mapper output, fed through this filter, also
  yields zero findings.

What makes findings appear: the collector's `EnrichDetections`
(`QualysFindingsXmlParser.cs:280-311`) attaches `VULNERABILITY_INFO` per-QID **only when the KB
lookup hits** (`:302-303`). So real, fully-enriched collector output DOES carry CVEs for QIDs that
have them, and those findings survive. The new test confirms this path: `_detection(cves=[...])`
injects `VULNERABILITY_INFO.CVE_LIST.CVE` and the test asserts the finding survives with a
populated `cve_id` (`test_qualys_assets_first.py:59-65, 148-150`).

Two real exposures here:

1. **QIDs with no CVE are silently dropped even in production.** Many Qualys QIDs are config/IG
   checks (e.g. fixture QID 90007 "cachedlogonscount", 90126 "RebootPending") that legitimately
   have no CVE. With this filter, those are NOT emitted as findings at all — regardless of
   enrichment. Whether that is intended (CVE-only findings) or a data-loss bug depends on product
   intent. This is a **pre-existing** behavior the diff does not change, but it directly determines
   the finding count and is worth an explicit product decision. Severity: **MEDIUM** (silent
   findings loss), **HIGH** if non-CVE vulns are expected downstream.
2. **For validating the parser against the probe fixtures, findings cannot be observed** — the
   fixtures are unenriched, so any fixture-driven test that asserts on `findings_df` rows must
   inject `VULNERABILITY_INFO` (as the new tests do). The probe's raw output alone proves assets,
   not findings.

For the assets-first goal specifically: severity is **LOW**. Assets are produced from the
host frame and are entirely independent of the cve filter; the exposureless and exposure-bearing
hosts both yield their asset rows regardless. The findings-drop does not threaten the assets-first
objective — it only means "findings need enrichment to appear," which is expected.

## 5. additional_fields — extra HOST_DETAILS fields land without schema change — CORRECT

- Asset `additional_fields` is built from `assets_source_df.columns` (every HOST_DETAILS column,
  lowercased) via `BaseParser.safe_struct` (`:285-292`). `serial_number`, `hardware_uuid`,
  `agent_status`, `qg_hostid`, `asset_id`, `dns_data`, the timestamp fields, etc. all flow through
  with no schema edit. ✔ The struct is later `to_json`'d in `create_asset_source`
  (`base_parser.py:533`), so arbitrary new HOST_DETAILS keys serialize without a schema change.
- Test `test_qualys_asset_additional_fields_populated_from_host_details`
  (`test_qualys_assets_first.py:153`) asserts `serial_number, hardware_uuid, agent_status,
  qg_hostid, asset_id` are present AND that detection-level columns (`qid, unique_vuln_id,
  results`) do NOT leak into the asset's additional_fields — which holds, because the asset frame
  is the pre-explode host frame (no detection columns exist in it). ✔ This is the structural payoff
  of the decoupling.
- `safe_struct` correctly guards the empty-column edge case (`base_parser.py:318-331`), though it
  won't trigger here since HOST_DETAILS always has columns.

Severity: none.

---

## Summary of severities

| # | Topic | Verdict | Severity |
|---|---|---|---|
| 1 | Exposureless host → 1 asset / 0 findings (pre-explode assets, inner-explode findings) | Correct | none (LOW: no all-exposureless-page test) |
| 2 | Identity (NETBIOS-else-IP) + finding↔asset correlation | Correct; value-based join, not id-based | LOW / informational |
| 3a | `fqdn` moved to `DNS_DATA.FQDN`→`DNS` | Correct, repairs dead flat path | fix is MEDIUM-value |
| 3b | `VULNERABILITY_INFO.*` is KB-only, absent from fixtures | Handled gracefully | feeds §4 |
| 3c | Unused `*_additional_fields` properties | Pre-existing dead code | LOW |
| 4 | Empty-cve_ids drop → 0 findings from unenriched fixtures; non-CVE QIDs dropped in prod too | Pre-existing; correct for assets-first | HIGH for "findings from fixtures" / MEDIUM-HIGH product question; LOW for assets-first |
| 5 | additional_fields carries extra HOST_DETAILS w/o schema change; no detection leak | Correct | none |

## Bottom line — shape mismatches between parser and what is emitted

- **No structural shape mismatch** between the parser's `HOST_DETAILS.*` / `explode(DETECTION_LIST)`
  assumptions and what the collector (`QualysFindingsXmlParser`/`QualysFindingsFlow`) and the RAW
  `combined.ndjson` actually emit. Field names and nesting line up.
- **One semantic gap**: the parser's findings depend entirely on `VULNERABILITY_INFO.CVE_LIST.CVE`,
  which is (a) collector KB-enrichment, absent from the probe fixtures, and (b) absent for any QID
  with no CVE even in production. Net effect: the probe fixtures prove assets-first end-to-end but
  prove zero findings; observing findings requires enriched input (the new tests inject it). The
  non-CVE-QID drop is a pre-existing product behavior worth an explicit decision, but it does not
  jeopardize the assets-first objective.
- **One correlation-model note**: the parser realizes correlation as `finding.asset_id ->
  asset.id` via a value-based join, not via the prototype's `asset.finding_ids[] <-> exposure.id`.
  The asset's `finding_ids` is emitted empty. Consistent and correct, but downstream consumers
  expecting a populated forward list on the asset will not get one from this parser.

Certainty: HIGH on §1, §3, §5 (verified against fixture keys and code paths). HIGH on §4's
mechanism and the zero-findings-from-fixtures conclusion (filter at `base_parser.py:226-235`,
enrichment conditional at `QualysFindingsXmlParser.cs:302-303`, 0 `VULNERABILITY_INFO` in
`combined.ndjson`). MEDIUM on whether the non-CVE-QID drop is intended vs. a product defect — that
is a product-intent question, not determinable from code alone.

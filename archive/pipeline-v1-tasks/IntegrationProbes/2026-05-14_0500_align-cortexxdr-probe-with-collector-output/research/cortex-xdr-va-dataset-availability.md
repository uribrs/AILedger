# Cortex XDR `va_endpoints` / `va_cves` dataset availability

## Verdict

`va_endpoints` and `va_cves` are real, documented Cortex XDR XQL datasets that power the Vulnerability Assessment views. They are populated only when the **Host Insights add-on** is licensed AND the **"Enable Host Insights Capabilities"** setting is turned on in the agent settings profile applied to the endpoints. The tenant we probed shows both datasets listed under `get_datasets` with `Last Updated: null`, which is the textbook signature of "Host Insights add-on not active / not enabled on agents / no scan has run yet."

## Licensing / activation

1. Base SKU: **Cortex XDR Pro per Endpoint** (PAN-XDR-ADV-EP).
2. Add-on SKU: **Host Insights** (PAN-XDR-HOST-INST). 3-month free trial from activation; without it, Vulnerability Management / `va_*` datasets do not populate.
3. After activation, the **Agent Settings Profile** assigned to the endpoint must have **"Enable Host Insights Capabilities"** = on. This setting is only visible once Host Insights is activated under the License dialog.
4. The endpoint agent does the scan: first scan happens after the policy lands, then every 24 h. Linux + Windows + macOS supported.

## Documented field names (corrections vs. our projection)

| Dataset | Field | Our projection | Documented | Notes |
|---|---|---|---|---|
| `va_endpoints` | endpoint_name | ✅ | ✅ | Standard join key in every public example. |
| `va_endpoints` | cves | ✅ | ✅ | Array of CVE IDs. |
| `va_endpoints` | severity | ✅ | ✅ | |
| `va_endpoints` | severity_score | ✅ | ✅ | |
| `va_endpoints` | endpoint_id | ✅ | ⚠️ unconfirmed | Plausible (standard Cortex identifier) but not seen in any public `va_endpoints` sample. Public examples always use `endpoint_name` as the key. |
| `va_cves` | cve_id | ✅ | ✅ | |
| `va_cves` | severity | ✅ | ✅ | |
| `va_cves` | severity_score | ✅ | ✅ | |
| `va_cves` | description | ✅ | ❌ no documented field | No public sample projects `description`. Likely cause of "unknown field" if exercised on a populated tenant. |
| `va_cves` | name | — | ✅ | CVE name; sample projections do `fields name, cve_id, ...`. |
| `va_cves` | affected_hosts | — | ✅ | Array of endpoint names. The collector / probe never asked for it — but it's the documented per-CVE → endpoint link. |
| `va_cves` | affected_endpoints | ❌ rejected on this tenant | ❌ not a field | Already confirmed in our probe run; `affected_hosts` is the right name. |

## Alternative paths (if Host Insights cannot be activated)

- `host_inventory_applications` / `host_inventory_endpoints` (preset) — application/host inventory, but does NOT contain CVE mappings on its own. Cymulate would have to join with an external CVE feed.
- There is **no public REST endpoint** under `/public_api/v1/vulnerability/` or `/public_api/v1/endpoints/get_vulnerabilities` in the Cortex XDR REST API. VA data is exposed only via XQL on `va_endpoints` / `va_cves`.

So: without Host Insights, the integration cannot retrieve CVE data from Cortex XDR.

## Implications for the collector

The AgentService `CortexXdrCollector` uses:

```
dataset = va_cves | fields cve_id, description, severity, severity_score | limit N
```

If `description` is not a real `va_cves` column, this query will FAIL on a populated tenant (status `FAIL`, `unknown field description`). Two flow-on consequences in the current collector:

1. `cveRows` becomes null → `cveDetailsById` is empty → every emitted vulnerability's `description` field is null and `severity` falls back to the endpoint-row severity. Not a crash, but the whole point of the `va_cves` enrichment is lost.
2. The probe will exhibit the same behavior on a populated tenant.

Worth surfacing to whoever owns the collector — this is testable only against a tenant with Host Insights and at least one scanned endpoint.

## Evidence

- [LIVEcommunity 1224904 — "Cortex XDR XQL query to get list of all vulnerabilities"](https://live.paloaltonetworks.com/t5/cortex-xdr-discussions/cortex-xdr-xql-query-to-get-list-of-all-vulnerabilities/td-p/1224904)
- [LIVEcommunity 1224021 — "Role to access va_cves and va_endpoint datasets"](https://live.paloaltonetworks.com/t5/cortex-xdr-discussions/role-to-access-va-cves-and-va-endpoint-datasets/td-p/1224021)
- [LIVEcommunity 571636 — "XQL Query for CVE counts in XDR per endpoint_name"](https://live.paloaltonetworks.com/t5/cortex-xsoar-discussions/xql-query-for-cve-counts-in-xdr-per-endpoint-name/td-p/571636)
- [Cortex XDR Pro Admin Guide — Vulnerability Assessment](https://docs-cortex.paloaltonetworks.com/r/Cortex-XDR/Cortex-XDR-Pro-Administrator-Guide/Vulnerability-Assessment)
- [Cortex XDR 3.x — Vulnerability Assessment (VA)](https://docs-cortex.paloaltonetworks.com/r/Cortex-XDR/Cortex-XDR-3.x-Documentation/Vulnerability-Assessment-VA)
- [Cortex XSIAM — Vulnerability Assessment](https://docs-cortex.paloaltonetworks.com/r/Cortex-XSIAM/Cortex-XSIAM-Documentation/Vulnerability-Assessment)
- [Host Insights for Cortex XDR — datasheet](https://www.paloaltonetworks.com/resources/datasheets/host-insights-for-cortex-xdr)
- [PAN-XDR-HOST-INST SKU listing](https://www.cdw.com/product/cortex-xdr-host-insights-add-on-license-1-license/6328602)
- [Cortex XDR REST API reference (Stoplight)](https://cortex-panw.stoplight.io/docs/cortex-xdr)
- [PaloAltoNetworks/cortex-xql-queries — official sample repo](https://github.com/PaloAltoNetworks/cortex-xql-queries)

## Confidence

- **High** that the datasets are real, Host-Insights-gated.
- **High** that `affected_hosts` is the right field, not `affected_endpoints` (corroborated by our probe run).
- **Medium-high** on the licensing / activation flow (paywalled doc bodies).
- **Medium** on the absence of `description` on `va_cves` (based on absence in every public sample; authenticated docs login would settle it).
- **Medium** on `endpoint_id` being a real `va_endpoints` column.

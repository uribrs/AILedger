# Cortex XDR server-side correlation for (endpoint × CVE)

## Verdict

Yes — Cortex XDR XQL supports server-side correlation via the `join` stage. A single XQL call against `va_cves` joined to the `host_inventory_endpoints` preset returns per-(endpoint, CVE) rows with both sides' fields. There is **no REST endpoint** that returns this shape; XQL is the only path.

## The canonical one-query shape (community-validated)

```
dataset = va_cves
| fields name, cve_id, severity, severity_score, affected_hosts, description, publication_date, modification_date
| arrayexpand affected_hosts
| join (preset = host_inventory_endpoints
        | fields endpoint_name, endpoint_id, operating_system, endpoint_type, last_report_time) as ep
       ep.endpoint_name = affected_hosts
```

Source: [LIVEcommunity 1224904](https://live.paloaltonetworks.com/t5/cortex-xdr-discussions/cortex-xdr-xql-query-to-get-list-of-all-vulnerabilities/td-p/1224904).

`arrayexpand` explodes each row's `affected_hosts[]` into one row per host. The join then matches host name against the `host_inventory_endpoints` preset to attach endpoint metadata.

## Key references

- [Join — Cortex XDR XQL Language Reference](https://docs-cortex.paloaltonetworks.com/r/Cortex-XDR/Cortex-XDR-XQL-Language-Reference/Join) — supports `inner` / `left` / `right` joins, alias via `as`, qualified column access (`<alias>.<column>`).
- [PaloAltoNetworks/cortex-xql-queries](https://github.com/PaloAltoNetworks/cortex-xql-queries) — official sample repo.
- [xsoar.pan.dev — Cortex XDR XQL Query Engine](https://xsoar.pan.dev/docs/reference/integrations/cortex-xdr---xql-query-engine) — quota and access-view notes.
- [LIVEcommunity 1231300](https://live.paloaltonetworks.com/t5/cortex-xdr-discussions/get-assets-and-vulnerabilities-from-cortex-xdr-via-api/td-p/1231300) — PAN staff confirm there is no dedicated REST endpoint for this.

## Access-view caveat

> "Investigation query view will provide you access to all of the datasets except `endpoints` and `host_inventory`."

So **join against the preset `host_inventory_endpoints`, not the raw `host_inventory` dataset.** API XQL uses the same engine; the preset works, the raw dataset does not.

## Practical limits

- Default result cap is **100 rows**; raise with `| limit N`.
- Over **1 000 rows** the API returns a gzipped result file rather than inline JSON. With `arrayexpand`, you generate one row per (host, CVE) pair, so this threshold hits quickly. Paginate by `modification_date` / `publication_date` window if needed.
- **Max 4 concurrent XQL queries per tenant** (API-level).
- Daily quota tracked at `/public_api/v1/xql/get_quota`.
- Joining does not appear to be priced separately from flat queries in the public docs.

## Field-set tradeoff vs `/endpoints/get_endpoint`

`host_inventory_endpoints` documented fields (community samples): `endpoint_name`, `endpoint_id`, `operating_system`, `endpoint_type`, `last_report_time`, etc.

Fields the `/endpoints/get_endpoint` REST call provides that `host_inventory_endpoints` may NOT (empirically unconfirmed — preset returned 0 rows on our tenant): `mac_address[]`, full `ip[]` list, `os_version` build string, `first_seen`, `domain` (for fqdn derivation), `group_name`, `tags`.

If the downstream needs MAC / full IP / OS build / fqdn, the server-side join alone is not a drop-in replacement; you would also do a per-`endpoint_id` enrichment via `/get_endpoint`.

## Three viable architectures

1. **Single XQL join (lean).** One call returns per-(host, CVE) rows. Asset side limited to what `host_inventory_endpoints` exposes. Best when downstream tolerates the slimmer asset shape and you want maximum simplicity. Caps at 1 000 rows inline; gzip path beyond that.
2. **Server-side join + REST enrichment.** XQL join for the correlation; `/endpoints/get_endpoint` (batched by `endpoint_id`) only for the fields the preset omits. Slightly more I/O, full asset shape preserved.
3. **Raw upload + correlate upstream.** Probe/collector emits `va_cves` rows and `/endpoints/get_endpoint` rows unjoined; AgentService server does the correlation. No XQL join cost, no preset field-set tradeoff. Most flexible, but moves CPU and disk to the AgentService side.

## Confidence

- **High** that XQL `join` works across `va_cves` and `host_inventory_endpoints`.
- **High** that no REST endpoint returns the correlated shape (multiple independent confirmations).
- **Medium** on the full `host_inventory_endpoints` field set in the tenant — preset returned 0 rows in the discovery probe, so the projection-error trick we used for other datasets can't surface the schema. Confirmable by adding a discovery query `preset = host_inventory_endpoints | limit 5` (no field list) and inspecting the row shape, OR by running the join query directly.

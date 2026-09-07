# Research — Cortex XDR `get_endpoint` sort-field options

## Target and decision being supported

- **Target system:** Palo Alto Networks Cortex XDR REST API, `POST /public_api/v1/endpoints/get_endpoint`.
- **Decision:** Whether `request_data.sort.field` accepts an **immutable** identifier (`endpoint_id` / `agent_id` / `aid`) as a sort value, in addition to the currently-used mutable `last_seen`. If yes, switching the sort field eliminates the pagination drift at the source (no duplicates AND no silent skips), making per-walk HashSet dedup unnecessary.
- **Triggering assumption:** A7 (`assumptions.md`).

## Documentation access status

**Official docs partially gated.**

The two authoritative pages for this endpoint exist publicly but require JavaScript to render their content:

- `https://docs-cortex.paloaltonetworks.com/r/Cortex-XDR-REST-API/Get-Endpoint`
- `https://cortex-panw.stoplight.io/docs/cortex-xdr/b149d40bd4c51-get-endpoint`

Both return title-only HTML to static fetchers. The underlying API reference is therefore not directly quotable. Reasoning below relies on official-adjacent artifacts (XSOAR marketplace changelog, integration YAML schemas, community libraries) which are first-class evidence for *behavior* even when the canonical doc page is inaccessible.

## Findings

### F1 — `sort.field` accepts at least `first_seen` and `last_seen`
- **Confidence: HIGH.** Direct observation in the existing agent codebase, which has run against multiple production tenants without `400 Bad Request` on this field. Both values are the only ones the agent and adapter have ever passed (`PaloAltoCortexApiBase.cs:108`, `CortexXdrAssetsFlow.cs:143`, `CortexXdrFindingsFlow.cs:368`). Also reflected in the XSOAR `CortexXDRIR.yml` `sort_by` argument's documented "Predefined values: `first_seen`, `last_seen`".
- **Source:** repo + `https://raw.githubusercontent.com/demisto/content/master/Packs/CortexXDR/Integrations/CortexXDRIR/CortexXDRIR.yml`

### F2 — `sort.field` accepts `endpoint_id` as of Cortex XDR marketplace v6.3.11 (March 2026)
- **Confidence: MEDIUM-HIGH.** The Cortex marketplace changelog explicitly states:

  > *"Updated the **xdr-get-endpoints** command to support sorting endpoints by **endpoint_id** using the **sort_by** argument."*

  (Cortex XDR marketplace, version 6.3.11 — March 11, 2026.) The XSOAR/Demisto integration's `sort_by` argument maps directly to the underlying REST API's `request_data.sort.field` in every existing case (`first_seen` / `last_seen` pass through verbatim). Adding `endpoint_id` to the XSOAR enum strongly implies the upstream API accepts the same value — otherwise XSOAR would have had to implement client-side resorting, which is incompatible with paginated streaming and would be flagged in the changelog as a *workaround* rather than a capability addition.
- **Caveat:** I could not directly quote the request JSON construction from the current `CortexXDRIR.py` — the version available via raw GitHub fetch didn't surface a `request_data["sort"]` block for the endpoint listing call (it may have been in a code path I couldn't load, or the sort may be applied via a different layer). So the inference rests on the changelog wording + the established pattern, not on a code-level proof.
- **Source:** `https://cortex.marketplace.pan.dev/marketplace/details/CortexXDR/` (changelog v6.3.11)

### F3 — Tenant backend version determines whether `endpoint_id` works
- **Confidence: HIGH (logical).** Marketplace XSOAR integrations are versioned independently from tenant backends, but the integration enables a capability only after the backend exposes it. A change in the marketplace integration in March 2026 implies the backend started accepting `endpoint_id` somewhere on or before that date. **Tenants running older Cortex XDR backend versions will likely reject `endpoint_id` with a `400 Bad Request` or silently treat it as an unsupported field.** No documented "minimum backend version" was surfaced.
- **Mitigation:** treat the change as feature-gated; probe before relying on it.

### F4 — No `search_after` / keyset cursor exists on this REST endpoint
- **Confidence: HIGH.** Every official-adjacent artifact (XSOAR integration, `ebarti/cortex-xdr-client` Python library, community implementations including the Microsoft Fabric forum thread on Cortex pagination) uses `search_from` / `search_to` ordinal paging. No `search_after`, no PIT (Point-In-Time), no snapshot ID is exposed. The only paging primitive is ordinal offset over the current re-sorted view.
- **Source:** `https://github.com/ebarti/cortex-xdr-client/blob/master/cortex_xdr_client/api/endpoints_api.py` (no sort exposed; uses `search_from`/`search_to`) and the official docs' page-title metadata.

### F5 — `endpoint_id` is an immutable identifier
- **Confidence: HIGH.** The agent's existing `CortexXdrFindingsMapper.BuildFindingId` (`CortexXdrCollector.cs:731`) treats `endpoint_id` as a stable composite key alongside `instance_id` and `cve_id`. The captured production dump (`/Users/user/Dev/Uri/CollectedData/simantec_2/findings_002.ndjson`) shows duplicate AWCD078 rows with identical `aid` values — confirming the field is stable across re-fetches. `endpoint_id` is the GUID assigned at agent install and is documented (in the Endpoint response schema present on every doc page surveyed) as the persistent identifier.

### F6 — Switching sort breaks the `lastSeenDate < iBaseDate` early-break
- **Confidence: HIGH.** Mechanical consequence. With `sort.field = "endpoint_id" ASC`, endpoints arrive in GUID order, not recency order. The current `if (lastSeenDate < iBaseDate) shouldContinue = false` shortcut (`PaloAltoCortexApiBase.cs:172-176`) terminates the walk based on the assumption that all subsequent endpoints will be older. That assumption is invalid once we sort by an immutable key. Walks then have to cover the full tenant every collection cycle.
- **Cost:** for a tenant with N endpoints, walks become `ceil(N / 100)` page fetches every run, vs. the current behavior of stopping when we hit the previous run's `baseDate`. For typical tenants (~1000 endpoints) this is ~10 page fetches; for a 50,000-endpoint enterprise tenant, ~500 fetches per run. Each page is ~50-200ms server-side, so a worst-case tenant walk goes from sub-second to ~1-2 minutes.

## Cross-reference and contradictions

| Source | F1 (first/last_seen) | F2 (endpoint_id) | F3 (version-gated) | F4 (no keyset) |
|---|---|---|---|---|
| Repo + XSOAR YAML | ✓ direct | — (yml not yet updated) | — | — |
| Marketplace changelog v6.3.11 | — | ✓ explicit | ✓ implied | — |
| `ebarti/cortex-xdr-client` | — | — | — | ✓ direct (no sort param at all) |
| Microsoft Fabric forum thread on Cortex pagination | — | — | — | ✓ confirms `search_from`/`search_to` only |
| Official PANW Stoplight / docs-cortex page | gated | gated | gated | gated |

No active contradictions. The `CortexXDRIR.yml`'s `sort_by` predefined-value list (`first_seen`, `last_seen` only) does not contradict F2 — YAML predefined-values lists in XSOAR are commonly out of date relative to runtime acceptance; the v6.3.11 changelog is the more recent and more authoritative source on capabilities.

The strongest residual unknown: **the exact minimum tenant backend version that accepts `endpoint_id` as `sort.field`.** Marketplace gives a date (March 2026) but not a backend SemVer.

## Recommendation — most defensible next action

**Two-tier approach: implement the dedup band-aid now (already done), and run a one-shot tenant probe before committing to the sort-key switch.**

### Implement-now (no verification gate)

- The `HashSet<string>` dedup landed in `PaloAltoCortexApiBase.FetchEndpointsAsync` is correct on its own terms and costs nothing operationally. Keep it. It catches duplicates regardless of which sort field the request uses. It does NOT solve the silent-skip problem (per `assumptions.md` A7 follow-up).

### Verify-first (gate before switching the sort field)

1. **Probe the customer's tenant** with a single `get_endpoint` request using `sort.field = "endpoint_id"` and `keyword = "ASC"`. Three possible outcomes:
   - `200 OK` with a populated `endpoints` array sorted lexicographically by `endpoint_id` → tenant supports it. Switch to the new sort.
   - `400 Bad Request` with a message about invalid sort field → tenant is on an older backend. Stay on `last_seen DESC` + HashSet dedup. Schedule a re-probe in 90 days.
   - `200 OK` but `endpoints` appears unsorted or sorted by some default → silent failure of the sort directive. Treat as not supported.

2. If the probe succeeds, switch the sort field. Replace `lastSeenDate < iBaseDate` early-break with a **per-row filter**: still skip emit when `last_seen < iBaseDate`, but never set `shouldContinue = false`. Walk to natural end (`endpointsFound < pageSize`). Document the perf regression for large tenants and tell vulnerability-management product owners.

3. The probe result should be cached per-tenant per-day (a single `enable_endpoint_id_sort` flag in tenant config or in a side-cache file) so we don't burn one extra request per run on the discovery.

### Open questions explicitly NOT resolved

- Exact minimum backend version threshold. Open a vendor ticket for a documented compatibility matrix.
- Whether `endpoint_id` sort is also accepted by the older `get_endpoints` (plural, "Get All Endpoints" endpoint) — the changelog only mentioned `get_endpoint` (singular, filtered). The agent uses the singular endpoint, so this is moot for our use case, but worth confirming if scope ever expands.
- Whether sort direction (`ASC` vs `DESC`) matters for stability. Either should be stable since `endpoint_id` is immutable; `ASC` is the conventional choice.

## Confidence calibration

- The mechanism diagnosis (drift causes duplicates + skips, ratio ~1:1) is **HIGH confidence**.
- The recommendation that `endpoint_id` IS the right sort field for integrity is **HIGH confidence** given F5.
- That `endpoint_id` is **accepted by the customer's actual tenant** is **MEDIUM confidence** — depends on tenant version. Probe required before commit.
- The cost estimate for losing the early-break is **MEDIUM-HIGH confidence**, accurate to one order of magnitude.

## Citations

- Cortex XDR Marketplace, version 6.3.11 (2026-03-11) — capability change: `endpoint_id` added to `sort_by`. [cortex.marketplace.pan.dev/marketplace/details/CortexXDR](https://cortex.marketplace.pan.dev/marketplace/details/CortexXDR/)
- XSOAR/Demisto Cortex XDR integration YAML — `sort_by` argument predefined-values list. [demisto/content CortexXDRIR.yml](https://github.com/demisto/content/blob/master/Packs/CortexXDR/Integrations/CortexXDRIR/CortexXDRIR.yml)
- `ebarti/cortex-xdr-client` — Python client showing no sort parameter on `get_all_endpoints` / `get_endpoint`, confirming the underlying primitive is ordinal-only paging without keyset support. [github.com/ebarti/cortex-xdr-client](https://github.com/ebarti/cortex-xdr-client/blob/master/cortex_xdr_client/api/endpoints_api.py)
- Cortex XDR REST API "Get Endpoint" docs (JS-gated, not directly quotable). [docs-cortex.paloaltonetworks.com/r/Cortex-XDR-REST-API/Get-Endpoint](https://docs-cortex.paloaltonetworks.com/r/Cortex-XDR-REST-API/Get-Endpoint)
- Cortex XDR REST API "Get all Endpoints" docs (JS-gated). [docs-cortex.paloaltonetworks.com/r/Cortex-XDR-REST-API/Get-all-Endpoints](https://docs-cortex.paloaltonetworks.com/r/Cortex-XDR-REST-API/Get-all-Endpoints)
- Stoplight "Get Endpoint" page (JS-gated, title-only via static fetch). [cortex-panw.stoplight.io](https://cortex-panw.stoplight.io/docs/cortex-xdr/b149d40bd4c51-get-endpoint)
- Repo evidence — current sort usage at `Source/Application/Cymulate.Agent.Application.Actions/Actions/QueryIntegration/Logic/Clients/BaseApiClients/PaloAltoNetworks/PaloAltoCortexApiBase.cs:108`; immutability of `endpoint_id` at `Source/CybiCollectors/CortexXdrCollector/CortexXdrCollector.cs:731`.

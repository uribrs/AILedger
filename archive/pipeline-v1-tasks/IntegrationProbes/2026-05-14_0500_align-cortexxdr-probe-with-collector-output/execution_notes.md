# Execution notes

Appended during execution. Notes here record deviations from the contract, decisions discovered during implementation, and validation outcomes.

## Code-reviewer pass 1 — disposition

The code-reviewer subagent ran in isolation (per operator pipeline rule) with no access to the contract, the collector source, or the verifier output. It flagged 3 must-fix items; the dispositions, with rationale:

1. **Duplicate `va_endpoints` end-to-end fetch (`CortexXdrProbeRunner.cs`)** — APPLIED. `ExecuteAllAsync` now fetches `va_endpoints` rows once and passes the cached `JsonArray` to both `BuildFindingIdsByEndpointId` (now synchronous, no I/O) and `ProbeEmitFindingsAsync` (now takes the prefetched rows as a parameter). Also closes a subtle race where the two fetches could return different row sets, leaving asset `finding_ids` out of sync with the emitted findings file.
2. **Cross-row CVE dedup in `BuildFindingIdsByEndpointId`** — REJECTED. The collector itself only dedupes within a single row (`CortexXdrCollector.cs:303` uses `Distinct` on a single `endpointRow["cves"]`). Cross-row dedup would diverge from the collector and violate the byte-identity constraint. The Cortex VA `va_endpoints` dataset has one row per endpoint snapshot, so in practice the same `endpoint_id` does not appear twice; no real duplication occurs at probe scale.
3. **Sparse `asset` inside emitted finding rows** — REJECTED. This is the explicitly documented Cortex XDR collector asymmetry (see `CortexXdrCollector/Examples/README.md` "Known asymmetry" and `emitted_finding_sample.json` reference, which shows `os_type`, `os_version`, `first_seen`, `last_seen`, `ip_address`, `mac_address`, `tags`, and `fqdn` as null inside the finding-embedded asset). The collector's `va_endpoints` projection is fixed at 5 fields and is what the AgentService emits today. Widening the projection or substituting a `/get_endpoint` lookup would produce output that no longer matches the collector — defeating the contract goal.

Should-fix items applied:
- Forced `\n` line endings on both JSONL sidecars (`StreamWriter.NewLine = "\n"`) for cross-platform-stable NDJSON.
- Removed redundant `FileStream` + `StreamWriter` double-wrapping; sidecar writes now use the path-constructor `StreamWriter` directly.

Should-fix items deferred (out of scope or pre-existing):
- `/xql/get_datasets` is still fetched twice (once by discovery `ProbeDatasetInventoryAsync`, once by emit `FetchDatasetsArrayAsync`). Two small calls; not worth tangling the discovery path.
- Pre-existing `PollQueryResultsAsync` leading `Task.Delay`, `NormalizeBaseUrl` port-dropping, and the `os_build = os_version` mapping all match the existing codebase / collector and pre-date this change.

## Divergence from collector — readiness gate removed in probe (2026-05-14)

Live run against a Host-Insights-enabled tenant surfaced a Cortex XDR permission split: the API key has XQL execution rights but not `get_datasets` (catalog) rights. `POST /public_api/v1/xql/get_datasets` returns `403 Forbidden, "Insufficient permissions for api key"`, while `start_xql_query` / `get_query_results` against `dataset = va_endpoints` and `dataset = va_cves` succeed with real data.

The collector's `isDatasetReadyForQuery` (`CortexXdrCollector.cs:329-377` + the calls at `:144` and `:277`) treats a failed inventory call as "dataset not ready" and short-circuits. Worse, in the collector path `FetchXqlDatasetsAsync` calls `EnsureSuccessStatusCode` and would throw on 403, propagating the failure into `CollectFindingsAsync`. Either way the production flow loses real findings data on permission-split keys.

**Change applied (probe only):**
- Removed `IsDatasetReadyForQuery` callsites from `CortexXdrProbeRunner.ExecuteAllAsync` and `ProbeEmitFindingsAsync`.
- Removed `FetchDatasetsArrayAsync` from the runner (no longer used by the emit path; the discovery `ProbeDatasetInventoryAsync` continues to do its own raw POST and is allowed to fail).
- Removed `IsDatasetReadyForQuery`, `FindDataset`, `IsDatasetExplicitlyEmpty`, `TryGetLong`, `GetFirstString`, `GetFirstToken` from `CortexXdrEmitMapper` (dead with the gate gone).
- Emit findings now always attempts both XQL queries (`va_endpoints` first, then `va_cves`); it trusts the XQL response (`SUCCESS` with rows / `SUCCESS` with empty / `FAIL`) rather than a metadata pre-check.

**Divergence consequence:** the probe will now successfully emit findings on tenants where the collector would either short-circuit or crash. To realign, the same change has to be made upstream in `CortexXdrCollector.cs` (both `fetchFindingIdsByEndpointIdAsync` and `CollectFindingsAsync`). Tracked here as a follow-up that lives outside this repo.

**Bonus discovery not yet exploited:** the live `va_cves` rows carry `name` (the public `CVE-YYYY-NNNN`), `publication_date`, `modification_date`, `affected_products`, `affected_hosts`, `exploitability_score`, `impact_score`, `type`, `confidentiality/integrity/availability`. The current collector projection (`cve_id, description, severity, severity_score`) does not pull `name`, so the join key between `va_endpoints.cves[]` and `va_cves` may be wrong (we still have not observed a populated `va_endpoints.cves[]` to confirm whether it holds hashes or `CVE-YYYY-NNNN`). `publication_date` is a confirmed-real epoch-ms field that the collector documents as "unvalidated, pending product confirmation" for the vulnerability `first_seen` slot. Both of these are upstream collector enhancements, not in scope here.

## Reverse-join switch (2026-05-14, post-live-tenant run)

Live run against a Host-Insights-populated tenant (archive `CortexXdr_20260514_080340_616Z`) surfaced the real `va_cves` schema, which includes a richer field set than the collector currently exploits. Most importantly, `va_cves.affected_hosts[]` is a clean, populated array of endpoint **hostnames**, identical in format to `endpoint_name` from `/endpoints/get_endpoint`.

**Change applied:** the probe emit path is now driven by the **reverse** join, not the collector's forward join.

| Aspect | Collector / probe-before | Probe now |
|---|---|---|
| Source of finding↔asset link | `va_endpoints.cves[]` → `va_cves.cve_id` map | `va_cves.affected_hosts[]` → `endpoint_name` |
| Asset shape (`MapAsset`) | unchanged | unchanged |
| Vulnerability `name`/`display_name`/`cve_ids[]` | from `va_endpoints.cves[]` token (format unconfirmed) | from `va_cves.name` (confirmed `CVE-YYYY-NNNN`) |
| Vulnerability `severity` | endpoint-row severity → cve severity → endpoint score → cve score | cve severity → cve score |
| Vulnerability `first_seen` | hardcoded null | `va_cves.publication_date` (epoch ms) |
| Vulnerability `last_seen` | hardcoded null | `va_cves.modification_date` (epoch ms) |
| Vulnerability `description` | from cve details (when join succeeded) | from `va_cves.description` |
| Other fields | unchanged | unchanged |

The vulnerability object's **shape** (key order, field set) is preserved. Only the values that the collector hardcoded to null are now populated from the validated `va_cves` schema.

**Why the reverse join is better in practice:**
1. No dependency on `va_endpoints` being populated. On this tenant the 10 sampled `va_endpoints` rows all carry `cves: ["No CVEs Found"]`; the reverse join still emits real findings because it walks `va_cves.affected_hosts[]` directly.
2. No join-key ambiguity. `va_endpoints.cves[]` format is still unobserved on a populated row; `va_cves.name` is observed and clean.
3. Strictly richer per-vulnerability data (`first_seen`, `last_seen`).

**Coverage caveat:** the probe still emits findings only for endpoints present in the `/endpoints/get_endpoint` page (capped by `QueryLimit`). `va_cves` rows referencing hostnames outside that page are surfaced in the `ProbeRunItem.Body` as an "outside the page" count rather than silently dropped.

**Files touched:**
- `CortexXdrEmitMapper.cs` — replaced `MapVulnerability` / `MapFindingResult` with `MapVulnerabilityFromCve` (va_cves-driven) and `BuildCvesByHost`. Removed the now-dead `ExpandCveIds`, `LooksLikeCveId`, `TryParseJson` helpers.
- `CortexXdrProbeRunner.cs` — `ExecuteAllAsync` now fetches the endpoints page and `va_cves` rows once, builds `cvesByHost`, and passes both to `ProbeEmitAssetsAsync` / `ProbeEmitFindingsAsync`. Removed `BuildFindingIdsByEndpointId`, `LookupFindingIds`, the va_endpoints-based path. Added `FetchEndpointsPageAsync`, `BuildFindingIdsFromCves`.

**Divergence from upstream collector:** larger than the previous gate-removal note. The collector still does the forward join with the field set it had. If we want to converge again, the upstream collector should:
1. Add `name` to its `va_cves` projection and key the details map on `name` (handles whichever format `va_endpoints.cves[]` is).
2. Optionally pull `publication_date`/`modification_date` for richer `first_seen`/`last_seen`.
3. Or — adopt the reverse join entirely.

## Final architecture (2026-05-14, end of session)

After live runs against a Host-Insights-enabled tenant, the design landed on **three sequential stages, no server-side correlation**. The probe now mirrors what the production collector will do, top to bottom.

### Pipeline

1. **Stage 1 — fetch assets.** `POST /public_api/v1/endpoints/get_endpoint` (single page of `QueryLimit` rows). Write each raw `endpoint` object from `reply.endpoints[]` as a JSON line to `assets.jsonl`.
2. **Stage 2 — fetch CVE bank.** XQL query `dataset = va_cves | fields cve_id, name, description, severity, severity_score, affected_hosts, affected_products, publication_date, modification_date, exploitability_score, impact_score, type, is_excluded | limit N`. Write each raw row as a JSON line to `cves.jsonl`.
3. **Stage 3 — correlate on disk.** Read both files. Build `endpoint_name → cveRow[]` map from `va_cves.affected_hosts[]` (case-insensitive). For each endpoint with at least one matched CVE, emit `{asset, vulnerabilities[]}` to `findings.jsonl` using `CortexXdrEmitMapper.MapAsset` + `MapVulnerabilityFromCve`. `finding_id` format: `{instanceId|cortex-xdr}:{endpointId}:{CVE-YYYY-NNNN}`.

### Three HTTP calls in production-shape

- 1 × `POST /endpoints/get_endpoint`
- 1 × `POST /xql/start_xql_query`
- 1 × `POST /xql/get_query_results` (+ optional `get_query_results_stream` for > ~1 000 rows)

Quota cost per run: ~0.004 quota units on probe-sized queries; ~3–10 units on 50 000-row production runs. Daily allowance comfortably covers daily collection.

### What is NOT in the probe anymore

- `POST /xql/get_datasets` and the readiness-gate logic — API key does not need this permission, and the gate misclassified permission-split tenants.
- `va_endpoints` query — the forward join via `va_endpoints.cves[]` is replaced by the reverse join via `va_cves.affected_hosts[]`.
- Server-side XQL `arrayexpand` + `join` — failed twice on filter syntax (`current_time() - 7d` not parsed, raw epoch ms rejected as wrong type). The join itself was never exercised. Dropped.
- 14 discovery XQL queries — purpose served; schema is known and documented in `research/`.
- `host_inventory_endpoints` preset — observed during discovery, not used in the final pipeline. Kept for reference in `research/cortex-xdr-server-side-correlation.md` in case the next agent wants to revisit server-side correlation.

### Files the probe emits per run

| File | Contents | Consumer |
|---|---|---|
| `assets.jsonl` | Raw `endpoint` rows from Cortex `/get_endpoint` response | Stage 3 (and production collector) |
| `cves.jsonl` | Raw `va_cves` rows from XQL | Stage 3 (and production collector) |
| `findings.jsonl` | `{asset, vulnerabilities[]}` per matched endpoint | Downstream / AgentService |
| `results.txt` | Per-stage `ProbeRunItem` summary (`Stage 1` / `Stage 2` / `Stage 3`) | Probe operator |

### Reference for the next agent (AgentService collector)

- File layout under `Integrations/CortexXdr/`:
  - `CortexXdrProbeRunner.cs` — the three-stage pipeline.
  - `CortexXdrEmitMapper.cs` — `MapAsset`, `MapVulnerabilityFromCve`, `BuildCvesByHost`, `BuildFindingId`, `ResolveCveName`.
  - `CortexXdrProbeApiClient.cs` — advanced auth (`x-xdr-timestamp` / `x-xdr-nonce` / SHA256 `Authorization`).
  - `CortexXdrProbeConfiguration.cs` — config including optional `InstanceId` / `ClientId`.
  - `CortexXdrProbeConfigurationBuilder.cs` — collector-compatible key aliases (`cymulate_instanceId`, `cymulate_clientId`, `client_id`).
- The final emit shape (`{asset, vulnerabilities[]}`) is **the contract**. Keep it stable.
- Stages 1 and 2 are independent and safe to retry separately. Stage 3 has no API dependency once the two files exist.
- Production collector should paginate `/endpoints/get_endpoint` (page size 100, sorted by `last_seen DESC`, stop when `last_seen < baseDate`) — this probe does a single page for cost reasons.
- Production collector should raise the XQL limit on stage 2 (collector's `cMaxXqlFindings = 50_000` is the practical ceiling).

## Known scope-bound asymmetries

- At probe `QueryLimit` defaults (10), the `va_endpoints` row set may not overlap with the single-page `/endpoints/get_endpoint` slice, so `finding_ids[]` on the emitted asset may be empty even though the production collector would populate it at 50 000-row scale. This is expected at probe scale and is documented in `assumptions.md` A4.
- The collector's documented `va_endpoints` row asymmetry (embedded asset inside finding row has `os_type/os_version/first_seen/last_seen/ip_address/mac_address/tags` and `domain`-derived `fqdn` null) is reproduced by the probe because the source dataset is the same and the mapper is identical. Confirmed in `assumptions.md` A5.

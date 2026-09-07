# Constraints

## Scope
- Refactor `CollectFindingsAsync` only. `CollectAssetsAsync` and `fetchFindingIdsByEndpointIdAsync` stay as-is.
- The known coherence gap between asset `finding_ids[]` (forward join) and finding ids emitted by the new flow (reverse join) is explicitly accepted out of scope. Log as a known asymmetry.
- Repo-local edits only inside `/Users/user/Dev/AgentService`. The probe and dbMigrations repos are reference only.

## Architecture
- Mirror `DefenderVmCollector`'s in-flight pattern: Phase 1 dumps intermediates to a per-collection temp dir; Phase 2 reads them back, enriches, and writes hydrated rows.
- Write both `cves.jsonl` (va_cves bank) and `endpoints.jsonl` (raw `/get_endpoint` rows) to a per-collection temp directory under the same parent dir as the result `StreamWriter`. Mirror `DefenderVmCollector.GetOutputDirectoryFromStreamWriter` with `CustomPaths.CymulateExcludedTempFilesDir` as fallback.
- Build `cvesByHost: Dictionary<string, List<JObject>>` in memory, keyed on hostname (case-insensitive), from `va_cves.affected_hosts[]`. Match each endpoint by `endpoint_name`. ~75 MB heap worst case at the 50k cap — acceptable.
- Drop the `/xql/get_datasets` readiness gate. Trust the XQL response status (`SUCCESS` / `FAIL`).
- Delete helpers rendered unreachable by the refactor (`isDatasetReadyForQuery`, `findDataset`, `isDatasetExplicitlyEmpty`, plus any other unreachable). Helpers still used by `CollectAssetsAsync` stay.

## Queries
- XQL projection (va_cves), exact column list: `cve_id, name, description, severity, severity_score, affected_hosts, affected_products, publication_date, modification_date, exploitability_score, impact_score, type, is_excluded`.
- XQL `limit = cMaxXqlFindings` (50_000) in normal runs, `1` in dry-run.
- Do NOT apply a time filter to the va_cves XQL (`modification_date` is metadata-revision-date, not first-observed).
- Endpoints fetched via `PaloAltoCortexApiBase.FetchEndpointsAsync(apiInfo, baseDate, ...)` with `baseDate = iIsDryRun ? DateTime.UtcNow : iBaseDate`. Pagination stops at `last_seen < baseDate` (same as `CollectAssetsAsync`).

## Emitted row shape
- Byte-faithful to `/Users/user/Dev/Uri/localprojects/IntegrationProbes/ProbeResults/CortexXdr_20260514_100608_494Z/emitted_findings.json`. Key order, field names, types, null/empty semantics, all preserved.
- Port the probe mapper logic into a collector-local helper class (e.g. `CortexXdrFindingsMapper`): `MapAsset`, `MapVulnerabilityFromCve`, `BuildCvesByHost`, `BuildFindingId`, `ResolveCveName`. AgentService collector uses Newtonsoft `JObject`/`JArray`; the probe used `System.Text.Json.Nodes` only because it lacked the Newtonsoft dependency.
- `finding_id` format: `{instanceId|"cortex-xdr"}:{endpoint_id}:{CVE-YYYY-NNNN}`. CVE name comes from `va_cves.name` with `va_cves.cve_id` as fallback. Same prefix-fallback rule as today.
- Per-vulnerability mapping:
  - `first_seen` ← `va_cves.publication_date`
  - `last_seen` ← `va_cves.modification_date`
  - `description` ← `va_cves.description`
  - `severity` ← first-non-empty of `va_cves.severity` then `va_cves.severity_score`
  - `mitigation` null
  - `status` `"open"`
  - `type` `"vulnerability"`
  - `cve_ids` 1-element array `[cveName]`
- Embedded asset inside each emitted row built via `MapAsset` from the raw `/get_endpoint` row, with the per-endpoint `finding_ids` array (list of finding ids for that endpoint's matched CVEs). This makes the embedded asset richer than the prior collector's (which was sourced from the 5-field va_endpoints projection).

## Persistence and batching
- Keep `SupportsBatchUpload = true`.
- Use existing `rBatchUploader` / `UploadFindingsBatchAsync` when `instanceId` is set, batching at `cBatchUploadSize = 1000`. Match current behavior.

## CollectionStats
- `TotalAssets = 0`.
- `TotalVulnerabilities` = sum of CVE-level vulnerability items emitted across all `{asset, vulnerabilities[]}` rows.
- `CollectionDuration` filled.

## Temp-dir lifecycle
- Do NOT manually delete the temp dir. `CybiActionManager.DeleteCollectedDataBeforeZipping` owns cleanup. Match Defender behavior.

## Empty-tenant behavior
- Tenants without Host Insights (va_cves returns 0 rows): log info, emit zero findings, do not throw.
- Tenants where XQL succeeds but returns no rows: same — log info, emit zero, do not throw.

## Dependencies and worktree
- No new NuGet packages. No `.csproj` changes beyond what's already in the worktree.
- Preserve the existing local modification to `Source/CybiCollectors/CortexXdrCollector/CortexXdrCollector.csproj`.

## Docs
- Update `CortexXdrCollector/Examples/README.md` "Known asymmetry" section to reflect the now-richer embedded asset (or surface this via `execution_notes.md` if the executor decides the doc update is out-of-scope).

## Code style
- No comments documenting the refactor in code. Comments only when WHY is non-obvious.

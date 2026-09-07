# Prompt Contract — Cortex XDR findings flow refactor (reverse join)

## Role
You are a senior .NET integrations engineer working in the AgentService codebase, refactoring the Cortex XDR collector's findings flow to mirror the established Defender VM in-flight collection pattern.

## Goal
Refactor `CortexXdrCollector.CollectFindingsAsync` (and only that flow) to:

1. Drop the `/xql/get_datasets` readiness gate.
2. Replace the forward join (`va_endpoints.cves[]`) with a reverse join from `va_cves.affected_hosts[]` → `endpoint.endpoint_name` (case-insensitive).
3. Emit hydrated `{asset, vulnerabilities[]}` rows whose shape is byte-faithful to the probe's `emitted_findings.json` contract.
4. Use a Defender-style in-flight architecture: dump intermediates to a per-collection temp dir, then enrich + emit in Phase 2.

`CollectAssetsAsync` must remain untouched. The known coherence gap between asset `finding_ids[]` (forward join) and the new finding ids (reverse join) is explicitly out of scope.

## Context

### Repo and branch
- Repo: `/Users/user/Dev/AgentService` (NOT dbMigrations — the working terminal was launched in the wrong repo by mistake; do all work in AgentService).
- Branch: `cortex-assets-and-findings` (HEAD `68ef31c89 cortex xdr assets and findings`).
- Worktree-modified file you must preserve as-is: `Source/CybiCollectors/CortexXdrCollector/CortexXdrCollector.csproj`.

### Required reading (cite verbatim — execute in this order)
1. `/Users/user/Dev/AgentService/Source/CybiCollectors/DefenderVmCollector/DefenderVmCollector.cs` — the in-flight collection→enrichment pattern to mirror (Phase 1 temp-file dump, Phase 2 enrich & batch-upload). Especially: `SetupCollectionAsync`, `CollectDataToTempFilesAsync`, `EnrichAndWriteResultsAsync`, `GetOutputDirectoryFromStreamWriter`.
2. `/Users/user/Dev/Uri/localprojects/IntegrationProbes/Integrations/CortexXdr/CortexXdrEmitMapper.cs` — byte-faithful mapper functions to port: `MapAsset`, `MapVulnerabilityFromCve`, `BuildCvesByHost`, `BuildFindingId`, `ResolveCveName`. Output shape and field semantics must match.
3. `/Users/user/Dev/Uri/localprojects/IntegrationProbes/Integrations/CortexXdr/CortexXdrProbeRunner.cs` — the three-stage reference pipeline (Stage 1 endpoints, Stage 2 va_cves, Stage 3 disk correlate → emit). Production uses the same logic in-flight, not in three disk-bounded stages.
4. `/Users/user/Dev/Uri/localprojects/IntegrationProbes/ProbeResults/CortexXdr_20260514_100608_494Z/emitted_findings.json` — the contract for the emitted row shape. Asset key order, field semantics, vulnerabilities array layout. Do not deviate.
5. `/Users/user/Dev/Uri/localprojects/IntegrationProbes/ai/active/2026-05-14_0500_align-cortexxdr-probe-with-collector-output/execution_notes.md` — the why. Final architecture (lines 72-119), readiness-gate divergence (lines 21-35), reverse-join switch (lines 37-70).
6. `/Users/user/Dev/AgentService/Source/Application/Cymulate.Agent.Application.Actions/Actions/QueryIntegration/Logic/Clients/BaseApiClients/PaloAltoNetworks/PaloAltoCortexApiBase.cs` — already provides `FetchEndpointsAsync` (paginated, last_seen DESC, stops at `last_seen < baseDate`, with `onEndpointAsync` streaming callback), `FetchXqlResultsAsync`, `setRequestAuthInfo`. Reuse these; do not reimplement.
7. `/Users/user/Dev/AgentService/Source/CybiCollectors/CortexXdrCollector/CortexXdrCollector.cs` — the file being refactored. Existing `CollectFindingsAsync` (lines 130-187) and helpers `fetchFindingIdsByEndpointIdAsync`, `buildVaEndpointsQuery`, `isDatasetReadyForQuery`, `findDataset`, `isDatasetExplicitlyEmpty`, `buildCveDetailsMap`, `mapFindingResult`, `mapVulnerability`, `expandCveIds`, `looksLikeCveId`, `tryParseJson` are involved. See Constraints below for what gets removed vs. preserved.

## Constraints

### Scope
- **Findings flow only.** `CollectAssetsAsync` and its `fetchFindingIdsByEndpointIdAsync` helper stay as-is.
- The asset-vs-finding ID-set asymmetry is accepted out of scope (see assumption A4).
- Repo-local edits only inside `/Users/user/Dev/AgentService`.

### Architecture
- Mirror Defender's in-flight pattern. Phase 1 dumps `cves.jsonl` and `endpoints.jsonl` to a per-collection temp directory; Phase 2 reads both back and emits hydrated `{asset, vulnerabilities[]}` rows.
- Temp directory location: under the same parent dir as the result `StreamWriter`. Mirror `DefenderVmCollector.GetOutputDirectoryFromStreamWriter` with `CustomPaths.CymulateExcludedTempFilesDir` as fallback.
- Build `cvesByHost: Dictionary<string, List<JObject>>` in memory, keyed on hostname (case-insensitive), from `va_cves.affected_hosts[]`. Match each endpoint by `endpoint_name`.
- Memory ceiling: worst case ≈ 50k × ~1.5 KB ≈ 75 MB. Acceptable per operator.
- Drop the `/xql/get_datasets` readiness gate entirely. Trust the XQL response status (`SUCCESS` / `FAIL`).
- Remove helpers rendered unreachable: `isDatasetReadyForQuery`, `findDataset`, `isDatasetExplicitlyEmpty`, plus any other helper no longer called by either `CollectAssetsAsync` or the new findings flow. Helpers still used by `CollectAssetsAsync` must stay.

### Queries
- XQL projection (va_cves), exact columns: `cve_id, name, description, severity, severity_score, affected_hosts, affected_products, publication_date, modification_date, exploitability_score, impact_score, type, is_excluded`.
- XQL `limit = cMaxXqlFindings` (50_000) in normal runs, `1` in dry-run.
- Do NOT apply a time filter to the va_cves XQL (`modification_date` is metadata-revision-date, not first-observed).
- Endpoints fetched via `PaloAltoCortexApiBase.FetchEndpointsAsync(apiInfo, baseDate, ...)` with `baseDate = iIsDryRun ? DateTime.UtcNow : iBaseDate`. Pagination stops at `last_seen < baseDate` (same as `CollectAssetsAsync`).

### Emitted row shape
- Byte-faithful to `/Users/user/Dev/Uri/localprojects/IntegrationProbes/ProbeResults/CortexXdr_20260514_100608_494Z/emitted_findings.json`: key order, field names, types, null/empty semantics.
- Port mapper logic into a collector-local helper class (e.g. `CortexXdrFindingsMapper`): `MapAsset`, `MapVulnerabilityFromCve`, `BuildCvesByHost`, `BuildFindingId`, `ResolveCveName`. Use Newtonsoft `JObject`/`JArray` (the probe used `System.Text.Json.Nodes` only because it had no Newtonsoft dependency; the collector already uses Newtonsoft).
- `finding_id` format: `{instanceId|"cortex-xdr"}:{endpoint_id}:{CVE-YYYY-NNNN}`. CVE name comes from `va_cves.name`, fallback to `va_cves.cve_id` only if `name` missing. Same prefix-fallback rule as today.
- Vulnerability fields:
  - `first_seen` ← `va_cves.publication_date`
  - `last_seen` ← `va_cves.modification_date`
  - `description` ← `va_cves.description`
  - `severity` ← first non-empty of `va_cves.severity` then `va_cves.severity_score`
  - `mitigation` null
  - `status` `"open"`
  - `type` `"vulnerability"`
  - `cve_ids` 1-element array `[cveName]`
- Embedded asset built via `MapAsset` from the raw `/get_endpoint` row with the per-endpoint `finding_ids` array (the list of finding ids for that endpoint's matched CVEs). This makes the embedded asset richer than the prior collector's; the prior "Known asymmetry" in `CortexXdrCollector/Examples/README.md` is partially resolved.

### Persistence and batching
- Keep `SupportsBatchUpload = true`.
- Use existing `rBatchUploader` / `UploadFindingsBatchAsync` when `instanceId` is set, batching at `cBatchUploadSize = 1000`. Match current behavior.

### CollectionStats
- `TotalAssets = 0`.
- `TotalVulnerabilities` = sum of CVE-level vulnerability items emitted across all `{asset, vulnerabilities[]}` rows.
- `CollectionDuration` filled.

### Temp-dir lifecycle
- Do NOT manually delete the temp dir. `CybiActionManager.DeleteCollectedDataBeforeZipping` cleans it up. Match Defender behavior.

### Empty-tenant behavior
- Tenants without Host Insights (va_cves returns 0 rows): log info, emit zero findings, do not throw.
- Tenants where XQL succeeds but returns no rows: same — log info, emit zero, do not throw.

### Dependencies and worktree
- No new NuGet packages. No `.csproj` changes beyond what's already in the worktree.
- Preserve the existing local modification to `Source/CybiCollectors/CortexXdrCollector/CortexXdrCollector.csproj`.

### Docs
- Update `CortexXdrCollector/Examples/README.md` "Known asymmetry" section to reflect the now-richer embedded asset (or surface this via `execution_notes.md` if the executor decides the doc update is out-of-scope).

### Code style
- No comments documenting the refactor in code. Comments only when WHY is non-obvious.

## Success Criteria

- `CollectFindingsAsync` no longer calls `/xql/get_datasets`.
- `CollectFindingsAsync` no longer issues an XQL query against `va_endpoints`.
- `CollectFindingsAsync` issues one XQL query against `va_cves` with the full 13-field projection (limit 50_000 / 1 in dry-run), and one paginated `/endpoints/get_endpoint` walk with `baseDate` cutoff.
- Both intermediates (`cves.jsonl`, `endpoints.jsonl`) are written to a per-collection temp directory derived from the output `StreamWriter` (Defender pattern); the result `StreamWriter` receives hydrated `{asset, vulnerabilities[]}` rows.
- Emitted row shape is byte-identical to `/Users/user/Dev/Uri/localprojects/IntegrationProbes/ProbeResults/CortexXdr_20260514_100608_494Z/emitted_findings.json` modulo data values (key order, field names, types, null/empty semantics all match).
- `finding_id` format and `CollectionStats.TotalVulnerabilities` count are as specified.
- `CollectAssetsAsync` is untouched.
- Helpers rendered dead by the refactor (`isDatasetReadyForQuery`, `findDataset`, `isDatasetExplicitlyEmpty`, plus any others no longer called) are deleted. Helpers still used by the asset path are preserved.
- `CortexXdrCollector/Examples/README.md` "Known asymmetry" section is updated (or an `execution_notes.md` entry surfaces this if the doc update is left out of scope).
- Solution builds clean (`dotnet build`) and any existing collector tests pass.

## Execution Rules
- Do not assume missing data.
- Respect constraints strictly.
- Do not modify `CollectAssetsAsync` or `fetchFindingIdsByEndpointIdAsync`.
- Do not modify `CortexXdrCollector.csproj` beyond the existing worktree change.
- Do not introduce new NuGet packages.
- Do not delete helpers still called by the unchanged asset path.
- Do not add code comments describing the refactor itself.

## Output Format
- Code changes inside `/Users/user/Dev/AgentService/Source/CybiCollectors/CortexXdrCollector/` (the refactored `CortexXdrCollector.cs` plus the new mapper helper file).
- Documentation update inside `/Users/user/Dev/AgentService/Source/CybiCollectors/CortexXdrCollector/Examples/README.md` (or an `execution_notes.md` entry deferring it).
- `execution_notes.md` in the task directory recording: dead helpers removed, deviations (if any) from the contract, memory ceiling verification status for `cvesByHost`, doc update status.
- Updated `state.json` reflecting completion.

## Stop Conditions
- Stop and surface if Cortex API base signatures have drifted from what's referenced above (e.g. `FetchEndpointsAsync` no longer accepts `onEndpointAsync`).
- Stop if `dotnet build` cannot be run locally (e.g. SDK missing).
- Stop if removing a helper would break a path outside `CortexXdrCollector` (cross-collector reuse).
- Stop if the existing csproj modification in the worktree conflicts with the refactor.
- Stop when the goal is achieved.
- Stop when required data is missing.

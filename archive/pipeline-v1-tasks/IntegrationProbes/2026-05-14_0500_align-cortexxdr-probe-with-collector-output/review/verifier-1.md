# Verifier — pass 1

## Verdict
PASS

## Findings

- [A] **Field-for-field asset parity** — PASS.
  - `CortexXdrEmitMapper.MapAsset` at `/Users/user/Dev/Uri/localprojects/IntegrationProbes/Integrations/CortexXdr/CortexXdrEmitMapper.cs:18-35` lists keys in the exact order of `CortexXdrCollector.MapAsset` at `/Users/user/Dev/AgentService/Source/CybiCollectors/CortexXdrCollector/CortexXdrCollector.cs:248-265`: client_id, instance_id, type, aid, value, os_type, os_version, first_seen, last_seen, finding_ids, ip_address, mac_address, tags, os_build, fqdn.
  - `type` defaults to `"endpoint"` when `endpoint_type` is missing (probe `CortexXdrEmitMapper.cs:16` `AsString(endpoint["endpoint_type"]) ?? "endpoint"`; collector `CortexXdrCollector.cs:252` `iEndpoint["endpoint_type"]?.ToString() ?? "endpoint"`). Empty-string `endpoint_type` is collapsed to JSON null in both because both apply `NullableString` after the `??`.
  - `finding_ids` is a fresh array — `CortexXdrEmitMapper.cs:29` `findingIds.DeepClone().AsArray()` (collector uses `cloneArray` at `CortexXdrCollector.cs:259`). Caller paths also detach: `CortexXdrProbeRunner.LookupFindingIds` at `CortexXdrProbeRunner.cs:534` returns a `DetachArray` copy, and `MapFindingResult` allocates a fresh `JsonArray` at `CortexXdrEmitMapper.cs:46` before passing to `MapAsset`.
  - `tags` clones `endpoint.tags[]` items and appends `group_name` if non-empty (`CortexXdrEmitMapper.cs:198-216`), matching `CortexXdrCollector.cs:665-678`.
  - `fqdn` derivation matches `buildFqdn` precisely (`CortexXdrEmitMapper.cs:218-236` vs `CortexXdrCollector.cs:696-714`): null endpoint_name → null; missing/no-dot domain → endpoint_name; already-suffixed → endpoint_name; otherwise concatenate.
  - `os_build` is `NullableString(osVersion)` where `osVersion = AsString(endpoint["os_version"])` (`CortexXdrEmitMapper.cs:15,33`), mirroring `CortexXdrCollector.cs:246,263`. For numeric `os_version`, the probe’s `AsString` falls back to `element.GetRawText()` (`CortexXdrEmitMapper.cs:383`), producing the same decimal text Newtonsoft’s `JToken.ToString()` returns.

- [B] **Finding shape parity** — PASS.
  - Outer shape `{asset, vulnerabilities[]}` at `CortexXdrEmitMapper.cs:59-63`, matching `CortexXdrCollector.cs:496-500`.
  - Vulnerability key order at `CortexXdrEmitMapper.cs:80-95` matches `CortexXdrCollector.cs:515-530`: id, client_id, instance_id, name, display_name, type, severity, mitigation, status, first_seen, last_seen, cve_ids, description.
  - Constants: `"type": "vulnerability"` (line 87), `"mitigation": null` (line 89), `"status": "open"` (line 90), `"first_seen": null` (line 91), `"last_seen": null` (line 92), `"cve_ids": [cveId]` (line 93). `System.Text.Json.Nodes.JsonObject` serialises a C# `null` slot as JSON `null`, which is byte-equivalent to `JValue.CreateNull()`.
  - Severity fallback chain `endpointRow["severity"] → cveDetails["severity"] → endpointRow["severity_score"] → cveDetails["severity_score"]` at `CortexXdrEmitMapper.cs:74-78` matches `CortexXdrCollector.cs:510-513`. `IsEmptyToken` (`CortexXdrEmitMapper.cs:298-330`) replicates the collector’s `isEmptyToken` (`CortexXdrCollector.cs:761-769`): null, JSON-null, empty string/whitespace, empty object/array.
  - `description` is sourced only from `cveDetails?["description"]` (`CortexXdrEmitMapper.cs:94`).

- [C] **Finding-id format** — PASS.
  - `CortexXdrEmitMapper.BuildFindingId` at `/Users/user/Dev/Uri/localprojects/IntegrationProbes/Integrations/CortexXdr/CortexXdrEmitMapper.cs:98-102` produces `{prefix}:{endpointId}:{cveId}` where `prefix = "cortex-xdr"` when instanceId is null/whitespace, otherwise `instanceId`. Identical to `CortexXdrCollector.cs:659-663`.

- [D] **ExpandCveIds semantics** — PASS.
  - `CortexXdrEmitMapper.ExpandCveIds` at `/Users/user/Dev/Uri/localprojects/IntegrationProbes/Integrations/CortexXdr/CortexXdrEmitMapper.cs:115-177` handles: null token, JsonArray recursion, JsonObject property names matching CVE pattern plus recursion into values, string with leading `[`/`{` parsed via `TryParseJson`, and split on `, ; whitespace \n \r \t` with `looksLikeCveId` filter. Matches `CortexXdrCollector.cs:533-595` step for step. `LooksLikeCveId` is the case-insensitive `CVE-` prefix check at `CortexXdrEmitMapper.cs:352-353`.

- [E] **Dataset readiness gate** — PASS.
  - `CortexXdrEmitMapper.IsDatasetReadyForQuery`/`IsDatasetExplicitlyEmpty` (`CortexXdrEmitMapper.cs:104-113, 262-283`) mirror the collector helpers (`CortexXdrCollector.cs:329-377`).
  - Asset-emit path: `CortexXdrProbeRunner.BuildFindingIdsByEndpointIdAsync` calls `IsDatasetReadyForQuery` at `CortexXdrProbeRunner.cs:171` before issuing the va_endpoints XQL at `CortexXdrProbeRunner.cs:177`. When not ready, it returns an empty map without issuing the query. (Per collector behaviour at `CortexXdrCollector.cs:278`, asset emission itself does not short-circuit; only the finding-id enrichment does. The probe mirrors that.)
  - Finding-emit path: `CortexXdrProbeRunner.ProbeEmitFindingsAsync` short-circuits at `CortexXdrProbeRunner.cs:318` before issuing any XQL. Identical to collector `CortexXdrCollector.cs:145`.

- [F] **XQL projection equality** — PASS.
  - Probe `BuildVaEndpointsQuery` (`/Users/user/Dev/Uri/localprojects/IntegrationProbes/Integrations/CortexXdr/CortexXdrProbeRunner.cs:548-549`) emits `dataset = va_endpoints\n| fields endpoint_id, endpoint_name, cves, severity, severity_score\n| limit {limit}`. Collector `buildVaEndpointsQuery` (`CortexXdrCollector.cs:379-386`) emits the same text from a raw-string literal with the same line breaks and identical field list. `limit` substitution drives off the probe’s `QueryLimit` (default 10).
  - Probe `BuildVaCvesQuery` (`CortexXdrProbeRunner.cs:551-552`) matches collector `buildVaCvesQuery` (`CortexXdrCollector.cs:388-395`): `dataset = va_cves | fields cve_id, description, severity, severity_score | limit N`.

- [G] **Configuration plumbing** — PASS.
  - `CortexXdrProbeConfiguration.InstanceId` and `ClientId` are optional `string?` (`/Users/user/Dev/Uri/localprojects/IntegrationProbes/Integrations/CortexXdr/CortexXdrProbeConfiguration.cs:15-17`). `Validate()` does not require them.
  - Builder reads `cymulate_instanceId`, `cymulate_clientId`, `client_id` (`/Users/user/Dev/Uri/localprojects/IntegrationProbes/Integrations/CortexXdr/CortexXdrProbeConfigurationBuilder.cs:12-13`).
  - Lab reads `CYMULATE_INSTANCE_ID` and `CYMULATE_CLIENT_ID` via `GetEnvironmentValue` (no throw) and only adds them when non-empty (`/Users/user/Dev/Uri/localprojects/IntegrationProbes/Integrations/CortexXdr/CortexXdrLabConfiguration.cs:18-28`).

- [H] **Discovery probes unchanged** — PASS.
  - `ProbeEndpointAssetAsync` at `CortexXdrProbeRunner.cs:51-72` still POSTs `/endpoints/get_endpoint` with `search_to: 1` and emits both `POST /endpoints/get_endpoint` and `Asset contract fields from /endpoints/get_endpoint` items.
  - `ProbeDatasetInventoryAsync` at `CortexXdrProbeRunner.cs:74-83` unchanged.
  - `BuildDiscoveryQueries` at `CortexXdrProbeRunner.cs:673-701` still yields 14 entries (verified via `grep -c "yield return" CortexXdrProbeRunner.cs` → 14).

- [I] **Sidecar wiring** — PASS.
  - `Program.cs:11` passes `archive.DirectoryPath` into `ExecuteAsync`; `Program.cs:73` forwards it to `CortexXdrProbeActivation.Runner.ExecuteAllAsync(archiveDirectory, ...)`.
  - Runner writes `emitted_assets.jsonl` at `CortexXdrProbeRunner.cs:280` under `archiveDirectory` and `emitted_findings.jsonl` at `CortexXdrProbeRunner.cs:369`.
  - Both `ProbeRunItem.Body` strings are built by `BuildEmitBody` (`CortexXdrProbeRunner.cs:554-573`) which prefixes `Sidecar: {sidecarPath}`.

- [J] **No new package references** — PASS.
  - `/Users/user/Dev/Uri/localprojects/IntegrationProbes/IntegrationProbes.csproj` has only `Microsoft.Extensions.Hosting 10.0.5` and `Microsoft.Extensions.Logging.Console 10.0.5`. No Newtonsoft.Json.

- [K] **No edits in AgentService tree** — PASS.
  - `find /Users/user/Dev/AgentService/Source/CybiCollectors/CortexXdrCollector -newer <prompt_contract.md> -type f` returns no results.
  - `stat -f '%Sm' CortexXdrCollector.cs` reports `May 13 15:51:20 2026`, predating the prompt contract created on May 14.

- [L] **Build cleanly** — PASS.
  - `dotnet build /Users/user/Dev/Uri/localprojects/IntegrationProbes/IntegrationProbes.csproj` → `Build succeeded. 0 Warning(s) 0 Error(s)`.

## Build output

```
Determining projects to restore...
  All projects are up-to-date for restore.
  IntegrationProbes -> /Users/user/Dev/Uri/localprojects/IntegrationProbes/bin/Debug/net8.0/IntegrationProbes.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:00.53
```

## Risks accepted

- A4 (already acknowledged in `assumptions.md`): with `QueryLimit = 10`, the va_endpoints sample may not include the same endpoint returned by `/endpoints/get_endpoint`, so emitted asset `finding_ids` arrays will frequently be empty even when the collector at production scale would populate them. This is documented as expected probe-scale behaviour, not a regression.
- The probe’s `AsString` uses `JsonElement.GetRawText()` for non-string `JsonValue`s. For integers, booleans, and nulls this produces text byte-identical to Newtonsoft’s `JToken.ToString()`. For floating-point numbers that round-trip differently between Newtonsoft (`R` formatting) and `System.Text.Json` (`G17` raw text), `os_build` could theoretically differ. In practice `os_version` is a string in Cortex XDR responses, so this is a latent-only difference and not exercised by real input.

## Required repairs
none

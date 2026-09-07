# Orchestration plan

## Execution path

Direct. Single executor against a single repo. No decomposition required. No research topics — both the collector and the probe are local, fully readable, and the gap analysis is complete.

## Step list

1. **Port mapper** — Create `CortexXdrEmitMapper.cs` with the static helpers ported from the collector: `MapAsset`, `MapFindingResult`, `MapVulnerability`, `ExpandCveIds`, `BuildTags`, `BuildFqdn`, `BuildFindingId`, `IsDatasetReadyForQuery`, `GetFirstNonEmptyToken`, `CloneOrNull`, `NullableString`. Uses `System.Text.Json.Nodes`. Preserves field insertion order from the collector exactly.
2. **Add config** — Extend `CortexXdrProbeConfiguration` with optional `InstanceId` and `ClientId`. Extend `CortexXdrProbeConfigurationBuilder` to read `cymulate_instanceId`, `cymulate_clientId`, `client_id` (collector-compatible keys). Extend `CortexXdrLabConfiguration` to read `CYMULATE_INSTANCE_ID` and `CYMULATE_CLIENT_ID` env vars (both optional, no exception on absence).
3. **Add emit probes** — In `CortexXdrProbeRunner` add `ProbeEmitAssetsAsync` and `ProbeEmitFindingsAsync`. Both gate on `IsDatasetReadyForQuery(va_endpoints)`. Both reuse `StartXqlQueryAsync`/`PollQueryResultsAsync`/`TryFetchStreamResultsAsync`. Findings probe also gates on `va_cves` readiness. Each writes a JSONL sidecar (`emitted_assets.jsonl`, `emitted_findings.jsonl`) under `ProbeRunArchive.DirectoryPath`. Each adds a `ProbeRunItem` whose `Body` summarises the sidecar path and record count.
4. **Plumb archive directory** — Pass `ProbeRunArchive.DirectoryPath` from `Program.ExecuteCortexXdrAsync` through `CortexXdrProbeActivation` into the runner constructor, so the runner knows where to write sidecars.
5. **Build** — `dotnet build /Users/user/Dev/Uri/localprojects/IntegrationProbes/IntegrationProbes.csproj`. Expect zero errors, zero new warnings.
6. **Verifier subagent** — Full context. Validates the emitted JSON shape line-by-line against the collector's mapper and the `Examples/emitted_*_sample.json` files. Writes `review/verifier-1.md`.
7. **Code-reviewer subagent** — Isolated context. No prompt contract, no verifier output, no user request. Reviews only the code diff for quality. Writes `review/code-reviewer-1.md`.

## Files touched (final list)

- `Integrations/CortexXdr/CortexXdrEmitMapper.cs` — new
- `Integrations/CortexXdr/CortexXdrProbeConfiguration.cs`
- `Integrations/CortexXdr/CortexXdrProbeConfigurationBuilder.cs`
- `Integrations/CortexXdr/CortexXdrLabConfiguration.cs`
- `Integrations/CortexXdr/CortexXdrProbeRunner.cs`
- `Activation/CortexXdrProbeActivation.cs` (constructor plumbing)
- `Program.cs` (pass archive directory into activation/runner)

## Out of scope

S3 batch upload, full pagination of `/endpoints/get_endpoint`, 50 000-row XQL limit, AgentService source edits.

Role:
You are a senior .NET engineer who has read both `CortexXdrCollector.cs` and the current `IntegrationProbes/Integrations/CortexXdr/*` files. You are porting collector mapping logic into the probe without touching the AgentService source.

Goal:
Extend the Cortex XDR probe so that, in addition to its existing discovery output, it emits per-asset and per-finding JSON records identical in shape (field order, null shape, finding-id format, CVE expansion) to the records `CortexXdrCollector.CollectAssetsAsync` and `CollectFindingsAsync` write today.

Context:
- Collector: `/Users/user/Dev/AgentService/Source/CybiCollectors/CortexXdrCollector/CortexXdrCollector.cs`.
- Probe runner: `/Users/user/Dev/Uri/localprojects/IntegrationProbes/Integrations/CortexXdr/CortexXdrProbeRunner.cs`.
- Reference samples: `CortexXdrCollector/Examples/emitted_asset_sample.json`, `emitted_finding_sample.json`.
- The probe already authenticates correctly (advanced SHA256 + `x-xdr-timestamp/nonce/auth-id`), already paginates XQL via start/poll/stream, and already has dataset inventory probing.

Constraints:
See `constraints.md`.

Success Criteria:
- Running `dotnet run -- cortex-xdr` produces, in the archive directory, two sidecar JSONL files (assets and findings) whose lines match the collector's output shape byte-for-byte for the same input data.
- Solution builds cleanly (`dotnet build`, no errors, no new warnings).
- Existing discovery probe results (`POST /endpoints/get_endpoint`, `POST /xql/get_datasets`, the 14 XQL queries, the asset-contract field summary, the dataset inventory summary) are unchanged.
- `CortexXdrProbeConfiguration` exposes optional `InstanceId` and `ClientId`; builder reads `cymulate_instanceId`, `cymulate_clientId`/`client_id`; lab config exposes `CYMULATE_INSTANCE_ID` and `CYMULATE_CLIENT_ID` (both optional).
- No new package references.
- No edits in the AgentService source tree.

Execution Rules:
- Do not assume missing data — fall back to collector defaults (`null` instance, `"cortex-xdr"` prefix).
- Respect constraints strictly.
- Do not refactor untouched probe code.

Output Format:
Code edits across:
- `Integrations/CortexXdr/CortexXdrProbeConfiguration.cs`
- `Integrations/CortexXdr/CortexXdrProbeConfigurationBuilder.cs`
- `Integrations/CortexXdr/CortexXdrLabConfiguration.cs`
- `Integrations/CortexXdr/CortexXdrProbeRunner.cs`
New file:
- `Integrations/CortexXdr/CortexXdrEmitMapper.cs`

Stop Conditions:
- Goal achieved (build green, both emit probes produce sidecar files of the expected shape).
- Build fails or a constraint cannot be satisfied without a deviation from `decisions.md`.

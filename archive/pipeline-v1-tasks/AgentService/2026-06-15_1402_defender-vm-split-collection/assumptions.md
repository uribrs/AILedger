# Assumptions

- A1 (OPEN): The adapter (`cymulate-integration-adapters`) DefenderVmCollector is the exact behavioral source of truth for stage order, URL building, and row shapes. To be VALIDATED by reading `DefenderVmRecordFormatter.cs`, `DefenderVmFindingsFlow.cs`, `DefenderVmUrlBuilder.cs` during execution.
- A2 (OPEN): The AgentService DefenderVmCollector currently exposes the upload primitive `SaveUploadAndDeleteBatchAsync` and a per-collector batch accumulation pattern comparable to CortexXdrCollector. To be VALIDATED by reading the existing collector source.
- A3 (OPEN): The adapter does NOT call the vulnerability-names endpoint, so AgentService should drop `VulnerabilityNamesCollectionService` from the findings path. To be VALIDATED against the adapter flow.
- A4 (OPEN): The Defender VM split parser pre-process is factored such that it can be reused/extracted by the Endpoint parser without changing Defender VM output. To be VALIDATED by reading `defenderVmAssetsFindings.py` / `DefenderVmAssetsFindingsNotHydrated.py` and `input_resolver.py`.
- A5 (OPEN): `defender-endpoint-assets-and-findings` requires the same split selection semantics (both `assets*.json` and `findings*.json` present → split mode) already used for `defender-vm-assets-findings`.
- A6 (VALIDATED): This is a code-bearing task spanning C# and Python; verifier + code-reviewer passes are required. Source: plan acceptance criteria + test requirements.
- A7 (OPEN): Both repos build/test locally with their standard toolchains (`dotnet test` for AgentService; pytest for parsers). To be VALIDATED at verification time.

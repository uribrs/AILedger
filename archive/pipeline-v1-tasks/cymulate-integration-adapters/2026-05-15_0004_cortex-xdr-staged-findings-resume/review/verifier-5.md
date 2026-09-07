# Verifier 5 - Cortex XDR staged findings resume

Result: PASS

I verified the current working tree after the code-reviewer-2 repairs against the original Cortex XDR staged findings/resume contract. I found no blocking product-code issues.

## Scope Reviewed

- `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Flows/Findings/CortexXdrFindingsFlow.cs`
- `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Recovery/CortexXdrCheckpointHelper.cs`
- `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Recovery/CortexXdrFindingsCheckpointState.cs`
- `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Recovery/CortexXdrResumeRunner.cs`
- `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/CortexXdrCollector.cs`
- `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Processing/CortexXdrCollectorFlowRunner.cs`
- `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Processing/Configuration/CortexXdrIdentification.cs`
- Cortex XDR collector unit tests covering findings flow, checkpoint parsing, and resume runner behavior.

## Repair Verification

- Fixed-size CVE tie-breaker: PASS. CVE replay ordering uses vendor-side `sort asc cve_id, name`, then client-side `cve_id`, `name`, and a SHA-256 selected-field tie-breaker instead of caching full selected-field strings. The duplicate-key resume regression covers this path.
- Versioned staged checkpoint parsing: PASS. Current staged findings checkpoints persist `checkpointVersion=2`, `stage`, `nextCveIndex`, separate counters, and separate output page counters. Parsing rejects legacy joined checkpoints without `stage`, rejects unsupported versions, and requires `totalFindingsCollected`, `totalAssetsCollected`, `assetsPage`, `findingsPage`, and `nextCveIndex` to be non-negative.
- Terminal empty endpoint page checkpoint: PASS. When the endpoint stage receives an empty terminal page, the flow writes a terminal `stage=assets`, `hasMorePages=false` state and emits a checkpoint event without advancing asset file counters for an unpublished empty batch.
- Resume result totals: PASS. Findings resume now preserves staged checkpoint state and uses final flow totals for `totalAssetsCollected` and `totalFindingsCollected`.

## Contract Coverage

- No adapter-side CVE/endpoint hydration remains in the findings flow; CVE rows are published as `findings_*.json` and endpoint rows as `assets_*.json`.
- CVE-stage resume re-runs the sorted XQL query and continues from `NextCveIndex`, matching the documented task decision for the lack of public XQL offset paging.
- Endpoint-stage resume skips XQL and resumes `/endpoints/get_endpoint` using `search_from/search_to`, preserving independent asset page numbering.
- Metadata now advertises both assets and findings support and describes the staged upstream-hydration behavior.
- `CanResumeFrom` and `ResumeAsync` remain routed through the Shared recovery scaffold and re-enter the real Cortex XDR flow.

## Evidence References

- Staged CVE and endpoint flow: `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Flows/Findings/CortexXdrFindingsFlow.cs:95`
- CVE publish/checkpoint transition: `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Flows/Findings/CortexXdrFindingsFlow.cs:192`
- Terminal empty endpoint checkpoint: `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Flows/Findings/CortexXdrFindingsFlow.cs:286`
- SHA-256 tie-breaker: `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Flows/Findings/CortexXdrFindingsFlow.cs:344`
- Versioned staged checkpoint parsing: `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Recovery/CortexXdrCheckpointHelper.cs:51`
- Required staged counters: `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Recovery/CortexXdrCheckpointHelper.cs:211`
- Resume result totals: `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Recovery/CortexXdrResumeRunner.cs:106`
- Findings-stage resume test: `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test/CortexXdrFindingsFlowTests.cs:276`
- Duplicate-key tie-breaker test: `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test/CortexXdrFindingsFlowTests.cs:360`
- Terminal empty endpoint checkpoint test: `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test/CortexXdrFindingsFlowTests.cs:518`
- Missing staged counter rejection test: `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test/CortexXdrCheckpointHelperTests.cs:189`
- Resume runner staged totals test: `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test/CortexXdrResumeRunnerTests.cs:15`

## Verification Commands

- `dotnet test src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false`
  - PASS: 25/25 tests.
- `dotnet build src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false`
  - PASS: build succeeded.
- `git diff --check`
  - PASS: no whitespace errors.

Both dotnet commands still emit only `NU1900` warnings because vulnerability metadata cannot be loaded from the configured CodeArtifact package source.

## Remaining Risks

- CVE-stage resume intentionally depends on the replayed sorted `va_cves` result set remaining stable for the same selected fields. This is the accepted tradeoff because public Cortex XDR XQL result APIs do not expose documented offset paging.
- The XQL query is capped at `limit 50000`; tenants with more CVE rows beyond that cap still need a future vendor-supported segmentation strategy or an explicit product decision.
- Verification is targeted to the Cortex XDR collector project and unit tests, not a full repository test sweep.

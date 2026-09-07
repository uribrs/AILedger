# Verifier 4: Cortex XDR Staged Findings Resume

Result: PASS

I verified the current working tree against the original staged findings/resume contract and the verifier-3/code-reviewer-1 repair list. I found no blocking product-code issues in this pass.

## Contract Coverage

- No adapter-side hydration/join: PASS. `CortexXdrFindingsFlow` publishes raw `va_cves` XQL rows and raw endpoint rows separately; the happy-path test asserts findings output has no `asset` or `vulnerability` joined wrapper.
- CVEs as chunked `findings_*.json`: PASS. CVE rows are emitted through the findings page publisher with findings page numbering, and tests assert `findings_000001.json` plus resumed `findings_000002.json`/`findings_000003.json` output.
- Endpoints as chunked `assets_*.json`: PASS. The findings flow uses the assets page publisher for `/endpoints/get_endpoint` rows, and tests assert `assets_000001.json` and resumed asset page numbering.
- Staged resume for both datasets: PASS. Checkpoint state now carries `stage`, `nextCveIndex`, `nextSearchFrom`, `findingsPage`, `assetsPage`, and separate asset/finding totals. `ResumeFindingsAsync` re-enters the real findings flow through `CollectorResumeRunner`.
- Vendor segmentation constraints reflected: PASS. Task research rejects undocumented XQL offset paging, accepts sorted XQL replay with adapter-owned row index for CVEs, and uses documented endpoint `search_from`/`search_to` paging for endpoint assets.
- Metadata/wiring: PASS. The collector implements both assets and findings adapters, routes findings resume through the shared recovery path, and metadata describes the staged upstream-hydration behavior.

## Repair Verification

- Cancellation throws instead of returning successful partial results: PASS. The staged loops use explicit `ThrowIfCancellationRequested()` boundaries before/inside/after stages, and `CollectAsync_WhenCancelledBetweenCvePages_ThrowsInsteadOfCompleting` covers the partial-CVE cancellation case.
- CVE deterministic replay ordering: PASS. XQL now sorts by `cve_id, name` before `limit`, and the client applies a selected-field tie-breaker before paging/resume slicing. Duplicate `cve_id`/`name` coverage exists.
- `checkpointVersion=2`: PASS. `SaveFindingsState` writes version `2`, and tests assert the persisted version.
- Legacy joined findings checkpoints rejected: PASS. Findings checkpoints without staged fields are rejected as non-resumable, with test coverage.
- Staged checkpoint/event state: PASS. Tests assert CVE-stage checkpoint metadata, CVE-to-assets transition metadata, final asset state, and persisted progress state.

## Evidence References

- Staged flow and sorted CVE query: `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Flows/Findings/CortexXdrFindingsFlow.cs:48`
- CVE replay, chunk publish, and checkpoint transition: `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Flows/Findings/CortexXdrFindingsFlow.cs:129`
- Endpoint asset paging and checkpointing: `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Flows/Findings/CortexXdrFindingsFlow.cs:257`
- Client-side CVE tie-breaker: `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Flows/Findings/CortexXdrFindingsFlow.cs:321`
- Checkpoint version/staged fields: `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Recovery/CortexXdrCheckpointHelper.cs:51`
- Legacy checkpoint rejection: `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Recovery/CortexXdrCheckpointHelper.cs:209`
- Resume through shared runner: `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Recovery/CortexXdrResumeRunner.cs:72`
- Metadata description: `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Processing/Configuration/CortexXdrIdentification.cs:16`
- Output shape test: `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test/CortexXdrFindingsFlowTests.cs:88`
- Findings-stage resume test: `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test/CortexXdrFindingsFlowTests.cs:277`
- Duplicate-key resume ordering test: `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test/CortexXdrFindingsFlowTests.cs:361`
- Cancellation test: `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test/CortexXdrFindingsFlowTests.cs:411`
- Staged checkpoint/event test: `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test/CortexXdrFindingsFlowTests.cs:446`
- Checkpoint version and legacy rejection tests: `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test/CortexXdrCheckpointHelperTests.cs:80`

## Local Verification

- `dotnet test src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false` -> PASS, 23/23.
- `dotnet build src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false` -> PASS.
- Both commands still emit only NU1900 vulnerability metadata warnings from the unreachable CodeArtifact feed.

## Residual Risks

- CVE-stage resume intentionally re-runs sorted XQL and skips by adapter-owned row index because public Cortex XDR XQL result APIs do not expose result offset paging. This is acceptable per the task decision, but it means CVE resume correctness still depends on the replayed result set being stable for the same sorted selected fields.
- The CVE query is capped at `limit 50000`; tenants with more matching `va_cves` rows beyond that cap would need a future vendor-supported segmentation strategy or an explicit product decision.
- Endpoint final-state checkpointing follows the existing Cortex XDR assets-flow count-based pagination pattern. A terminal empty endpoint page after an exact full page does not add a new `hasMorePages=false` checkpoint, but resume from the previous `hasMorePages=true` checkpoint re-enters safely and completes on the empty page.

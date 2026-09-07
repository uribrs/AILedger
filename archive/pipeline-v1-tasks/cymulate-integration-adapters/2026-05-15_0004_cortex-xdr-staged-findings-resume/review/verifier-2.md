# Verifier 2

## Verdict
FAIL

The current product code satisfies the main staged findings behavior: it no longer joins assets and CVEs in the adapter, it publishes XQL CVE rows to `findings_*.json`, it publishes endpoint rows to `assets_*.json`, and findings resume can re-enter either the CVE or endpoint stage. The verifier-1 P1 product bug is repaired: `CortexXdrFindingsFlow` now sorts the CVE XQL query with `| sort asc cve_id, name` before `limit`, and the latest consistency patch makes the final CVE checkpoint event report the same `assets` stage that is saved in checkpoint state.

The verdict remains FAIL because the contract still has concrete closure gaps in test coverage, metadata, and task artifacts.

## Findings

- P2, `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test/CortexXdrFindingsFlowTests.cs`: there is still no CVE-stage resume regression test. The riskiest resume path is `Stage=findings` with `NextCveIndex > 0`, where the flow must re-run sorted XQL, skip already-published CVE rows, continue `findings_*.json` page numbering, and then checkpoint/transition into the assets stage. Current tests cover output shape, no-CVE assets collection, stale endpoint cutoff, assets-stage resume, checkpoint serialization, resume-runner result data, and presence of the XQL sort, but not the row-index resume behavior itself.

- P2, `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Processing/Configuration/CortexXdrIdentification.cs:16`: metadata advertises `FindingsFlow` in `SupportedTopics`, but the description still says this is a collector for endpoint assets via `get_endpoint`. The task contract required supported findings behavior to be advertised intentionally. The description should mention that findings mode publishes Cortex XDR CVE rows and endpoint asset rows separately for upstream hydration.

- P3, `ai/active/2026-05-15_0004_cortex-xdr-staged-findings-resume/state.json`: task state still marks S3-S6 as `pending`, `verification.status` as `not_started`, and `verifierRun`/`codeReviewerRun` as false, even though implementation, tests, verifier-1, repair, and verifier-2 have occurred. This does not affect runtime behavior, but the task artifacts are stale.

## Requirement Coverage

- No adapter-side hydration/join: PASS. The old host/CVE join path is removed from `CortexXdrFindingsFlow`; emitted findings are raw CVE rows and tests assert no `asset`/`vulnerability` wrapper.
- CVEs from XQL into chunked `findings_*.json`: PASS. CVE rows are published through `CortexXdrFindingsPagePublisher`, with deterministic XQL sort before the limit.
- Endpoints into chunked `assets_*.json` during findings flow: PASS. Findings flow uses `CortexXdrAssetsPagePublisher` for endpoint pages and tests assert `assets_000001.json`.
- Upstream-hydration assets collection: PASS. Findings flow collects endpoints even when XQL returns no CVE rows.
- Endpoint-stage resume: PASS. `Stage=assets` skips XQL, resumes from `NextSearchFrom`, and continues `assetsPage`.
- CVE-stage resume design: PASS for product code, TEST GAP for coverage. The implementation re-runs sorted XQL and starts publishing at `NextCveIndex`; no targeted regression verifies that behavior.
- Checkpoints reported for both stages: PASS. Findings checkpoints persist `stage`, `nextCveIndex`, `nextSearchFrom`, `findingsPage`, `assetsPage`, totals, and cursor. Checkpoint events now use `nextStage` at the CVE-to-assets transition and `assets` during endpoint paging.
- Shared recovery wiring: PASS. `CanResumeFrom` validates staged findings checkpoints, and `ResumeAsync` delegates through `CollectorResumeRunner` back into the real flow.
- Vendor segmentation analysis: PASS. Research records that `get_endpoint` supports `search_from/search_to`, while public XQL result APIs lack offset pagination; the accepted CVE resume tradeoff is deterministic re-read plus `NextCveIndex` skip.

## Verification Run

- `dotnet test src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false` -> PASS, 18/18 tests.
- `dotnet build src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false` -> PASS.
- Both commands emitted NU1900 warnings because the configured CodeArtifact vulnerability feed could not be reached; there were no build errors.

## Residual Risks

- XQL CVE retrieval remains capped at 50,000 rows. This appears inherited and aligned with the current query, but it is still a completeness limit if a tenant can exceed that count.
- Endpoint continuation still follows the existing Cortex assets-flow heuristic: continue while the page saw at least `PageSize` endpoints and cutoff was not reached. I did not find a new regression there.

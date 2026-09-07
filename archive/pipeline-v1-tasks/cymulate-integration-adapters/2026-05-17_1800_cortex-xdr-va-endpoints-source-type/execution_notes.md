# Execution Notes

## Summary

Added the `va_endpoints` XQL stage to `CortexXdrFindingsFlow` and stamped a
`sourceType` discriminator on every published row across all three Cortex
findings/assets streams. Bumped the findings checkpoint to `version = 3` with
a new `NextVaEndpointIndex` cursor. Resume from `version = 2` is rejected
as legacy. Pre-existing `CortexXdrFindingsFlowTests` and
`CortexXdrCheckpointHelperTests` updated for the stage rename and sourceType
prefix; eight new tests cover the va_endpoints stage, the sourceType
contract, resume from `FindingsEndpoints`, Host-Insights-missing graceful
skip, v2 rejection, and the `sourceType` collision fail-fast.

## Files changed

### New
- `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Processing/CortexXdrRecordFormatter.cs`
  — internal helper that mirrors `DefenderVmRecordFormatter.AddSourceType`'s
  `ArrayBufferWriter<byte>` + `Utf8JsonWriter` pattern. Stamps `sourceType` as
  the first JSON property of every row. **Collision policy diverges from
  Defender's silent-skip**: if a row already carries a `sourceType` field, the
  formatter throws `InvalidOperationException`. Exposes three constants
  (`SourceTypeVaCves`, `SourceTypeVaEndpoints`, `SourceTypeEndpoint`),
  byte-input `StampFromBytes`, `JsonElement`-input `StampFromElement`, and a
  streaming `StampStream` wrapper used to stamp the XQL byte streams in-flight.

### Modified
- `Flows/Findings/CortexXdrFindingsStage.cs` — `Findings → FindingsCves`,
  added `FindingsEndpoints`, kept `Assets`. String values are lowerCamel:
  `"findingsCves"`, `"findingsEndpoints"`, `"assets"`.
- `Flows/Findings/CortexXdrFindingsFlow.cs` — three-stage state machine
  (`FindingsCves → FindingsEndpoints → Assets`), per-stage cursors
  (`NextCveIndex`, `NextVaEndpointIndex`, `NextSearchFrom`), `va_endpoints`
  XQL query string, dataset-availability probe wired through
  `CortexXdrXqlClient.ProbeDatasetAvailableAsync`, `sourceType` stamping on all
  three streams via `CortexXdrRecordFormatter.StampStream` /
  `StampFromElement`. Probe failure / explicitly-empty dataset gracefully
  skips the `FindingsEndpoints` stage and continues through `Assets`.
- `Processing/Urls/CortexXdrUrls.cs` — added `GetXqlDatasets`.
- `Processing/Xql/CortexXdrXqlClient.cs` — added
  `ProbeDatasetAvailableAsync(string dataset, CancellationToken)` plus
  defensive parsers `TryExtractDatasetArray` and `IsDatasetReadyForQuery` that
  mirror on-prem `PaloAltoCortexApiBase.extractDatasets` and
  `isDatasetReadyForQuery`. Body uses singular `"request":{}` per on-prem.
- `Recovery/CortexXdrFindingsCheckpointState.cs` — added
  `NextVaEndpointIndex` (long) init property.
- `Recovery/CortexXdrCheckpointHelper.cs` — `FindingsCheckpointVersion` bumped
  to `"3"`. `SaveFindingsState` writes `nextVaEndpointIndex`.
  `TryLoadFindingsStateCore` requires `checkpointVersion == "3"` (rejects v2 /
  missing with a clear "start a new collection" log message), accepts the new
  stage triple, and parses `nextVaEndpointIndex` as `long`. Inlined a small
  `TryGetLong` helper rather than touching Shared
  `RecoveryParsingHelper` (constraint: do not modify Shared).
- `UnitTests/.../CortexXdrFindingsFlowTests.cs` — rewrote the test fixture
  to handle the new `get_datasets` endpoint and the second
  `start_xql_query`/`get_query_results` pair, dispatching results-replies by
  inspecting the most-recent `start_xql_query` body (so resume-from-
  FindingsEndpoints tests skip the va_cves response). Updated all
  preserved-assertion tests for the stage rename and the new sourceType
  prefix. Added the following tests:
  - `CollectAsync_DryRun_DoesNotProbeVaEndpointsDataset`
  - `CollectAsync_WhenVaEndpointsDatasetAvailable_PublishesFindingsEndpointsBeforeAssets`
  - `CollectAsync_WhenVaEndpointsDatasetExplicitlyEmpty_SkipsStage_AndContinuesToAssets`
  - `CollectAsync_ResumeFindingsEndpointsStage_SkipsCveStage_AndContinuesByVaEndpointIndex`
  - `CollectAsync_VaEndpointsDatasetAvailable_StampsSourceTypeAsFirstProperty_OnAllThreeStreams`
  - `CollectAsync_WhenVendorRowAlreadyContainsSourceType_FailsFast`
  - (existing) `CollectAsync_HappyPath_*`, `CollectAsync_OverXqlStreamBranch_*`
    updated with sourceType assertions.
- `UnitTests/.../CortexXdrCheckpointHelperTests.cs` — `SaveFindingsState`
  round-trip asserts `checkpointVersion == "3"` and includes
  `NextVaEndpointIndex`. Added:
  - `CanResumeFrom_WithCheckpointVersion2_ReturnsFalse`
  - `CanResumeFrom_WithFindingsEndpointsStage_ReturnsTrue`

## Behavioural notes

- **Malformed-line policy** (stream branch): per-row stamping now parses each
  byte row to inject `sourceType` as the first property, so a malformed line
  surfaces as `JsonException` from `CortexXdrRecordFormatter.StampFromBytes`
  rather than from Egress. This is upstream of Egress and still fails
  fast — it does not regress the "lenient at Ingress, strict at Egress"
  policy because the formatter is a Cortex-local concern, not part of Ingress.
  Updated the existing test
  `CollectAsync_OverXqlStreamBranch_MalformedLine_FailsAtPublisher_NotAtIngress`
  to assert `JsonException` (was `InvalidOperationException`) and a comment
  documents the change.
- **DryRun** preserved: only probes the va_cves XQL path and the endpoints
  REST path. No `get_datasets`, no `va_endpoints` query.
- **Page numbering**: `findingsPage` counts pages across both XQL stages.
  `va_cves` pages appear first under the `findings_*` prefix, then
  `va_endpoints` pages. Upstream routes by `sourceType`, not by file name.

## Assumptions resolved during execution

- VALIDATED: `CortexXdrXqlClient` has no caller other than
  `CortexXdrFindingsFlow`. Pre-flight grep confirmed; safe to extend.
- VALIDATED: Stage rename `Findings → FindingsCves` does not leak into other
  collectors. Pre-flight grep showed `"findings"`/`"assets"` literals in other
  collectors refer to unrelated flow names — none of them are Cortex stage
  string consumers.
- VALIDATED: `xql/get_datasets` body shape is `{"request":{}}` (singular),
  not `{"request_data":{}}`. Cross-checked against on-prem
  `PaloAltoCortexApiBase.FetchXqlDatasetsAsync`. The decisions/constraints
  files were updated mid-execution to reflect this.
- OPEN (carried forward): Upstream consumer tolerance for the additive
  `sourceType` field. Coordination is out-of-band per the operator's framing.

## Risks accepted

- `endpoint_id` sort determinism on `va_endpoints` is inherited from XQL's
  `| sort asc endpoint_id`. No SHA-256 tie-breaker. Same risk-acceptance basis
  as `cve_id` uniqueness on `va_cves`. If duplicates exist and resume drifts,
  upstream dedup is the fallback.
- The new per-row JSON parse in `CortexXdrRecordFormatter` adds one
  `JsonDocument.Parse` per row. The va_cves and va_endpoints stages are
  bounded at 50k rows each by the XQL `| limit`; allocation cost is bounded
  and dominated by the publisher's own per-row work.

## Verification

| Command | Result |
|---|---|
| `dotnet build src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false` | succeeded, 0 warnings, 0 errors |
| `dotnet build src/Cymulate.Integration.Adapters/Cymulate.Integration.Adapters.sln --no-restore --disable-build-servers -p:UseSharedCompilation=false` | succeeded, 0 warnings, 0 errors |
| `dotnet test src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false` | 36/36 passed (CortexXdrFindingsFlowTests: 19 incl. 6 new; CortexXdrCheckpointHelperTests: 9 incl. 2 new; CortexXdrAssetsFlowTests: 6; CortexXdrResumeRunnerTests: 1; CortexXdrCollectorConfigurationBuilderTests: 1) |

## Out-of-scope state observed in the working tree (left untouched)

Pre-existing uncommitted work in the tree from a different effort:
- `src/.../Shared/.../DataPipeline/Egress/Ndjson/NdjsonBatchSession.cs` (M)
- `src/.../Shared/.../DataPipeline/Egress/Ndjson/NdjsonUtf8BatchSession.cs` (M)
- `src/.../Shared/.../DataPipeline/Egress/ResultsBatchPublisher.cs` (M)
- `src/.../Shared/.../DataPipeline/Json/JsonArrayPropertyStreamReader.cs` (M)
- `src/.../Shared/.../DataPipeline/Json/TopLevelJsonStreamArrayReader.cs` (M)
- `src/.../Shared/.../Session/SessionTelemetry.cs` (M)
- `src/.../Shared/.../DataPipeline/Telemetry/` (untracked dir)
- `src/.../Shared/.../Exceptions/DataPipelineException.cs` (untracked)

Per repo rules these were left untouched. The commit stages only the
Cortex-collector and Cortex-test files plus the new
`CortexXdrRecordFormatter.cs`.

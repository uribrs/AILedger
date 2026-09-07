# Code Reviewer 3 - Focused Technical Re-review

## Scope

Reviewed current working-tree changes for high-risk Cortex XDR production collector recovery/publishing logic and tests:

- `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Flows/Findings/CortexXdrFindingsFlow.cs`
- `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Recovery/CortexXdrCheckpointHelper.cs`
- `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test/CortexXdrFindingsFlowTests.cs`
- `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test/CortexXdrCheckpointHelperTests.cs`

Review lens: technical safety only for C#/.NET 8 collector recovery, publishing, checkpoint parsing, paging counters, cancellation, and runtime behavior. The accepted tradeoff remains that XQL resume re-runs the sorted query and skips by persisted index because no documented vendor offset paging is available.

## Findings

No blocking or major technical safety findings found in the focused re-review.

## Requested Issue Re-check

### CVE tie-breaker allocation / memory behavior

Resolved for the prior major concern.

`CortexXdrFindingsFlow` now uses `IncrementalHash` in `BuildCveTieBreakerHash` rather than constructing one large composite tie-breaker string per row. LINQ sorting still materializes one final SHA-256 hex key per row, which is expected for deterministic in-memory ordering. Some transient per-field encoding remains, especially for non-string JSON values serialized by `GetJsonSortValue`, but with the explicit 50,000-row cap this is no longer a major safety issue. A future micro-optimization could hash non-string JSON bytes directly, but I would not block on that.

### Staged checkpoint parser requiring counters

Resolved.

`CortexXdrCheckpointHelper.TryLoadFindingsStateCore` now rejects staged findings checkpoints missing required stage counters:

- `totalFindingsCollected`
- `totalAssetsCollected`
- `assetsPage`
- `findingsPage`
- `nextCveIndex`

The parser also rejects unsupported `checkpointVersion` values and refuses legacy joined findings checkpoints without a valid `stage`, which prevents silently resuming with default counter values.

### Empty terminal asset page checkpoint state

Resolved.

`PublishEndpointAssetsAsync` now writes and emits a terminal assets-stage checkpoint when the endpoint page contains no endpoints. The persisted state keeps the current `globalPage`, `assetsPage`, `findingsPage`, totals, and `hasMorePages=false`, without advancing counters for an unpublished empty batch. That avoids leaving the previous findings-stage checkpoint as the latest state when the asset stage terminates immediately.

## Additional Technical Notes

- Cancellation is checked before major stage transitions and inside async record enumeration. The new cancellation test covers cancellation between CVE pages.
- Checkpoint page counters and output page counters are separated clearly enough: `Page` tracks global staged progress, while `FindingsPage` and `AssetsPage` track target file page numbers.
- The findings-stage resume path re-runs XQL and skips by `NextCveIndex`; the local deterministic tie-breaker reduces duplicate-key nondeterminism within the accepted vendor paging constraint.
- Event sink failures remain isolated behind debug logging, matching existing collector patterns.

## Verification

Focused test command:

```bash
dotnet test src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test.csproj --no-restore --filter "FullyQualifiedName~CortexXdrFindingsFlowTests|FullyQualifiedName~CortexXdrCheckpointHelperTests"
```

Result: passed, 18/18 tests. The run emitted `NU1900` warnings because the private package vulnerability feed was unreachable, but build and tests completed successfully.

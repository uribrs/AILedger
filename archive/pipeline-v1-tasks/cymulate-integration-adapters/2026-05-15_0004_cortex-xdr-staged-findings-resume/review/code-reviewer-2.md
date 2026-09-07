# Code Review 2 - Cortex XDR staged findings resume

## Review Scope

- Change type: production collector feature/recovery logic plus tests.
- Risk level: High.
- Stack: C# / .NET 8.
- Scope boundary: technical safety, idiomatic C#/.NET, runtime behavior, recovery/idempotency, maintainability, and test quality only.
- Accepted tradeoff: XQL CVE-stage resume re-runs the sorted query and skips by persisted index because the vendor result API has no documented offset/page parameter.

## Findings

### Major - CVE resume tie-breaker duplicates potentially large JSON fields into cached sort keys

**Location:** `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Flows/Findings/CortexXdrFindingsFlow.cs:31-46`, `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Flows/Findings/CortexXdrFindingsFlow.cs:321-355`

**Problem:** `OrderCveRowsForResume` adds `ThenBy(BuildCveTieBreaker, StringComparer.Ordinal)`, and `BuildCveTieBreaker` concatenates every selected field into a single string for each row, including `affected_hosts` and `affected_products`. LINQ sorting computes and caches these keys for all materialized rows. With up to 50,000 CVE rows, and with host/product arrays that may be large, this can duplicate a large fraction of the XQL result set in memory before publishing begins.

**Impact:** Large tenants can see avoidable allocation spikes and GC pressure, potentially turning a recoverability improvement into an OOM/timeout risk. The issue is not the presence of a tie-breaker; it is storing full normalized JSON for high-cardinality fields as sort keys for every row.

**Recommended fix:** Keep the deterministic client-side tie-breaker, but make the key bounded. For example, compute a stable fixed-size hash over the same selected fields and sort by the hex/base64 hash, or remove very large volatile arrays from the cached string key if they are not needed for practical uniqueness. A hash preserves deterministic ordering while storing tens of bytes per row instead of full array payloads.

**Refactor required:** Local patch. No architectural change needed.

### Major - Versioned findings checkpoints still accept missing/invalid staged counters

**Location:** `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Recovery/CortexXdrCheckpointHelper.cs:200-226`

**Problem:** `TryLoadFindingsStateCore` rejects unsupported `checkpointVersion` and correctly rejects legacy checkpoints without `stage`, but it treats `totalAssetsCollected`, `assetsPage`, and `findingsPage` as optional defaults even for staged checkpoints. In a versioned staged checkpoint, these fields are part of the resume/idempotency contract: `assetsPage` and `findingsPage` drive output file numbering, and collected totals drive result metadata.

**Impact:** A malformed or partially persisted staged checkpoint can be accepted and resume with default page counters. The most dangerous case is `stage=assets` with a missing/invalid `assetsPage`: resume can publish the next asset batch as `assets_000001.json`, risking overwrite or duplicate target naming relative to already published asset pages. This is exactly the class of issue checkpoint versioning should prevent.

**Recommended fix:** For `checkpointVersion == "2"` or any staged checkpoint intended to be current-format, require `totalAssetsCollected`, `assetsPage`, and `findingsPage` to parse as non-negative integers. Only use compatibility defaults in an explicit legacy-staged branch, and cover that branch with tests if it is intentionally supported.

**Refactor required:** Local patch in the checkpoint helper plus focused tests for missing/invalid staged fields.

### Minor - Asset-stage completion on an empty terminal page leaves the last checkpoint marked as resumable

**Location:** `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Flows/Findings/CortexXdrFindingsFlow.cs:268-288`

**Problem:** When the endpoint asset stage fetches an empty page, the loop breaks before saving a terminal checkpoint. If the previous checkpoint had `hasMorePages=true` (for example, after the final CVE page transitions to `stage=assets`, or after a full asset page), the progress state remains marked as resumable even though the flow completed successfully.

**Impact:** If the final progress state is persisted or inspected after completion, it can advertise stale resume state and cause a later resume attempt to perform an unnecessary asset-stage fetch. This is less severe than data loss because the next fetch should be empty, but it weakens checkpoint accuracy and makes operational state harder to reason about.

**Recommended fix:** On `parseState.EndpointsSeen == 0`, save a terminal asset-stage checkpoint with `hasMorePages=false`, preserving the current page counters and totals, before breaking. Add a test for the CVE-to-assets transition followed by an empty endpoints response.

**Refactor required:** Local patch.

## Positive Notes

- Cancellation handling in the findings flow now uses explicit `ThrowIfCancellationRequested` checks around stage boundaries and page loops, which avoids silently converting cancellation into successful completion.
- The staged transition checkpoint after the final CVE page is a sound recovery boundary: it records `stage=assets`, the completed CVE index, and independent findings/assets page counters.
- Rejecting legacy joined findings checkpoints without a staged `stage` value is technically safer than attempting to reinterpret old cursor semantics under the new staged output model.

## Test Evidence

Executed:

```text
dotnet test src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test.csproj --no-restore
```

Result: passed, 23/23 tests. The run emitted `NU1900` warnings because package vulnerability data could not be loaded from the configured CodeArtifact source, but build and tests completed successfully.

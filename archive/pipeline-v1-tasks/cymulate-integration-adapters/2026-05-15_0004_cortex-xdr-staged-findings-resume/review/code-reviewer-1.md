# Code Review: Cortex XDR Staged Findings Resume

Change type: production collector feature/recovery logic plus tests  
Risk level: High  
Stack: C# / .NET 8

## Findings

### Major: Cancellation can be reported as a successful partial collection

File: `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Flows/Findings/CortexXdrFindingsFlow.cs`  
Lines: 176, 241, 156-163

Problem: Both staged loops use `while (... && !cancellationToken.IsCancellationRequested)`. If cancellation is requested between pages, after a checkpoint has been advanced or between the CVE and asset stages, the loop exits normally. `CollectAsync` then logs completion and returns `TotalFindingsCollected` as a successful result.

Impact: A cancelled run can be marked complete even though `hasMorePages`/stage state still indicates more work. In recovery-oriented collector code this is dangerous: orchestration may treat a partial collection as finished, skip resume handling, and publish misleading completion totals.

Recommended fix: Use cancellation as an exceptional control path. Check `cancellationToken.ThrowIfCancellationRequested()` at the top of each loop iteration and again before returning from each stage or before logging final completion. Prefer `while (nextCveIndex < cveRows.Count)` / `while (hasMorePages)` with explicit cancellation checks inside.

Refactor required: Local patch.

### Major: Index-based CVE resume is not fully deterministic for duplicate sort keys

File: `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Flows/Findings/CortexXdrFindingsFlow.cs`  
Lines: 31-35, 315-326

Problem: The accepted resume strategy re-runs the XQL query and skips by `NextCveIndex`, but the query only sorts by `cve_id, name`. The code then slices the returned vendor order directly. If multiple rows share the same `cve_id` and `name`, their relative order is not guaranteed by this implementation.

Impact: A resume from the CVE stage can duplicate or skip rows within equal-key groups if the vendor returns tied rows in a different order across executions. This is exactly the failure mode the explicit sort is meant to reduce, so the remaining nondeterminism matters for idempotency.

Recommended fix: Make the resume order total, not partial. If XQL supports additional stable fields, include them in the `sort`. Otherwise, after materializing `cveRows`, apply a deterministic client-side ordering before paging, using `cve_id`, `name`, and a stable tie-breaker such as normalized row JSON or the full selected-field tuple. Add a test with duplicate `cve_id`/`name` rows to lock the behavior.

Refactor required: Local patch.

### Major: New checkpoint parser rejects pre-existing findings checkpoints without an explicit migration path

File: `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Recovery/CortexXdrCheckpointHelper.cs`  
Lines: 197-209

Problem: `TryLoadFindingsStateCore` now requires `stage` and `nextCveIndex`. Findings checkpoints written by the previous implementation did not contain those keys, so `CanResumeFrom`/`ResumeAsync` will reject them.

Impact: Any persisted findings checkpoint from the prior format becomes non-resumable after deployment. In the best case the user gets a clear resume failure; in the worse operational path the collection is restarted and previously published data may be duplicated. Persisted recovery state should either be versioned and migrated or deliberately rejected with an explicit compatibility decision.

Recommended fix: Add checkpoint version handling. If old findings checkpoints cannot be safely mapped to the new staged model, fail with a specific legacy-checkpoint message and document the non-resumable format. If they can be mapped, provide defaults based on the old semantics and cover that path with tests.

Refactor required: Local patch, possibly small migration helper.

### Minor: Tests do not assert the persisted checkpoint/cursor state produced by staged transitions

File: `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test/CortexXdrFindingsFlowTests.cs`  
Lines: 217-357

Problem: The new resume tests validate resumed requests and output paths, but the flow tests do not inspect the `AdapterProgressContext` state after CVE-stage pages, the CVE-to-assets transition, or final asset pages.

Impact: Bugs in `stage`, `nextCveIndex`, `nextSearchFrom`, `assetsPage`, `findingsPage`, cursor clearing, or total counters can slip through while the visible batches still look correct. For this change, the checkpoint state is the production contract that makes recovery safe.

Recommended fix: Add focused tests around staged checkpoint writes: mid-CVE page, final CVE page transitioning to assets, mid-assets page, and final assets page. Assert the state dictionary and cursor values, not only emitted NDJSON and request offsets.

Refactor required: Test-only local patch.

## Verification

Ran:

```text
dotnet test src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test.csproj --no-restore --filter FullyQualifiedName~CortexXdr
```

Result: Passed, 19 tests. NuGet vulnerability-data warnings appeared because the configured CodeArtifact feed was unreachable in this environment.

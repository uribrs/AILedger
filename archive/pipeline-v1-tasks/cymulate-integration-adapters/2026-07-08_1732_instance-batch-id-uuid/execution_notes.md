# Execution Notes

## 2026-07-08 — contract-driven-execution (branch batchful-uploads-uuid)

Changed:
- `Shared/.../DataPipeline/Egress/BatchScopedStorage.cs`
  - `BuildBatchInstanceId(baseUrl, pageNumber)`: RFC 4122 v5 over frozen namespace
    `b584b489-7c3d-4caf-97eb-49d7ff6d78fb` + `"{base}/batch_{page:D6}"`; uses .NET 8
    `Guid.TryWriteBytes(..., bigEndian: true)` / `new Guid(..., bigEndian: true)` — no manual
    endianness swapping needed.
  - `BeginPage` now writes that UUID to `Metadata["instanceBatchId"]` (was: folder segment).
  - `InstanceBatchIdMetadataKey` doc comment rewritten; namespace constant doc'd as frozen.
  - Path logic (BuildBatchSegment/StripBatchSegment/ResolveBaseUrl/RestoreBase) untouched.
- `Shared/.../DataPipeline/Egress/README.md` — batch-id bullet rewritten (UUIDv5, determinism load-bearing twice).
- Tests:
  - Shared `BatchScopedStorageTests`: 2 literal assertions → `BuildBatchInstanceId(...)`; 4 new pins:
    golden vector `a9a3292a-43fe-56d8-8626-fcbd67059ccd` (cross-checked against Python `uuid.uuid5` —
    validates RFC v5 correctness AND freezes the namespace), determinism+parseable+lowercase,
    cross-page/cross-base inequality, resume round-trip announces identical id.
  - `QualysBatchScopedStorageTests`: 1 literal → derived. RestoreBase-removal already covered (unchanged).
  - `InsightVmCloudBatchScopedStorageTests`: 2 literals → derived; NotContainKey assertions unchanged.

Verified:
- `dotnet build` solution: 0 warnings, 0 errors.
- Filtered runs (no suite sweeps): Shared BatchScopedStorageTests 18/18; QualysBatchScopedStorageTests 3/3;
  InsightVmCloudBatchScopedStorageTests 4/4; AdapterFailureDecisionExecutorTests 19/19 (lives in
  DummyCollector.Test — ran class-filtered only, per no-suite-sweep policy).

Residual risks:
- OPEN assumption unchanged: ISB end-to-end delivery of the `instanceBatchId` key (non-blocking, flagged
  for ISB/consumer owners in assumptions.md).

## 2026-07-08 — post-review (orchestrator)

- Verifier (review/verifier-1.md): PASS, no gaps; golden vector independently recomputed.
- Code review (review/code-reviewer-1.md): Approve. Repaired: added argument guards to
  BuildBatchInstanceId (null/whitespace baseUrl, page < 1) — rebuild + Shared suite re-run 18/18.
  Accepted risks (recorded, not repaired):
  - Deploy-skew during rollout: wire value changes batch_NNNNNN → UUID with no in-repo consumer;
    external backend must not assume the old shape (trivially discriminable — UUID never starts
    with "batch_"). Coordination item for consumer owners.
  - "Globally unique" rests on base-storage-URL uniqueness per run (platform guarantee), not on
    this code; two runs sharing a URL would upsert-merge — intentional, replay stability wins.

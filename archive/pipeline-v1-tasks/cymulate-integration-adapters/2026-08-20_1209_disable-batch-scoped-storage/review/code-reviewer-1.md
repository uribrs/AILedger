# Code Review 1

Change classification: collector storage-layout configuration and recovery-sensitive publishing; medium risk.

## Findings

### Major — Flat publishing does not normalize a scoped URL carried by a resume

**Problem:** The defaults at `InsightVmCloudCollectorConfiguration.cs:48` and `QualysCollectorConfiguration.cs:53`, plus the hard-coded opt-out at `TenableIoCorrelatedBatchPublisher.cs:53`, switch page publishing to `batchScopedStorage: false`. In that mode the emitter does not run the batch-scope lifecycle. A resumed progress context can legitimately contain only a prior page URL such as `.../batch_000007` (the shared `BatchScopedStorage` API explicitly documents this resume shape), but none of these flat paths restores/normalizes it before publishing. The updated resume tests construct only pristine run-root contexts, so they do not exercise the transition.

**Impact:** A retry/resume crossing this layout change can publish page 8 as `.../batch_000007/findings_000008.json` (and analogously for InsightVM assets/findings and Qualys findings) instead of at the run root. The progress event may also retain the previous batch identity. This defeats the intended flat layout, leaves objects outside the new naming sequence, and can cause upstream parsing or replay to miss, misattribute, or duplicate data.

**Recommended fix:** When flat storage is selected, normalize `storageUrl` at flow entry using `BatchScopedStorage.ResolveBaseUrl(progressContext)` and clear the old scope metadata via `BatchScopedStorage.RestoreBase(progressContext)` before the first publish. Add transition tests whose progress metadata contains a scoped `storageUrl` without `baseStorageUrl`, then assert resumed pages publish at the bare run root and carry no stale `instanceBatchId`.

**Scope:** Local patch in each affected collector flow/publisher plus focused tests; no infrastructure refactor is required.

## Validation

- `git diff --check` passed for the reviewed artifacts.
- InsightVM Cloud collector tests: 19 passed.
- Qualys collector tests: 27 passed.
- Tenable.io collector tests: 153 passed.

Verdict: changes requested due to the cross-version retry/resume path defect above.

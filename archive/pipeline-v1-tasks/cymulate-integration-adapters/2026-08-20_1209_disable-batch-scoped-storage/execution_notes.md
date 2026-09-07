# Execution Notes

## W1 — InsightVM Cloud
- Changed `InsightVmCloudCollectorConfiguration.BatchScopedStorage` default from `true` to `false` and documented flat run-root storage as the default.
- Updated the default assertion while retaining explicit scoped-storage capability tests.
- Full-project execution exposed three stale default path expectations in `InsightVmCloudCollectorTests.cs`; ownership was extended to W1, which changed them to flat `assets_*.json` / `findings_*.json` paths.
- Focused batch-scoped tests: 4/4 passed. Full project: 19/19 passed.

## W2 — Qualys
- Changed `QualysCollectorConfiguration.BatchScopedStorage` default from `true` to `false` and documented flat run-root storage as the default.
- Updated the default assertion while retaining explicit scoped-storage lifecycle tests.
- Focused batch-scoped tests: 5/5 passed. Full project: 27/27 passed.

## W3 — TenableIo
- Changed the correlated findings emitter argument from `batchScopedStorage: true` to `false`.
- Updated comments and path helpers/assertions for flat `findings_{page:D6}.json` output while preserving ordering, checkpoints, retries, resume numbering, and atomic page objects.
- Focused correlated/collector tests: 41/41 passed. Full project: 153/153 passed.

## Synthesis Verification
- Production collector scan for literal `BatchScopedStorage = true` / `batchScopedStorage: true`: no matches.
- `dotnet build src/Cymulate.Integration.Adapters/Cymulate.Integration.Adapters.sln --no-restore --verbosity minimal`: succeeded with 0 warnings and 0 errors.
- `git diff --check`: passed.

## Residual Risk
- No live collector run was performed; evidence is source-level plus component/integration-style unit tests and a full solution build.

## Code Review Repair Cycle
- `review/code-reviewer-1.md` found a Major cross-version resume issue: flat emitters could inherit a pre-change `storageUrl` ending in `batch_NNNNNN` and a stale `instanceBatchId`.
- InsightVM Cloud assets/findings publishers, the Qualys findings publisher, and the TenableIo correlated publisher now resolve the pristine base URL, restore it as the live `storageUrl`, and clear stale scope identity before flat publication.
- Added scoped-only resume regression tests for both InsightVM lanes, Qualys page 8, and TenableIo page 8.
- Post-repair full projects: InsightVM Cloud 21/21, Qualys 28/28, TenableIo 154/154. `git diff --check` remains clean.
- Synthesis removed an unnecessary new InsightVM exception for contexts without a storage URL; the InsightVM project remained 21/21 afterward.

## Independent Review
- `review/verifier-2.md` — PASS; all success criteria satisfied, A1-A3 validated, scoped-only legacy resume transition verified for all four publishing lanes.
- `review/code-reviewer-2.md` — approved with no findings; path uniqueness, identity cleanup, retry/resume ordering, and absent-storage behavior accepted.

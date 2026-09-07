# Verifier 2 — repair cycle

## Verdict

PASS. The Major resume-transition finding in `review/code-reviewer-1.md` is resolved in the current diff. Scoped-only pre-change metadata (`storageUrl` ending in `batch_000007`, no `baseStorageUrl`, stale `instanceBatchId`) is normalized to the bare run root before flat publication for InsightVM Cloud assets, InsightVM Cloud findings, Qualys findings, and TenableIo correlated findings. Page 8 publishes flat, stale identity is removed, explicit scoped opt-in remains covered, missing storage metadata gains no new normalization failure, all 203 affected project tests pass, the full solution builds with 0 warnings/errors, and the production literal-true scan is empty.

No product/test repair is required from this verifier pass. A fresh isolated code-reviewer rerun remains required to replace the prior changes-requested verdict before the complete contract can close.

## Findings by severity

- P0: none.
- P1: none.
- P2: none.
- P3 / workflow condition: `review/code-reviewer-1.md` predates the repair and correctly records changes requested. The repair is independently verified here, but the workflow still needs an isolated post-repair code-review pass; no implementation defect remains from the original finding.

## Code-review finding closure

**Major — flat publishing did not normalize a scoped URL carried by resume: RESOLVED.**

- InsightVM Cloud assets resolves the base URL and restores scope metadata before flat emission at `InsightVmCloudAssetsPagePublisher.cs:54-67`; its scoped-only page-8 regression is `InsightVmCloudBatchScopedStorageTests.cs:115-138`.
- InsightVM Cloud findings performs the same transition at `InsightVmCloudFindingsPagePublisher.cs:62-75`; its page-8 regression is `InsightVmCloudBatchScopedStorageTests.cs:140-163`.
- Qualys tracks the selected mode and invokes `NormalizeFlatStorage` only for flat publication at `QualysFindingsBatchPublisher.cs:21-47, 94-103`; its page-8 regression is `QualysBatchScopedStorageTests.cs:147-167`.
- TenableIo invokes `RestoreRunRootStorage` before every flat correlated publish at `TenableIoCorrelatedBatchPublisher.cs:65-82, 110-127`; its full resume-flow regression verifies flat target path, live run-root URL, and stale identity removal at `TenableIoCorrelatedFlowTests.cs:610-643`.
- Each implementation resolves the base before `RestoreBase`, so a legacy scoped-only URL is stripped correctly even when `baseStorageUrl` was not round-tripped. `RestoreBase` then removes `instanceBatchId` unconditionally.

## Requested boundary checks

### Scoped-only legacy resume

Satisfied for all four lanes. The new tests construct `.../batch_000007` plus a stale identity without `baseStorageUrl`, publish page 8, and assert bare run-root `assets_000008.json` / `findings_000008.json` targets. TenableIo additionally exercises the actual checkpoint resume flow and confirms export/chunk continuation.

### Identity cleanup

Satisfied. Every new transition test asserts `instanceBatchId` is absent after publication. The production normalization calls `BatchScopedStorage.RestoreBase`, whose contract removes identity independently of whether `baseStorageUrl` exists.

### Explicit scoped opt-in

Preserved. InsightVM Cloud explicit-`true` assets/findings tests still assert `batch_NNNNNN` targets and deterministic identity (`InsightVmCloudBatchScopedStorageTests.cs:38-87`). Qualys explicit-`true` publish, checkpoint, and dud-page lifecycle tests remain (`QualysBatchScopedStorageTests.cs:37-125`). All pass within the full project runs.

### Missing storage URL

No new failure is introduced by normalization. `BatchScopedStorage.ResolveBaseUrl` returns null for absent metadata; each new implementation guards assignment on a non-null result, and `RestoreBase` is a no-op except for safely removing any stale identity. The synthesis correctly removed the transient InsightVM exception. TenableIo's staging spine still intentionally requires a storage URL, an existing collector contract verified by `TenableIoAssetSpineTests.RequireBaseStorageUrl_WhenTheRunCarriesNoStorageUrl_FailsLoudly`; the repair does not add or move that failure.

### Production literal-true scan

Satisfied. The verifier reran the precise production scan for a `BatchScopedStorage` property/assignment or named `batchScopedStorage` argument set to literal `true`; `rg` exited 1 with no matches. Explicit `true` values remain only in unit tests as preserved opt-in coverage.

## Success-criteria coverage

1. **No production collector defaults/emitter calls use literal `true` — satisfied.** Current precise scan is empty; all product defaults/calls in scope are false.
2. **All three collectors use flat storage by default, including cross-version resume — satisfied.** Source plus scoped-only page-8 regressions cover InsightVM assets/findings, Qualys, and TenableIo.
3. **Tests match the disabled state while preserving capability coverage — satisfied.** Disabled-default assertions and explicit scoped opt-in tests coexist and pass.
4. **Affected test projects and build pass — satisfied.** Verifier reruns: InsightVM Cloud 21/21, Qualys 28/28, TenableIo 154/154; zero failures/skips. `dotnet build ...sln --no-restore --nologo --verbosity quiet` exited 0 with 0 warnings and 0 errors.
5. **Production literal-true scan is clean — satisfied.** No matches.
6. **Independent verifier and isolated review report no unresolved issue — partially satisfied at this workflow point.** Both verifier passes now find the repaired implementation correct. The earlier reviewer finding is resolved by code and tests, but a fresh isolated reviewer verdict is still required by the workflow.

## Original-request and constraint coverage

The three enabled production values remain disabled without removing the shared capability or Falcon's default-off configurable path. The repair is confined to the same three collector/test clusters and addresses only storage scoping at publication boundaries. No retrieval, parsing, pagination, staging-spine identity, checkpoint grammar, record shape, or shared IntegrationInfra code changed. `git diff --check` passes.

The decomposed path remains correct under the rubric: three disjoint file sets existed. Repair ownership extensions are recorded in `orchestration_plan.md`; the 12 changed product/test files remain inside W1's InsightVM set, W2's Qualys set, or W3's TenableIo set. No shared frozen surface or sibling-owned path was changed.

## Assumption disposition

| id | status | citation | actor |
|----|--------|----------|-------|
| A1 | VALIDATED | Base-ref scan at `0c61fad68ea4add59784cb9294737c880d8676fc` found exactly InsightVM Cloud, Qualys, and TenableIo enabled production cases; current precise scan has no matches | verifier |
| A2 | VALIDATED | Production normalization at InsightVM assets `:54-67`, InsightVM findings `:62-75`, Qualys `:44-47, 94-103`, and TenableIo `:65-82, 110-127`; scoped-only resume tests at InsightVM `:115-163`, Qualys `:147-167`, TenableIo flow `:610-643`; verifier runs 21/21, 28/28, 154/154 | verifier |
| A3 | VALIDATED | Disabled-default plus retained explicit opt-in tests in `InsightVmCloudBatchScopedStorageTests.cs` and `QualysBatchScopedStorageTests.cs`; full affected projects passed 21/21, 28/28, and 154/154 after repair | verifier |

## Decision drift

- **Change the three production values to false without deleting the reusable capability — landed as decided.** The repair adds transition normalization only; shared scoping and explicit opt-in tests remain.
- **Preserve Falcon's existing default-off configurable behavior — landed as decided.** No Falcon file changed.
- **Update stale capability comments and assertions — landed as decided.** Flat-default and resume-path comments/assertions remain aligned after repair.
- **Proceed on the belief local collector tests expose storage-scope behavior without vendor connectivity — validated.** Scoped-only transition behavior, identity cleanup, explicit opt-in, retry/resume numbering, and target paths all execute locally in passing tests.
- **Run independent verification and isolated code review — followed with a repair loop.** Verifier 1 passed the initial implementation, code-reviewer 1 exposed a cross-version edge case, workers repaired it, and this verifier 2 confirms closure. A fresh isolated post-repair review is the remaining planned step.

## Residual risks

- No live vendor-backed run or real object-store publication was performed. Direct publisher captures and the TenableIo full resume-flow regression materially reduce this risk.
- The complete solution test suite was not run. All 203 tests in affected projects passed and the entire solution compiled cleanly.
- Verification used restored packages (`--no-restore`); dependency acquisition was not retested because no dependency state changed.

## Required next action

Run the isolated post-repair code-reviewer. No implementation repair is required before that review.

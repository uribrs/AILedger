# Verifier 1

## Verdict

PASS for the implemented change. No correctness finding or required product/test repair was identified. The base-ref scan proves the three enabled production cases, the current production scan finds no literal `true` opt-in, all 199 affected project tests pass in this verifier run, and the full solution builds with 0 warnings and 0 errors.

Final workflow completion remains contingent on the separately required isolated code-reviewer pass. That pass follows independent verification by design, so the review half of Success Criterion 6 is pending rather than an implementation defect.

## Findings by severity

- P0: none.
- P1: none.
- P2: none.
- P3 / workflow condition: the isolated code-reviewer has not run yet (`state.json` records `workflow.codeReviewerRun: false`). The orchestrator must run it before declaring the complete contract satisfied; no code repair is required by this verifier.

## Success-criteria coverage

1. **No production collector default or hard-coded emitter call sets batch-scoped storage to `true` — satisfied.** The verifier's precise scan over `src/Cymulate.Integration.Adapters/Collectors/**/*.cs` returned no current match for a `BatchScopedStorage` property/assignment or named `batchScopedStorage` argument set to literal `true`. A full occurrence audit shows current production defaults at `InsightVmCloudCollectorConfiguration.cs:48`, `QualysCollectorConfiguration.cs:53`, `FalconCollectorConfiguration.cs:166`, and `FindingsFlowRunConfig.cs:51` are all `false`; TenableIo constructs its emitter with `batchScopedStorage: false` at `TenableIoCorrelatedBatchPublisher.cs:53`.
2. **InsightVM Cloud, Qualys, and TenableIo use flat storage scoping by default — satisfied.** InsightVM Cloud and Qualys configuration defaults are false and their flows forward that value. TenableIo's correlated emitter is explicitly false. Updated assertions verify flat `assets_*.json` / `findings_*.json` run-root paths, with TenableIo checks covering initial pages, in-invocation retry numbering, and resume at page 8 (`TenableIoCorrelatedFlowTests.cs:135-143, 596-603, 850-858`; `TenableIoCollectorTests.cs:338-343`).
3. **Tests match the disabled state — satisfied.** InsightVM Cloud asserts `Configuration_DisablesBatchScopedStorageByDefault` at `InsightVmCloudBatchScopedStorageTests.cs:26-35`; Qualys does the same at `QualysBatchScopedStorageTests.cs:24-34`. Existing explicit-`true` tests remain as deliberate shared-capability coverage rather than being weakened or deleted.
4. **Relevant collector projects build and focused tests pass — satisfied.** The verifier reran all three full affected projects with `--no-restore`: InsightVM Cloud 19 passed, Qualys 27 passed, and TenableIo 153 passed; all had zero failures/skips. The verifier also reran `dotnet build src/Cymulate.Integration.Adapters/Cymulate.Integration.Adapters.sln --no-restore --verbosity minimal`; exit 0, 0 warnings, 0 errors.
5. **Production-source literal-true scan is clean — satisfied.** The exact scan returned no matches. The base-ref counterpart found exactly the two old configuration defaults and the TenableIo emitter argument, confirming that the scan is discriminating rather than vacuously broad.
6. **Independent verifier and isolated review report no unresolved correctness issue — partially satisfied at this workflow point.** This verifier found no unresolved correctness issue. The isolated code-reviewer is intentionally next and remains required before final completion.

## Original-request and constraint coverage

The user's request to “un-true” the three previously reported production cases is fully implemented: InsightVM Cloud, Qualys, and TenableIo now choose flat storage by default. The reusable shared `BatchScopedStorage`/`NdjsonBatchEmitter` infrastructure is unchanged; Falcon remains default-off and explicitly configurable. The base-to-worktree diff contains only the three collector clusters and their affected tests/comments, with no retrieval, parsing, staging-spine, checkpoint grammar, record shape, or shared-infrastructure change. `git diff --check` passes.

The decomposed execution path remains justified in hindsight. Recon identified three disjoint collector/test sets, satisfying the rubric's hard trigger. W1's ownership extension to `InsightVmCloudCollectorTests.cs` was bounded to the same collector after the full project exposed stale path assertions and is recorded in both `orchestration_plan.md` and `execution_notes.md`. The nine changed files stay within the three declared sets; no worker touched the frozen shared surface or another worker's files.

No external research was needed because all behavior was determinable from local source and tests. Execution aligns with `research/internal-recon.md`, including its verify-first requirements and forbidden shared-surface changes.

## Assumption disposition

| id | status | citation | actor |
|----|--------|----------|-------|
| A1 | VALIDATED | Base-ref `git grep` at `0c61fad68ea4add59784cb9294737c880d8676fc` found the InsightVM Cloud default, Qualys default, and TenableIo emitter literal (plus the comment quoting it); `research/internal-recon.md`, “Files in scope” and “No other production source needs modification” | verifier |
| A2 | VALIDATED | Current production lines `InsightVmCloudCollectorConfiguration.cs:48`, `QualysCollectorConfiguration.cs:53`, and `TenableIoCorrelatedBatchPublisher.cs:53`; verifier project runs 19/19, 27/27, and 153/153; flat-path assertions at `TenableIoCorrelatedFlowTests.cs:135-143, 596-603, 850-858` | verifier |
| A3 | VALIDATED | Disabled-default assertions at `InsightVmCloudBatchScopedStorageTests.cs:26-35` and `QualysBatchScopedStorageTests.cs:24-34`; retained explicit-opt-in tests at InsightVM lines 38-87 and Qualys lines 37-145; verifier project runs passed | verifier |

## Decision drift

- **Treat “un-true them” as changing the three production values to `false`, not deleting the capability — landed as decided.** The three values are false and the shared emission implementation plus explicit opt-in tests remain.
- **Preserve Falcon's default-off configurable behavior — landed as decided.** Falcon product files are unchanged; its defaults remain false and its builder still accepts an explicit `batchScopedStorage` override.
- **Update stale capability comments and assertions — landed as decided.** Production comments now describe flat defaults; TenableIo flow/publisher and test path comments describe run-root storage; affected default and path assertions were updated.
- **Proceed on the unverified belief that collector tests expose storage-scope behavior without vendor connectivity — landed as decided and validated.** All three local project suites exercised configuration/emission/path behavior and passed without vendor connectivity; no fallback-only verification was needed.
- **Run independent verification and isolated code review — verifier landed as decided; code-reviewer remains the next required workflow step.** This is planned sequencing, not abandoned scope.

## Residual risks

- No live vendor-backed collector invocation or S3 publication was performed. Risk is low-to-moderate because the change is a local storage-layout switch and component/integration-style tests directly capture target paths, metadata, retry, and resume behavior.
- The complete solution test suite was not run. Risk is low because all 199 tests in the three affected projects passed and the full solution compiled cleanly; unrelated runtime suites are outside the changed clusters.
- Verification used existing restored assets (`--no-restore`), so clean package acquisition was not retested. No package or dependency file changed.

## Required next action

Run the isolated code-reviewer pass. No implementation repair is required before that review.

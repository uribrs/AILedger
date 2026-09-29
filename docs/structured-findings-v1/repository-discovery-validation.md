# External-output repository discovery validation

Date: 2026-09-28. Branch: `codex/structured-findings-contract`. Base: task-5 commit `5d22e6b`.
The requested worktree was clean before edits. This is a new validation record; the earlier
[task-5 results](task-5-validation.md), including its failed standard invocation, remain unchanged.

## Cause and scoped fix

VSTest runs the tests from the external assembly directory. Neither that directory nor its
ancestors identifies the source checkout. The existing repository-aware wrapper succeeds because
`scripts/test-governed.sh` explicitly changes into its checkout before running xUnit.

Both test projects now import `tests/Support/RepositoryLayout.props`. MSBuild derives the checkout
from that file's location and embeds `AILedger.TestRepositoryRoot` in **test assemblies only**.
The shared `RepositoryLayout` validates `AILedger.sln` and either a `.git` directory (clone) or
file (worktree). It does not guess another checkout from the runtime directory. No machine-specific
checkout path is present in source, and no runner argument or manually exported variable is needed.

Memory's source-boundary checks and the main suite's Git/cognitive helpers use this shared root.
The scripted provider request fixture now names that repository instead of the test host's current
directory; its exact argument assertion still checks the request's working directory. The negative
Codex-outside-Git test remains intact.

The first implementation run exposed a second layer: CLI fixtures that briefed against the resolved
root then omitted `--cognitive-root` on later commands still hit production's default discovery.
The main test assembly now sets the existing `AILEDGER_COGNITIVE_ROOT` setting once at module startup,
before parallel tests, to its build checkout's cognitive layer. This is confined to that test process
and its children. Explicit per-command cognitive roots still take precedence. No current-directory
mutation, user/global configuration, production implementation, runner change, assertion relaxation,
skip, or frozen-output change was made.

Six new cases cover the real build metadata/input wiring (including the test process setting),
clone/worktree markers, and rejection of incomplete checkout markers with a rebuild diagnostic.

## Actual results

| Validation | Main | Memory | Result |
|---|---|---|---|
| Initial standard run, before configuring the test process cognitive root | 1,777 passed / 124 failed | 99 passed | Exit 1; retained as an intermediate failure |
| Final standard `dotnet test`, external artifacts | 1,901 passed | 99 passed | Exit 0; zero failures or skips |
| Repository-aware full runner, external artifacts | 1,901 passed | 99 passed | Exit 0; zero failures, skips, or runner errors |

The main count is the original 1,895 plus six new discovery cases. All **244 previously failing
case names** in the task-5 standard log matched passing cases in the final TRX files. There are
**no remaining failures in either final full invocation**.

- Main: **212** cognitive-root lookup failures resolved, including fixtures whose later CLI commands
  also needed the same cognitive layer.
- Main: **26** Codex-outside-Git exceptions plus **1** masked capability-probe assertion resolved by
  correcting the scripted request's working directory.
- Main: **3** `KernelVersionTests` Git-root failures resolved:
  `TheKernelDoesNotWarnAboutTheTreeItWasBuiltFrom`, `AGitCommandThatPrintsNothingHasStillAnswered`,
  and `AGitCommandThatFailsHasNotAnswered`.
- Memory: `ShadowBoundaryTests.MemoryProductionSourcesDoNotUseGovernedReadsOrWriterLocks` and
  `ShadowBoundaryTests.R1_KernelProjectsDoNotReferenceOrInvokeMemory` both pass.
- Frozen checker: **11 unchanged hashes**, checked before and after code changes.
- Read-only baseline probe: **four historical report cases and four provider-cost samples matched**.
  No capture mode was used.
- Builds: no compiler warnings/errors; runner solution/runner builds and baseline build each report
  zero warnings and errors. The sandboxed baseline build emitted a macOS `CSSM_ModuleLoad()` message
  during restore but exited 0; its read-only probe also exited 0.
- `git diff --check`: clean.

The earlier task-5 failure population, grouped by class, is below. Every listed case now passes;
counts include theory cases. Comparison used exact display names against the final TRX results,
not just aggregate totals.

| Previously failing class | Resolved cases |
|---|---:|
| `AILedger.Memory.Tests.Architecture.ShadowBoundaryTests` | 2 |
| `AILedger.Tests.Assurance.BundleAssuranceTests` | 82 |
| `AILedger.Tests.Assurance.LegacySpacedWorkCliTests` | 8 |
| `AILedger.Tests.Cli.BatchPreflightCliTests` | 4 |
| `AILedger.Tests.Cli.CliApplicationBoundaryTests` | 4 |
| `AILedger.Tests.Cli.EntryActionCliTests` | 2 |
| `AILedger.Tests.Cli.KernelVersionTests` | 3 |
| `AILedger.Tests.Cli.LessonLifecycleCliTests` | 1 |
| `AILedger.Tests.Cli.ProviderDispatchCliTests` | 11 |
| `AILedger.Tests.Cli.ProviderGrantCliTests` | 6 |
| `AILedger.Tests.Cli.ProviderRunRecordingCliTests` | 7 |
| `AILedger.Tests.Cli.ProviderScopeCliTests` | 4 |
| `AILedger.Tests.Cli.RefusalJournalLaunchTests` | 3 |
| `AILedger.Tests.Cli.RunCostLaunchTests` | 26 |
| `AILedger.Tests.Cli.RunDispatchLaunchTests` | 12 |
| `AILedger.Tests.Cli.RunManifestLaunchTests` | 3 |
| `AILedger.Tests.Cli.TaskRetrospectiveCliTests` | 1 |
| `AILedger.Tests.Cli.TaskStatusCliTests` | 1 |
| `AILedger.Tests.Cli.WhoRoleCoverageTests` | 1 |
| `AILedger.Tests.Cli.WorkItemBaseRefTests` | 4 |
| `AILedger.Tests.Cli.WorkItemCliTests` | 9 |
| `AILedger.Tests.Constraints.ConstraintContextTests` | 1 |
| `AILedger.Tests.ContextBriefing.ContextBudgetDeliveryTests` | 3 |
| `AILedger.Tests.ContextBriefing.ContextBuildLifecycleTests` | 5 |
| `AILedger.Tests.ContextBriefing.ContextSkillSelectionTests` | 2 |
| `AILedger.Tests.Core.PipelineGateTests` | 3 |
| `AILedger.Tests.Findings.ProviderFindingsLaunchTests` | 1 |
| `AILedger.Tests.Providers.LaunchTimeoutTerminationTests` | 5 |
| `AILedger.Tests.Providers.ProviderArgumentsTests` | 5 |
| `AILedger.Tests.Providers.ProviderProtocolTests` | 14 |
| `AILedger.Tests.Providers.ReviewerAmbientInputTests` | 3 |
| `AILedger.Tests.Verification.LaunchPreflightTests` | 8 |

Task-5 comparison log SHA256: `348027acfea5064b77be288f2d1fe094969cb4db158b207d630addf4fc9b7ca3`.

## Reproduction

Run from the checkout. Use a fresh external directory; keep the source checkout available:

```sh
validation=$(mktemp -d "${TMPDIR:-/tmp}/ailedger-discovery.XXXXXX")
dotnet test AILedger.sln --artifacts-path "$validation/standard" -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false --verbosity minimal --logger trx --results-directory "$validation/results"
UseSharedCompilation=false sh scripts/test-governed.sh all
python3 tools/FindingsBaseline/verify-fixtures.py
dotnet build tools/FindingsBaseline/FindingsBaseline.csproj --artifacts-path "$validation/baseline" -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false
dotnet "$validation/baseline/bin/FindingsBaseline/debug/FindingsBaseline.dll" tests/Fixtures/structured-findings-v1
```

Actual retained temporary logs/artifacts (may expire):

- Initial standard log: `/tmp/ailedger-discovery-standard-20260928.log`; artifacts/TRX below
  `/tmp/ailedger-discovery-standard-20260928/`.
- Final standard log: `/tmp/ailedger-discovery-standard-final-20260928.log`; artifacts/TRX below
  `/tmp/ailedger-discovery-standard-final-20260928/`.
- Full wrapper log: `/tmp/ailedger-discovery-runner-20260928.log`; build and suite logs below
  `/private/var/folders/v3/pncbbv7x45jgp4bnd81fjnl80000gn/T/ailedger-tests.w5wOP0/`.
- Baseline build/probe logs: `/tmp/ailedger-discovery-baseline-build-20260928.log` and
  `/tmp/ailedger-discovery-baseline-probe-20260928.log`; build below
  `/tmp/ailedger-discovery-baseline-20260928/`.

## Remaining limitations and boundaries

Tests requiring source are not standalone distributable binaries. The build's Git checkout must
remain accessible; rebuild after moving/deleting it or copying the test binaries to another
machine. Source archives without a `.git` marker remain unsupported for these repository checks.
The test process deliberately selects the build checkout's cognitive layer even if the invoking
shell points `AILEDGER_COGNITIVE_ROOT` elsewhere. Tests for another cognitive layer must pass their
explicit `--cognitive-root`; testing ambient production discovery would need an isolated process.

`ReconsiderationConsultantTests.R8_ConsultationRequiresARecordedSubjectRole` passed in both final
runs. Its pre-existing independent-clock race remains unchanged and is not claimed fixed.
Local sockets, Git, and disposable application-data access are still required. Permission failures
in a more restricted environment must remain visible; do not weaken or skip those checks.

The kernel development workflow stayed OFF: no governed development task, development governance
record, agent dispatch, global installation, billable provider episode, merge, publication, or task
6–8 implementation. Test kernel writes belong to disposable fixtures. Production behavior and all
structured-findings trust, compatibility, measurement, and durability limits remain as documented
in task 5.

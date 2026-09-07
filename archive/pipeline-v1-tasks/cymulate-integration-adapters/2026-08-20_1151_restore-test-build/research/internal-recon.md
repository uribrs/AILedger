# Internal Recon

## Durable sources read
- `CLAUDE.md:14-16,24-42,96-102` — settles the `net8.0` build/test entry points, the transitive ownership of `Cymulate.Integration.Client` and `Cymulate.Http.Package.Session`, central package management, redirected unit-test outputs, and the `InternalsVisibleTo` convention.
- `src/Cymulate.Integration.Adapters/UnitTests/Directory.Build.props:1-29` — settles unit-test `bin`/`obj` routing and the required exclusion of stale in-project generated artifacts.
- `ai/skills/collector-tests/SKILL.md` — settles the repository's behavior/invariant-oriented test posture; no test intent or assertions need to change for this compile-graph regression.
- Injected `AGENTS.md` instructions — settle .NET specialization, narrow cohesive changes, SRP, and preserving code-local conventions. No repository `AGENTS.md` file exists at the worktree root.

## Files in scope
- `src/Cymulate.Integration.Adapters/UnitTests/Indicators/Cymulate.Integration.Adapters.Indicators.Defender.Test/Cymulate.Integration.Adapters.Indicators.Defender.Test.csproj:20-22` — sole repair candidate; its production-project `ProjectReference` item group was deleted by the pulled merge — touched by: restore test build graph.
- `src/Cymulate.Integration.Adapters/Indicators/Cymulate.Integration.Adapters.Indicators.Defender/Cymulate.Integration.Adapters.Indicators.Defender.csproj:9-18` — referenced production project and source of Defender types plus transitive Infra dependencies — touched by: verification only, no edit indicated.
- `src/Cymulate.Integration.Adapters/UnitTests/Indicators/Cymulate.Integration.Adapters.Indicators.Defender.Test/DefenderAdapterProcessTests.cs:2-7,14-17,33` — compile-failure consumer — touched by: verification only.
- `src/Cymulate.Integration.Adapters/UnitTests/Indicators/Cymulate.Integration.Adapters.Indicators.Defender.Test/DefenderAdapterTests.cs:1-5,12-15,34` — compile-failure consumer — touched by: verification only.
- `src/Cymulate.Integration.Adapters/UnitTests/Indicators/Cymulate.Integration.Adapters.Indicators.Defender.Test/DefenderIOCFlowAuthRetryTests.cs:2-6,19,180-181,221` — compile-failure consumer — touched by: verification only.
- `src/Cymulate.Integration.Adapters/UnitTests/Indicators/Cymulate.Integration.Adapters.Indicators.Defender.Test/DefenderIOCFlowBatchingTests.cs:4-8,21,100-101,141` — compile-failure consumer — touched by: verification only.
- `src/Cymulate.Integration.Adapters/UnitTests/Indicators/Cymulate.Integration.Adapters.Indicators.Defender.Test/DefenderIocUploadHandlerNonTransientTests.cs:3-9,24,76,104,120-121` — compile-failure consumer — touched by: verification only.

Compiler evidence:

- `dotnet build src/Cymulate.Integration.Adapters/Cymulate.Integration.Adapters.sln --no-restore --verbosity minimal` exits 1 with `0 Warning(s), 56 Error(s)`; every compiler error belongs to `Cymulate.Integration.Adapters.Indicators.Defender.Test.csproj`, while the Defender production project and the other emitted test projects build.
- A focused `dotnet build .../Cymulate.Integration.Adapters.Indicators.Defender.Test.csproj --no-restore --verbosity quiet /clp:ErrorsOnly` reproduces the same 56 errors in 0.29s. Representative roots are `DefenderAdapterProcessTests.cs:2` (`CS0234`, missing Defender `Configuration`), `DefenderAdapterProcessTests.cs:4` (`CS0234`, missing transitive `Cymulate.Integration.Client`), `DefenderIOCFlowAuthRetryTests.cs:2` (`CS0234`, missing transitive `Cymulate.Http.Package.Session`), `DefenderAdapterProcessTests.cs:7` (`CS0234`, missing transitive `Microsoft.Extensions.Logging`), and `DefenderIOCFlowBatchingTests.cs:101` (`CS0246`, missing `DefenderIOCFlow`). The remaining diagnostics are cascading missing-type/namespace errors at the exact consumer lines listed above.
- `git diff HEAD^1..HEAD -- ...Defender.Test.csproj` shows one relevant merge change: deletion of lines 20-22 containing `ProjectReference Include="..\..\..\Indicators\Cymulate.Integration.Adapters.Indicators.Defender\Cymulate.Integration.Adapters.Indicators.Defender.csproj"`. `git show HEAD^1:...Defender.Test.csproj` confirms the reference existed immediately before merge commit `a7b5121b548201fc8da9690a20ee7cde0419f80c`.

## Patterns to mirror
- Test-to-production project linkage → `src/Cymulate.Integration.Adapters/UnitTests/Indicators/Cymulate.Integration.Adapters.Indicators.CarbonBlack.Test/Cymulate.Integration.Adapters.Indicators.CarbonBlack.Test.csproj:19-21` — each indicator test project directly references its matching indicator project in a dedicated `ItemGroup`.
- Exact pre-merge Defender linkage → first parent of `a7b5121b`, `src/Cymulate.Integration.Adapters/UnitTests/Indicators/Cymulate.Integration.Adapters.Indicators.Defender.Test/Cymulate.Integration.Adapters.Indicators.Defender.Test.csproj:20-22` — restore the deleted `ProjectReference` unchanged.
- Internal test access → `src/Cymulate.Integration.Adapters/Indicators/Cymulate.Integration.Adapters.Indicators.Defender/Cymulate.Integration.Adapters.Indicators.Defender.csproj:16-18` — production already grants `InternalsVisibleTo` to the test assembly; no visibility changes are needed.

## Shared surface to freeze
- Freeze the existing production/test contract: test assembly name `Cymulate.Integration.Adapters.Indicators.Defender.Test`, production project path `Indicators/Cymulate.Integration.Adapters.Indicators.Defender/Cymulate.Integration.Adapters.Indicators.Defender.csproj`, and its current public/internal type signatures. The test project consumes production types and transitive dependencies through one `ProjectReference`; no package, namespace, signature, assertion, or test-body changes are justified.

## Disjoint sets available
- None — the evidence identifies one atomic MSBuild graph repair in `Cymulate.Integration.Adapters.Indicators.Defender.Test.csproj:20-22`; splitting production code, tests, or dependencies into separate work would manufacture scope rather than isolate it.

## Landmines
- Do not add direct references to `Cymulate.Integration.Client`, `Cymulate.Http.Package.Session`, or other `Cymulate.*` packages to the test project: `CLAUDE.md:14-16` says they arrive transitively through `Cymulate.IntegrationInfra`, and the missing transitive namespaces are symptoms of the deleted project edge.
- Do not edit the five test files or weaken their assertions. All 56 diagnostics collapse from the missing project edge; the test sources were not changed by this merge.
- Do not modify `Directory.Build.props`, generated files, `artifacts/`, or dependency lock/package state. The focused `--no-restore` reproduction proves restored local assets are sufficient, and `UnitTests/Directory.Build.props:21-29` protects against stale generated-file duplication.
- The relevant merge diff is unrelated to the Falcon feature payload: it accidentally deletes only the Defender test project's matching `ProjectReference`. Keep the repair to that deletion unless post-repair compilation exposes a non-cascading diagnostic.

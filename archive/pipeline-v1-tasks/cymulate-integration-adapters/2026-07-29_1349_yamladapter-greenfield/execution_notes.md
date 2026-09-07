# Execution Notes

## Contract-design measurements — established 2026-07-29T13:49:02Z, do NOT re-derive

Commands are named so any claim below can be re-run rather than trusted.

| what | measured value | how |
|---|---|---|
| sibling concerns | `Collectors`, `Indicators`, `Exclusions`, `QueryIntegration`, `Strategies`, `Shared`, `Tools`, `E2E`, `UnitTests` | `ls src/Cymulate.Integration.Adapters/` |
| capability types | 17 `IIndicatorCapability`, 7 `ICollectorCapability`, 1 `IExclusionCapability` | `grep -rhno 'IIntegrationAdapter<[A-Za-z]*>' --include='*.cs'` |
| concerns owning `Directory.Build.props` | `Collectors`, `Indicators`, `Exclusions`, `UnitTests` | `find . -maxdepth 2 -name Directory.Build.props` |
| in-repo readers of `IsCollector` | **0** | `grep -rn 'IsCollector' --include='*.cs' --include='*.csproj' --include='*.props' --include='*.json' --include='*.yaml' --include='*.yml'`, excluding the props that write it |
| business files to port | **18** | `find Collectors/YamlCollector -name '*.cs'` minus the vendored engine subtree, minus `obj`/`bin` |
| `YamlCollector.Test` files | 25 | `find … -name '*.cs'` |
| `YamlCollector.E2E` files | 5 | same |
| vendored `Yaml.Engine.Test` files | 73 | same — dies with `YamlCollector`, out of scope |
| `ai/` gitignored here | yes | `git check-ignore -v` exits 0; `git ls-files ai/` returns 0 |
| in-repo CI | two Slack-notification workflows only; no Jenkinsfile, no Dockerfile, no csproj glob | `find .github -type f`; `grep -rn 'Collectors\|IsCollector' .github/` |
| `UnitTests/` substructure | `Collectors`, `Exclusions`, `Indicators`, `Shared`, `Collectors.Tests.Infrastructure` | `ls UnitTests/` |
| `Glossary/` contents | `AdapterTopics.cs`, `CollectorGlobalDefaults.cs`, `CollectorNames.cs`, `CollectorZipNames.cs`, `IndicatorNames.cs` | `ls Shared/…/Glossary/` |
| entry-point delegation already in place | `YamlCollectorAdapter.cs:204` → `AdapterBusEntrypointRunner.RunAsync` | `grep -n` |
| `pwsh` / `powershell` on this machine | neither present | `which pwsh powershell` |
| ISB repo on this machine | absent | `ls -d /Users/user/Dev/*ntegration*ervice*us*` |
| git state | branch `test/apply-yaml-engine-nuget`, 9 files modified, uncommitted; `origin/dev` @ `032e0c3` | `git status --porcelain`, `git log -1 origin/dev` |
| prior task dir in this repo | `2026-06-11_1053_falcon-collector-refactor-cleanup` — **empty**, so no layout convention to inherit | `ls` |

## The finding that reshaped the parity gate

The gate originally proposed was an old-vs-new live vendor differential through `YamlLocalRunner`.
That was dropped after reading the tool:

- `Tools/…YamlLocalRunner/Program.cs:114` — constructs `IntegrationEngine` directly.
- `Tools/…YamlLocalRunner/Program.cs:71` — loads the definition itself via `loader.LoadFromString`.

So the runner never touches `YamlCollectorAdapter`, runtime discovery, the Shared orchestration
pipeline, checkpointing, or the sinks — which is exactly the 18 files this task ports. **The two live
vendor collections run earlier this session are evidence about the engine package and nothing else.**

What does exercise the host code, and its availability here:

| oracle | exercises | runnable here |
|---|---|---|
| `YamlCollector.Test` (25 files) | host types directly | **yes** — the gate |
| `dotnet build` over the solution | compilation, package resolution | **yes** |
| `run-local-yaml-smoke.ps1` | discovery → registry → Mediator → adapter → engine → HTTP, incl. `PartialWaitRequired` | **no** — no `pwsh`, no ISB repo, and stale `$bridgeProj` |
| `run-vendor-e2e.ps1` | the above plus real vendors and MinIO | **no** — also needs Docker |
| `E2E` project (`Category=VendorE2E`) | dockerized ISB stack | **no** |

Consequence recorded as A9/D14: this task can deliver unit-level parity evidence and must stop short
of the operator's kill criterion for `YamlCollector`.

## Pre-existing defect found during contract design

`run-local-yaml-smoke.ps1` and `run-vendor-e2e.ps1` both set `$bridgeProj` to
`Collectors\YamlCollector\Cymulate.Integration.Adapters.Collectors.YamlCollector\…csproj` — a nested
directory that does not exist. The csproj is one level up, at
`Collectors/YamlCollector/Cymulate.Integration.Adapters.Collectors.YamlCollector.csproj`.

Not introduced by this task. Report it regardless.

---

## Execution log

*(appended during execution)*

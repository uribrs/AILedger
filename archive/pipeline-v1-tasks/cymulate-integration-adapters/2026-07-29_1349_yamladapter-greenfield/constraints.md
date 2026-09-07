# Constraints

## Repo boundaries

- Work happens only in `/Users/user/Dev/cymulate-integration-adapters`.
- `/Users/user/Dev/yaml-adapter-engine` and `/Users/user/Dev/cymulate-magic-integration` are
  READ-ONLY; both must end with `git status --porcelain` empty.
- `dev` is the integration branch. Never push to `master`.
- The engine package is not modified, rebuilt, or re-versioned by this task.

## Runtime discovery — load-bearing

- `YamlAdapter/Directory.Build.props` must stamp `IsCollector=true` via `AssemblyMetadataAttribute`
  for the collector-capability assembly. The hosting platform discovers adapters by this flag
  (`CLAUDE.md:90`); a repo-wide grep finds zero in-repo readers, so inventing an `IsYamlAdapter` flag
  makes the assembly invisible.
- It must also import the parent `Directory.Build.props` with the same non-empty-relative-path +
  `Exists(...)` guard the sibling props files use. The comment in `Collectors/Directory.Build.props`
  explains the MSB4020 hazard that idiom avoids.
- It must carry its own version property in the sibling pattern (`Collectors/` uses
  `CollectorVersion`, default `5.1.1`; `Exclusions/` uses `ExclusionAdapterVersion`, default
  `1.0.1`).

## Project shape

- `PackageReference` to `Cymulate.Integration.Yaml.Engine` with **no inline version** — central
  package management is on; the version is pinned in `Directory.Packages.props`.
- No `ProjectReference` to engine source. No vendored engine subtree. No `DefaultItemExcludes` hack.
  The current `YamlCollector.csproj` has all three; removing them is the point of the task.
- TFM `net8.0`, though the installed SDK is 9.x. Never pass `-f net9.0`.
- `Nullable` and `ImplicitUsings` enabled.
- `InternalsVisibleTo` the test project — the established convention in this repo, deliberately
  unlike the engine repo.

## Architecture — from the adapters CLAUDE.md, not the engine's

- The engine repo's numeric rules (methods <100, classes <500, 4+ params → record, concepts as
  Contracts/Logic folders) are **not** binding here. This repo's conventions win inside it. Where the
  two differ, follow this repo and note the divergence.
- **The adapter entry point is thin.** `ProcessAsync` delegates to
  `Shared/.../Orchestration/AdapterBusEntrypointRunner` via an `AdapterBusEntrypointDefinition<T>`.
  `YamlCollectorAdapter.cs:204` already does this. Do not re-implement the pipeline. Read
  `Shared/.../Orchestration/README.md` before writing the entry point.
- Respect the five Shared subsystem boundaries (`Orchestration/`, `Session/`, `DataPipeline/`,
  `Recovery/`, `Resilience/`), each with its own README. Heuristic from `CLAUDE.md:68`: decides what
  to do next → Orchestration/Recovery; classifies a failure or picks a wait → Resilience;
  reads/transforms/writes a record → DataPipeline; concerns the wire → Session.
- **Adapters never sleep and never call scheduler primitives.** Long waits return
  `AdapterResult.PartialResult(delay, waitReason, data)`; the host owns the wait and re-invokes.
- The four-layer retry split at `CLAUDE.md:74-81` must survive intact — including the 60s
  `DefaultInProcessServerDelayThreshold` behaviour for high-volume vuln collectors and the 24h clamp
  on vendor-supplied delays. `YamlResilienceStrategyFactory.cs` is where it is encoded.
- `IResumableAdapter` and in-process transient retry inside the runner's Polly loop, so a long scan
  keeps its watermark.
- `Shared/.../Glossary/` is the single source of truth for strings (`AdapterTopics`,
  `CollectorNames`, `CollectorGlobalDefaults`, `CollectorZipNames`, `IndicatorNames`). Use the
  constants, never magic strings.
- Test output is redirected to repo-root `artifacts/bin|obj/ut/` by `UnitTests/Directory.Build.props`.
  Do not "fix" the in-project `obj`/`bin` excludes.

## Porting posture

- Greenfield means **rewrite the structure, port the load-bearing files close to verbatim**.
- Preserve "this is why" comments verbatim. `Execution/AwsAlcAssemblyResolver.cs` is the canonical
  case — it exists because the AWS SDK's reflective STS load fails inside the adapter's isolated
  `AssemblyLoadContext` under EKS IRSA. Do not rediscover production scars.
- Every one of the 18 files must be accounted for: ported, renamed, split, merged, or deliberately
  dropped. A dropped file requires naming the role it played and what performs that role now.

## Evidence discipline

- Every claim names the search or command that established it.
- Never write "latent", "unreachable", "no caller" or "dropped" without naming the check.
- Never write a number that was not measured — least of all in a commit message.
- Before writing "fixed": grep the symbol and count the sites.
- State what is being measured on each side of any comparison. A prior pass on this arc produced a
  false drift finding by comparing file line counts against class line spans.

## Secrets

- Credentials exist at `Tools/.../LocalAdapterRunner/appsettings.local.json` and
  `E2E/.../e2e-credentials.json`. **Never print a credential value** — key names and value lengths
  only.
- Credential keys come from the YAML definition's `authentication` block (`client_id_field:
  'clientId'`), **not** from the `config_fields` UI names (`client_id`). Getting this wrong yields
  "Required credential 'clientId' is missing or empty".

## Environment

- No `pwsh`/`powershell` on this machine (darwin). The ISB repo is not present. Docker-based E2E is
  therefore unavailable.
- Restore of `Cymulate.*` packages needs AWS CodeArtifact auth. A 401/403 on those is an auth issue,
  not a code issue — surface it as a `! aws codeartifact login ...` suggestion for the operator to
  run rather than guessing at a fix.

## Artifacts

- `ai/` is gitignored in this repo (measured: `git check-ignore` exits 0, zero tracked files under
  it). Task artifacts must be **current at completion**; do not require them to be inside a commit.

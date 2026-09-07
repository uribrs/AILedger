# Internal Recon

## Durable sources read
- `CLAUDE.md` — settles that YamlAdapter consumes `Cymulate.Integration.Yaml.Engine` only as a centrally pinned NuGet package, that the old `Collectors/YamlCollector` engine copy is independent, and that builds/tests target .NET 8 with CodeArtifact-backed restore.
- `/Users/user/Dev/yaml-adapter-engine/CLAUDE.md` — settles that engine public-contract changes must be checked against this adapter consumer, package publication is a separate concern, and claims about historical behavior require immutable Git evidence.
- `ai/active/2026-08-31_1217_yaml-adapter-build-regression/{task.md,prompt_contract.md,constraints.md,assumptions.md,decisions.md,execution_notes.md,state.json}` — bounds the work to a read-only two-repository regression investigation, requires parent/commit build evidence, and names the source-versus-package timing distinction.

## Files in scope
- `src/Cymulate.Integration.Adapters/YamlAdapter/Cymulate.Integration.Adapters.YamlAdapter/Definitions/ConnectionValidator.cs` — first adapter consumer of `OperationConfig.HandshakeFor` at lines 184 and 194 — inspected by: adapter pass/fail boundary and commit attribution.
- `src/Cymulate.Integration.Adapters/YamlAdapter/Cymulate.Integration.Adapters.YamlAdapter/YamlAdapter.cs` — forwards `adapterCategory` into connection validation in the same suspected change — inspected by: adapter commit boundary.
- `src/Cymulate.Integration.Adapters/UnitTests/YamlAdapter/Cymulate.Integration.Adapters.YamlAdapter.Test/YamlAdapterTests.cs` and `Fakes/AdapterTestHarness.cs` — capability-scoped handshake fixtures/tests introduced with the consumer change — inspected by: earlier-PR comparison and reproducible test boundary.
- `src/Cymulate.Integration.Adapters/UnitTests/YamlAdapter/Cymulate.Integration.Adapters.YamlAdapter.Test/Cymulate.Integration.Adapters.YamlAdapter.Test.csproj` — references the production adapter project and is the narrow test/build entry point — inspected by: immutable pass/fail builds.
- `src/Cymulate.Integration.Adapters/YamlAdapter/Cymulate.Integration.Adapters.YamlAdapter/Cymulate.Integration.Adapters.YamlAdapter.csproj` — package-only engine dependency at line 32, explicitly no engine `ProjectReference` — inspected by: package-consumer topology.
- `src/Cymulate.Integration.Adapters/Directory.Packages.props` — centrally pins `Cymulate.Integration.Yaml.Engine` to `1.0.0-preview.19` at line 138 — inspected by: package availability boundary.
- `/Users/user/.nuget/packages/cymulate.integration.yaml.engine/1.0.0-preview.19/lib/net8.0/Cymulate.Integration.Yaml.Engine.dll` and its sibling `.nupkg` — exact binary contract resolved by the adapter — inspected by: published-package evidence.
- `/Users/user/Dev/yaml-adapter-engine/src/Cymulate.Integration.Yaml.Engine/Definition/Contracts/Models/OperationConfig.cs` at `origin/dev` — declares `[YamlMember(Alias = "handshake_for")] HandshakeFor` at lines 70–71, introduced by `ef407cf` — inspected by: engine contract timing.
- `/Users/user/Dev/yaml-adapter-engine/src/Cymulate.Integration.Yaml.Engine/Definition/Schemas/integration.schema.json` at `origin/dev` — admits `handshake_for` at line 532 — inspected by: engine schema timing.
- `/Users/user/Dev/yaml-adapter-engine/src/Cymulate.Integration.Yaml.Engine/Compile/Logic/PlanBinder.cs` and `TaskInstantiation.cs` at `origin/dev` — carry `HandshakeFor` at lines 80 and 128 — inspected by: complete engine public-contract propagation.
- `/Users/user/Dev/yaml-adapter-engine/Directory.Build.props` across `cf5e6c1`, `ef407cf`, and `origin/dev` — records/controls package versioning while warning that CI can override `VersionSuffix` — inspected by: source/package release ordering.

## Patterns to mirror
- Consumer/package boundary → `src/Cymulate.Integration.Adapters/YamlAdapter/Cymulate.Integration.Adapters.YamlAdapter/Cymulate.Integration.Adapters.YamlAdapter.csproj:17` and `Directory.Packages.props:138` — prove behavior against the restored NuGet binary, not against a neighboring engine checkout.
- Narrow regression build → `src/Cymulate.Integration.Adapters/UnitTests/YamlAdapter/Cymulate.Integration.Adapters.YamlAdapter.Test/Cymulate.Integration.Adapters.YamlAdapter.Test.csproj:26` — building/testing this project necessarily compiles the adapter consumer and avoids unrelated solution failures.
- Public YAML contract propagation → engine `ef407cf:OperationConfig.cs:70`, `PlanBinder.cs:80`, `TaskInstantiation.cs:128`, and `integration.schema.json:532` — a new definition field is serialized, schema-accepted, and preserved through both plan-copy paths.
- PR attribution → adapter merge commits `a5b2e60b` (PR #350) and `80fad2bd` (PR #352) plus their first parents — distinguish the authored breaking commit (`54aef287`) from the merge that first places it on `dev` (`80fad2bd`).
- Publication chronology → engine `cf5e6c1`/merge `9a05fd0` (preview.19 lineage) before `ef407cf`/merge `1b765b9` (HandshakeFor lineage) — compare ancestry, timestamps, and package contents instead of relying on commit subjects.

## Shared surface to freeze
- `Cymulate.Integration.Yaml.Engine.Definition.OperationConfig.HandshakeFor : List<string>?`, YAML alias `handshake_for` — produced by engine source/schema/binders; consumed directly by adapter `ConnectionValidator.FindHandshakeKeys`.
- NuGet identity `Cymulate.Integration.Yaml.Engine` version `1.0.0-preview.19` — produced by the engine publication pipeline; selected centrally by adapter `Directory.Packages.props` and compiled through the package reference in YamlAdapter.
- Regression comparison points — adapter last-pass candidate `a5b2e60b` (PR #350 merge), authored consumer delta `54aef287` over `59ba3351`, and first `dev` merge candidate `80fad2bd` (PR #352); all build workers must use these immutable refs without moving either repository branch.

## Disjoint sets available
- adapter-boundary: adapter Git topology, `ConnectionValidator.cs`, `YamlAdapter.cs`, its test project/tests, and the unchanged preview.19 package pin — independent of: engine/package lineage except for the frozen package/type contract.
- engine-package-lineage: engine commits `cf5e6c1`, `9a05fd0`, `ef407cf`, `1b765b9`, `OperationConfig`/schema/binder propagation, and the preview.19 package binary — independent of: adapter PR topology except for the frozen package/type contract.
- synthesis-only: order the two timelines and distinguish authored commit, merge-to-`dev`, source availability, and package publication; no product files overlap because this task is investigative only.

## Landmines
- `/Users/user/Dev/yaml-adapter-engine` local `dev` is stale at `e1a5355`; fetched `origin/dev` is `1b765b9`. Read source through immutable refs or temporary worktrees, never from the checked-out branch by assumption.
- Engine `Directory.Build.props` explicitly says CI may override `VersionSuffix`; a source file saying preview.19 is not proof that a given commit produced the published preview.19 package. The restored `.nupkg`/DLL and ancestry are authoritative.
- `54aef287` is a normal commit on the PR branch; `80fad2bd` is the PR #352 merge into `dev`. Report both roles rather than calling either one the other.
- PR #350 merged as `a5b2e60b` and contains `59ba3351`, whose `ConnectionValidator` has no `HandshakeFor`; PR #352 adds the property reads. Rebuilding only current `dev` cannot establish this boundary.
- The old vendored engine/test surface under `Collectors/YamlCollector` has the same assembly identity but is explicitly independent. Do not use it to infer the contract available to YamlAdapter.
- Reuse of existing `obj/bin` or a mutable global package cache can blur the boundary. Use isolated temporary worktrees/output paths and verify the resolved preview.19 DLL/package identity for each build; do not edit either working tree.
- The engine `ef407cf` and adapter `54aef287` timestamps are only ten seconds apart, but timestamp proximity is not publication evidence. A new engine package version must exist before the adapter can consume that public member.

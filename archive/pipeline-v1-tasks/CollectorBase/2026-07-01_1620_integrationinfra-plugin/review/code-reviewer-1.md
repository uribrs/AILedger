# Code Review — Shared project → Cymulate.IntegrationInfra NuGet swap

**Scope reviewed:** `CollectorBase.slnx`, `Directory.Packages.props`, `nuget.config`, the 4 csprojs
(CollectorExecutor, Runner, Tests/Collectors.Tests.Infrastructure, Tests/CollectorExecutor.Test),
and the ~22 `.cs` files whose `using`/type identifiers were remapped. This is a mechanical
dependency swap; review targets behavior neutrality, consistency, dangling refs, and config smell.
Unrelated pre-existing CollectorBase code was **not** reviewed.

**Change type:** infrastructure / build-wiring + mechanical namespace rewrite.
**Risk level:** Medium (build/dependency graph is shared infra; the code delta itself is name-only).

## Verification performed (evidence, not inference)

- `dotnet build CollectorBase.slnx -c Debug` → **Build succeeded, 0 errors** (10 NU1507 warnings; see Minor-1).
- `dotnet test --no-build` → **96 passed, 0 failed, 0 skipped**.
- `dotnet nuget list source` from the repo dir → local feed `integrationinfra-local` is registered
  and **Enabled** (source #1), resolved from the relative path to
  `/Users/user/Dev/Uri/localprojects/IntegrationInfra/artifacts`; inherited `nuget.org` and
  `cym-dom/cym-repo-nuget` remain Enabled. No `<clear/>`, org feed intact.
- `grep` for the old namespace root `Cymulate.Integration.Adapters.Shared` across all `.cs` → **0 matches** (remap complete).
- All 11 distinct `Cymulate.IntegrationInfra.*` namespaces used by the code exist in the package
  (`.nuspec`/XML doc + successful compile against `Cymulate.IntegrationInfra.1.0.0-preview.2.nupkg`).
- Package `.nuspec` declares `Cymulate.Integration.Sdk >= 3.2.0` — the SDK bump is a **required**
  floor, not discretionary; 3.2.0 is present and the build/tests link against it with no API breakage.

Net: the swap is compilable, test-green, and behavior-preserving. No Blocker/Major issues found.

---

## Critical
None.

## Major
None.

## Minor

### Minor-1 — CPM + multiple package sources without source mapping (NU1507)
- **File:** `nuget.config` (interacting with `Directory.Packages.props` `ManagePackageVersionsCentrally=true`).
- **Problem:** Every project emits `NU1507: There are 2 package sources defined ... please map your
  package sources with package source mapping` (sources seen at build: `nuget.org`,
  `cym-dom/cym-repo-nuget`). With CPM on and >1 feed, NuGet wants explicit `<packageSourceMapping>`.
- **Impact:** Warning-only today. The real risk is a **dependency-confusion / resolution-ambiguity**
  surface: `Cymulate.*` package ids could in principle be served by more than one feed, and without
  mapping the winner is order/cache-dependent. For a `Cymulate.*`-heavy graph this is the one config
  smell worth closing before this leaves a developer's machine.
- **Recommended fix (local patch):** add `<packageSourceMapping>` — route `Cymulate.IntegrationInfra`
  (and, if desired, `Cymulate.*`) to the local/org feeds explicitly and `*` to nuget.org. Note the
  `<packageSources>` add here is *additive* to the inherited config, so the mapping must account for
  the inherited feeds too. Defer only if this file is strictly a throwaway local-dev convenience that
  never ships.

### Minor-2 — Local feed uses a relative path that is only valid from a specific checkout layout
- **File:** `nuget.config` — `value="../IntegrationInfra/artifacts"`.
- **Problem:** The relative path assumes `CollectorBase` and `IntegrationInfra` are **siblings**. It
  resolves correctly in this checkout (verified: expands to the sibling `IntegrationInfra/artifacts`
  and the `.nupkg` is present), and NuGet resolves it relative to the `nuget.config` location, so it
  is layout-portable *as long as the sibling invariant holds*. It breaks for anyone who clones the
  two repos elsewhere or into a nested layout.
- **Impact:** Local-dev only; CI/published consumers pull from the org feed. Low.
- **Recommended fix:** none required for a dev-only local source. If it is meant to be shared, document
  the sibling-checkout assumption or make it an env-var/`$(...)`-based path. Deferrable.

## Nit

### Nit-1 — Stale type name in a csproj comment
- **File:** `CollectorExecutor/CollectorExecutor.csproj` line 7 — comment says
  `CollectorNdjsonPublisher (real egress)`, but the code now calls `AdapterNdjsonPublisher`
  (`Execution/Steps/FetchStepExecutor.cs:212-213`). The rename didn't reach this prose comment.
- **Impact:** Cosmetic; comment drift.
- **Fix:** rename to `AdapterNdjsonPublisher` in the comment. Also `CollectorExecutorAdapter.cs`
  XML `<see cref="AdapterBusEntrypointRunner"/>` is correct; no action there.

### Nit-2 — "Shared" prose survives in comments/doc-summaries
- **Files:** `Runner/Runner.csproj:5`, `CollectorExecutor/CollectorExecutor.csproj:4`,
  `CollectorExecutorAdapter.cs` (lines 27, 80, 83, 188, 316), `Resilience/FailureSeam.cs:19`,
  `Execution/CollectorExecutorSessionFactory.cs:46`, `Execution/CollectorExecutorRequest.cs:4`.
- **Problem:** These say "the Shared substrate / Shared collector pipeline / Shared resilience". They
  are conceptual references to the shared-infra layer (now the IntegrationInfra package), not code
  identifiers, so nothing is broken. `CollectorExecutorAdapter`'s `Description` string (line ~80-83)
  is user-visible metadata that still reads "built on the Shared substrate + http.package".
- **Impact:** Cosmetic / mild staleness in a surfaced description string.
- **Fix:** optional wording refresh ("IntegrationInfra substrate"). Not merge-blocking.

## Observations (no action required)

- **Rename is selective and correct, not blanket.** Package types that were renamed `Collector*`→`Adapter*`
  (e.g. `AdapterResult`, `AdapterFailureDecision`, `AdapterNdjsonPublisher`, `AdapterBusEntrypointRunner`)
  are consistently remapped, while (a) local domain types keep their names
  (`CollectorExecutorAdapter`, `CollectorExecutorRunner`, `CollectorExecutorCheckpointManager`,
  `CollectorExecutorStepHelpers`, `CollectorExecutorRequest`, …) and (b) package types that
  legitimately retained the `Collector` prefix (`CollectorResultPayload`, `CollectorResumeRunner`,
  `ICollectorBusEntrypointSource`, `CollectorBusEntrypointDefinitionBuilder`) are left intact. All
  confirmed present in the package XML doc and resolved by the compiler. No half-renamed identifier,
  no accidental over-rename detected.
- **Local project namespace unchanged.** The `.cs` files keep
  `namespace Cymulate.Integration.Adapters.Collectors.CollectorExecutor...` (the repo's own
  `RootNamespace`). Only `using` directives pointing at the moved *package* namespaces changed. Correct.
- **`slnx` change is a clean folder-entry removal.** The former `Shared` project entry is gone;
  `Shared/` no longer exists on disk. Remaining entries (Tests folders, CollectorExecutor, Runner,
  Strategies) are intact and all build.
- **`Strategies.csproj` was not in the 4 edited csprojs and needs no edit.** It has **no** direct
  `Cymulate.IntegrationInfra` PackageReference yet its `Resilience/*.cs` use IntegrationInfra types;
  it obtains them **transitively** via its `ProjectReference` to CollectorExecutor (whose
  PackageReference has no `PrivateAssets`, so assets flow through). This is the *same* transitive shape
  it had under the old in-solution `Shared` ProjectReference — preserved, not a regression. If you ever
  want the dependency explicit, add a direct version-less `PackageReference`; not required.
- **CPM wiring is correct.** All four edited csprojs use version-less
  `<PackageReference Include="Cymulate.IntegrationInfra" />`; the single `<PackageVersion>` lives in
  `Directory.Packages.props` at `1.0.0-preview.2` (matches the on-disk `.nupkg`). SDK pinned centrally
  at 3.2.0. No per-project version leaked in.
- **SDK bump risk is nil in practice.** 3.1.8→3.2.0 is mandated by the package's dependency floor;
  build + 96 tests link and pass against 3.2.0, so no consumed SDK API broke.

---

## Severity summary
- Critical: 0
- Major: 0
- Minor: 2 (NU1507 source-mapping under CPM; relative local-feed path assumes sibling checkout — both local-dev scoped)
- Nit: 2 (stale `CollectorNdjsonPublisher` comment; "Shared" prose in comments + one description string)

**Verdict:** Mechanical swap is sound — compiles clean, 96/96 tests pass, namespace/type rewrite is
consistent and behavior-preserving, no dangling refs or dead usings, nuget.config correctly adds the
local feed without disturbing the inherited org feed. Only cleanup-grade items remain; the sole one
worth acting on before broader use is the CPM source-mapping warning (Minor-1).

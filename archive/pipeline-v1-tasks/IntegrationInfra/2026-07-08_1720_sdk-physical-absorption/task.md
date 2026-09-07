# Task: Physical Absorption of the ISB SDK Source

Repo: /Users/user/Dev/IntegrationInfra, branch `absorb/sdk-source` (already cut from dev @ db6d722).
PHYSICAL move only — namespaces and PackageId unchanged; convergence explicitly deferred by operator.

## Move

- `/Users/user/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Sdk/Cymulate.Integration.Sdk`
  (120 .cs, leaf: deps only M.E.DependencyInjection.Abstractions 10.0.9 + M.E.Logging.Abstractions 10.0.9 +
  Polly 8.7.0; no InternalsVisibleTo) → `src/Cymulate.Integration.Sdk/` as a second packable project.
- `.../UnitTests/Cymulate.Integration.Sdk.UnitTests` (1 file, 9 tests) → `tests/Cymulate.Integration.Sdk.UnitTests/`.
- Wire both into `IntegrationInfra.slnx`.

## Packaging

- SDK keeps its 3.x lineage at **3.2.1**. `src/Directory.Build.props` sets Version=1.0.0-preview.3 for
  everything under src/ — the SDK project dir gets its own `Directory.Build.props` overriding Version=3.2.1.
  MSBuild caveat: a lower Build.props does NOT chain to the parent automatically — either Import the parent
  (`GetPathOfFileAbove`) or stand alone deliberately; executor decides + records.
- Strip the adapter cosplay from ISB's Sdk-level props (`AdapterVersion`, `IsAdapter=true` assembly metadata) —
  a contracts package is not an adapter. Recreate only packaging needs (IsPackable, XML docs, snupkg, license).
- `RepositoryUrl` → `https://cymulate-corp.ghe.com/cymulate-rnd/IntegrationInfra` (current value points at a
  repo the SDK never lived in).

## Wiring

- `Directory.Packages.props`: ADD the SDK's three pins (no conflicts — grounded: Infra pins neither Polly nor
  the two M.E.Abstractions); REMOVE the `Cymulate.Integration.Sdk 3.2.0` feed pin once nothing package-references
  it (executor verifies no test csproj pins the SDK directly).
- `IntegrationInfra.csproj`: `PackageReference Cymulate.Integration.Sdk` → `ProjectReference`. `dotnet pack`
  must then declare a package dependency on Cymulate.Integration.Sdk (>= 3.2.1).

## Key risk

Infra compiled against FEED 3.2.0; the source is 3.2.1. Building against source surfaces any 3.2.0→3.2.1
delta as compile/test failures — SURFACE these, do not patch SDK code (verbatim constraint).

## Docs

README + ARCHITECTURE.md: one repo, two packages, two version lines. DESIGN.md "Concerns, not layers": add a
clarifier (SDK is a separate CONTRACT package, not a concern split; Infra concerns remain one assembly).
CHANGELOG [Unreleased] entry.

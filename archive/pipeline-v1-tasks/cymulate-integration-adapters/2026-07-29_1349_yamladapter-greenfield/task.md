# Task — greenfield YamlAdapter concern

## What

Create a new top-level adapter concern `YamlAdapter/` in
`src/Cymulate.Integration.Adapters/`, peer to `Collectors/`, `Indicators/`, `Exclusions/` and
`QueryIntegration/`. Inside it, build one greenfield project that:

- consumes the YAML execution engine **as a NuGet package**, treated as though it already ships from
  CodeArtifact;
- holds the business concerns that are not the engine — the 18 `.cs` files currently in
  `Collectors/YamlCollector/` outside the vendored engine subtree.

## Why a peer and not another collector

Three SDK capability types exist in this repo (17 `IIndicatorCapability`, 7 `ICollectorCapability`,
1 `IExclusionCapability`). A YAML-defined integration is not inherently a collector — the same
engine can serve exclusions or indicators. The folder is shaped so those can join later; only the
collector capability is built now.

## Why now

The engine is a package. `Collectors/YamlCollector/` still carries the engine as a vendored source
subtree behind a `DefaultItemExcludes` hack plus a `ProjectReference`. That arrangement is what this
task replaces.

## Scope

**In:** the `YamlAdapter/` folder and its `Directory.Build.props`; one project serving the collector
capability; the 18 ported business files, reshaped; a test project; solution wiring; giving the
existing PowerShell harnesses a code path to the new project.

**Out:** ISB dispatch, routing and platform registration. Any change to the engine package. The
`assets`/`findings` topic parameterization. Deleting `YamlCollector` — it dies later, on the
operator's word, once parity is satisfied. YAML exclusions/indicators adapters.

## Deliverables

1. The `YamlAdapter/` concern, building clean.
2. A test project, with the existing 25-file `YamlCollector.Test` suite pointed at the new project.
3. `parity_report.md` — what was verified by execution with measured numbers, and separately what
   could not be verified on this machine and what would verify it.

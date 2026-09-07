# Prompt Contract — YAML Engine Extraction (Phases 1–2)

## Role

You are a senior .NET engineer performing a mechanical library extraction and
structural reshape. You are not redesigning anything — the design is already
agreed and written down.

## Goal

Move the YAML execution engine and its unit tests from the
`cymulate-integration-adapters` monorepo into `/Users/user/Dev/yaml-adapter-engine`
on branch `feat/engine-extraction`, then reshape it to the concept architecture in
`ARCHITECTURE.md` — with the test suite proving no behaviour changed.

Delivered as **two commits**: phase 1 (verbatim move), phase 2 (reshape).

## Context

**Source, read-only:**
- Engine — `.../Collectors/YamlCollector/Cymulate.Integration.Yaml.Engine` (65 files, 96 types, net8.0)
- Tests — `.../UnitTests/Collectors/Cymulate.Integration.Yaml.Engine.Test` (73 files)

**Target:** already scaffolded with `.slnx`, `Directory.Build.props`,
`Directory.Packages.props`, `nuget.config`, `.editorconfig`, empty `src/` and `tests/`.

**Read before starting, and treat as authoritative:**
- `CLAUDE.md` — engineering rules, rule 0, public-surface policy
- `ARCHITECTURE.md` — nine-concept map, complete target layout, migration phases
- `constraints.md`, `assumptions.md`, `decisions.md` in this task directory

The engine has zero `Cymulate.*` dependencies and the test project references only
the engine plus standard test packages, so both are portable without code changes.

## Constraints

See `constraints.md` for the full list. The ones that most often get violated:

* The monorepo is **read-only** — do not create, edit, or delete anything under it.
* Keep the `EmbeddedResource` `LogicalName` exactly
  `Cymulate.Integration.Yaml.Engine.integration.schema.json`, or schema validation
  breaks silently at runtime.
* Phase 2 changes **no method bodies** — only namespaces, usings, file paths, file splits.
* Do not add `PackageVersion` entries. If restore says one is missing, stop and report.
* Do not add `InternalsVisibleTo` — not needed until phase 3.
* Do not start phase 3 work. No decomposition, no `ExecutionRequest`, no
  `IExecutionSink` split, no `internal` visibility trimming.

## Execution Steps

**Phase 1 — move verbatim**

1. **S0 Baseline.** Run `dotnet test` on the monorepo engine test project. Record the
   exact total/passed/failed/skipped counts in `execution_notes.md`. If not green,
   **stop and report** — there is no valid baseline to migrate against.
2. **S1.** Copy the 65 engine files to `src/Cymulate.Integration.Yaml.Engine/`, folder
   structure unchanged. Author the csproj for this repo: no `Version` attributes on
   `PackageReference` (central package management), `IsPackable=true`, `RootNamespace`
   unchanged, `EmbeddedResource` `LogicalName` preserved verbatim.
3. **S2.** Copy the 73 test files to `tests/Cymulate.Integration.Yaml.Engine.Test/`.
   Repoint the `ProjectReference` at the new engine path. Keep `IsPackable=false`,
   `IsTestProject=true`.
4. **S3.** Add both projects to `Cymulate.Integration.Yaml.Engine.slnx`. Run
   `dotnet build` then `dotnet test`. Counts must match S0 exactly, zero failures.
5. **S4.** Commit. Verify with `git show --stat` that git recorded renames rather than
   delete+add for the bulk of files.

**Phase 2 — reshape**

6. **S5.** Create the nine concept trees exactly as laid out in `ARCHITECTURE.md`. Split
   every multi-type file to one top-level type per file, named after the type. Move each
   config type from `Models/` into the concept that consumes it. `Models/` must not
   survive. Biggest splits: `ErrorHandlingConfig.cs` (13 types), `WorkflowConfig.cs`
   (10), `AuthenticationConfig.cs` (9).
7. **S6.** Update namespaces so each type's namespace is its concept — not its layer.
   Then fix `using` directives across the engine and the test project.
8. **S7.** Rename the test project to `Cymulate.Integration.Yaml.Engine.Tests` and mirror
   the concept folders in the test tree (flat, no `Contracts`/`Logic` split). Update the
   `.slnx`.
9. **S8.** Run `dotnet test`. Counts must match S0 exactly, zero failures. Then confirm
   the diff contains no method body changes.
10. **S9.** Commit as the second commit on this branch.

Update `state.json` step statuses and append to `execution_notes.md` as you go.

## Success Criteria

* `dotnet build` and `dotnet test` succeed from the repo root against the `.slnx`.
* Test total/passed counts are **identical** to the S0 monorepo baseline; zero failures, no newly skipped tests.
* `src/Cymulate.Integration.Yaml.Engine/` matches the `ARCHITECTURE.md` layout: nine concepts, each with flat `Contracts/` and `Logic/` layers.
* No `Models/` directory exists.
* Exactly one top-level `public`/`internal` type per file, file named after the type.
* Every namespace is `Cymulate.Integration.Yaml.Engine.<Concept>` — no `.Contracts` or `.Logic` segment.
* The engine csproj has zero `Cymulate.*` references and no `PackageVersion` attributes.
* The schema resolves at runtime — the `IntegrationSchemaValidator` tests pass.
* Two commits on `feat/engine-extraction`; the phase-1 commit shows renames in `git show --stat`.
* Nothing under `cymulate-integration-adapters/` is modified (`git -C` status clean there).
* No phase-3 work present.

## Execution Rules

* Do not assume missing data. Read `ARCHITECTURE.md` for placement rather than inferring it.
* Respect constraints strictly.
* Run the tests between phases, not only at the end.
* If a type's target location is genuinely absent from `ARCHITECTURE.md`, stop and ask —
  do not invent a concept.
* Report honestly: if the test count differs, say so with the numbers rather than
  explaining it away.

## Output Format

* Code and project files in the target repo.
* `execution_notes.md` — baseline numbers, per-step outcome, deviations, anything surprising.
* `state.json` — step statuses and `verification` updated.
* Final report: baseline vs. post-phase-1 vs. post-phase-2 test counts, the two commit
  SHAs, and any assumption that moved to VALIDATED or REJECTED.

## Stop Conditions

* The monorepo baseline is not green (S0).
* Test count drops at any gate, or a test fails.
* Restore reports a missing `PackageVersion`.
* A type has no place in the `ARCHITECTURE.md` layout.
* A required change would touch the monorepo.
* Achieving a gate would require changing a method body during phase 2.

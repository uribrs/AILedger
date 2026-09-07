# Task — YAML Engine Extraction (Phases 1–2)

Move the YAML execution engine out of the `cymulate-integration-adapters`
monorepo into the standalone `yaml-adapter-engine` repo, and reshape it to the
agreed concept architecture, so it can later ship as a NuGet package.

## Source (read-only)

| What | Path | Size |
|---|---|---|
| Engine | `cymulate-integration-adapters/src/Cymulate.Integration.Adapters/Collectors/YamlCollector/Cymulate.Integration.Yaml.Engine` | 65 files, 11,271 lines, 96 types |
| Tests | `cymulate-integration-adapters/src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Yaml.Engine.Test` | 73 files |

The monorepo is **not** modified by this task.

## Target

`/Users/user/Dev/yaml-adapter-engine`, branch `feat/engine-extraction`.
Already scaffolded: `.slnx`, `Directory.Build.props`, `Directory.Packages.props`,
`nuget.config`, `.editorconfig`, empty `src/` and `tests/`.

## Authoritative design (already agreed — read, do not re-derive)

- `CLAUDE.md` — the engineering rules, including rule 0 (no `Cymulate.*`
  dependencies) and the public-surface policy.
- `ARCHITECTURE.md` — the nine-concept map, the complete target file layout with
  every existing type placed, the rule-violation inventory, and the three-phase
  migration plan.

## Scope

**Phase 1 — move verbatim.** Both projects into `src/` and `tests/`, project
reference repointed, both wired into the `.slnx`. No content changes, so git
records renames.

**Phase 2 — reshape.** Concept folders with `Contracts/` and `Logic/` layers,
one top-level type per file, `Models/` dissolved into consuming concepts,
namespaces updated. Strictly mechanical — no method body changes.

## Out of scope

- Phase 3: decomposing `IntegrationEngine` / `WorkflowRunner`, introducing
  `ExecutionRequest` / `PageOutcome`, splitting `IExecutionSink`, trimming the
  public surface to `internal`.
- Modifying the consuming adapter in the monorepo.
- Publishing the package; CI setup.

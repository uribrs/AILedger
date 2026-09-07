# Constraints

## Hard — violating any of these fails the task

- The engine project must have **zero `Cymulate.*` package references** (CLAUDE.md rule 0).
- `integration.schema.json` stays an `EmbeddedResource` with `LogicalName` exactly
  `Cymulate.Integration.Yaml.Engine.integration.schema.json`. Changing it makes
  `IntegrationSchemaValidator` fail to resolve the schema at runtime, silently.
- The monorepo is read-only. No file under `cymulate-integration-adapters/` is created,
  edited, or deleted.
- Phase 2 changes **no method bodies**. Only namespace lines, `using` lines, file
  locations, and file splits. Any behavioural change belongs to phase 3.
- Test count after phase 1 and after phase 2 must equal the S0 monorepo baseline,
  with zero failures. A dropped or skipped test is a failure, not a rounding error.
- Root namespace stays `Cymulate.Integration.Yaml.Engine`; assembly name and
  `PackageId` are unchanged.

## Structural — from CLAUDE.md and ARCHITECTURE.md

- Every concept folder has exactly two code layers: `Contracts/` and `Logic/`. Both
  are flat — no subfolders inside a layer.
- Namespace tracks the **concept**, not the folder. `Pagination/Contracts/IPaginator.cs`
  and `Pagination/Logic/CursorPaginator.cs` are both `…Engine.Pagination`.
- One top-level `public`/`internal` type per file; file named after the type. Private
  nested types are exempt.
- Exceptions crossing a concept boundary live in `Contracts/`; internal control-flow
  exceptions (`WorkflowWaitRequested`) stay in `Logic/`.
- `Models/` must not exist after phase 2.
- Dependency direction is one-way: `Workflow` → `Execution` → primitives. Nothing
  references `Execution` or `Workflow`.

## Build and tooling

- `net8.0`, nullable enabled, central package management already configured. Do not
  add `PackageVersion` entries unless a genuinely new dependency appears — and if one
  does, stop and report rather than adding it.
- No `InternalsVisibleTo` is required and none exists today; engine tests currently
  exercise only the public surface. Do not add one in phases 1–2.
- Test project is `IsPackable=false`, `IsTestProject=true`.
- The engine project opts into packing (`IsPackable=true`); the repo default is false.

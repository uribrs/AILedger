# Decisions

- Two commits, not one — phase 1 (move) and phase 2 (reshape) land separately, so the reshape diff is reviewable on its own.
- Phase 1 is verbatim: folder structure and file contents unchanged, so git records renames rather than delete+add.
- Namespace tracks the concept, not the folder; `IDE0130` disabled in `.editorconfig` to stop the IDE fighting it.
- `Models/` is dissolved rather than renamed — each config type moves to the concept that consumes it.
- `Retry/` becomes `Resilience/` — the folder holds classification and error rules, not just retry.
- `HttpTraceEntry` goes to `Execution/Contracts`, not a standalone `Diagnostics` concept — a one-type concept is ceremony.
- Test project renamed `.Test` → `.Tests` in phase 2, not phase 1, to keep the move commit clean.
- Engine project opts into `IsPackable=true`; repo default stays `false` so nothing else packs by accident.
- Phase 3 stays out of this branch — mixing mechanical relocation with behavioural surgery produces an unreviewable diff.
- Baseline test count is measured before the move (S0), not asserted afterwards.
- `ai/active/` and `ai/done/` added to `.gitignore`, matching monorepo convention.

## Post-review revisions (2026-07-28, from verifier-1)

- **REVERSED:** `HttpTraceEntry` moves to its own `Diagnostics` concept. The earlier call ("a one-type concept is ceremony") was wrong — all eight authenticators emit traces, so parking the record in `Execution` made `Authentication` depend on the orchestrator that calls it. The boundary was load-bearing; ten concepts, not nine.
- The dependency rule is restated at the **Logic** layer. `Contracts/` are a shared vocabulary and may reference each other freely — the YAML document is one connected graph. The original "no concept may reference Execution or Workflow" was unsatisfiable given the layout it accompanied.
- Dependency audits must be done by **type reference, not `using` directive** — namespace-is-concept means a using imports both layers and over-reports.
- `Schemas/` moved to `Definition/Schemas/` to match ARCHITECTURE.md rather than amending the doc to match the code.

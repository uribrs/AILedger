# Constraints

## Invariants

- Rule 0: zero `Cymulate.*` references in the engine project.
- `/Users/user/Dev/cymulate-integration-adapters` is **read-only**. No file there is created, edited
  or deleted. `git status --porcelain` in that repo stays empty.
- No host-visible signature changes. The adapter calls exactly three engine entry points, verified by
  grep: `ExecuteOperationAsync` 6p+ct (`YamlOperationRunner.cs:386`), `ExecuteOperationAsync` 4p+ct
  (`:518`), `WorkflowRunner.RunAsync` 8p+ct (`:245`). None of C1–C3 touches them.

## Structure

- One top-level type per file, file named after the type. Applies to every new record.
- A new contract type belongs in its own concept's `Contracts/Models/`, never a shared bucket.
- Default to `internal`. A new type is `public` only when a `public` signature forces it, and that
  case is recorded as a decision rather than assumed.
- No parameter object whose members each consumer only partly uses. `CLAUDE.md` rule 5 and the
  existing note at `WorkflowRunner.cs:442-455` both name that grab-bag as the thing the rule exists
  to prevent. Compose records; do not flatten.

## Scope

- In scope: only the files named in C1, C2, C3, their call sites, and the tests that drive them.
- Out of scope, and touching any of them is a defect and not initiative:
  `IntegrationEngine.ExecuteOperationCoreAsync` and its 19 rule-5 violations; `WorkflowRunner.RunAsync`;
  `YamlIntegrationLoader`'s size; the public-surface narrowing (101 public types, 32 in `Logic/`);
  the `IIntegrationEngine` single-implementation question; CI; `LICENSE`; `XmlShapingConfig.ToOptions`;
  `MergePlan.Create`; the 56 undocumented rule-5 deviations outside C1–C3.

## Gates

- Tests after **each** commit: ≥ 709 passed, 0 failed, 0 newly skipped.
- Review after **each** commit, before the next begins: 1 verifier pass + 2 independent
  code-reviewer passes.
- Reviewers receive minimal context only: no contract, no plan, no verifier output, no user request,
  and no mention that a rule audit motivated the change. The two code reviewers do not see each
  other's output.
- Determinism at end of task: ≥ 12 runs with `--logger trx`, parsed from the trx files rather than
  read off the console, zero failures across all runs.

## Documentation

- `progress_log.md` exists **before** the first commit and is updated **in** the same commit as each
  piece of work.
- `CHANGELOG.md` is updated **in** each of the three commits, not at the end.
- `state.json` step statuses are correct at every commit boundary.
- `defect_register.md` records findings as they surface. A defect folded into a commit message
  instead is one nobody can audit.
- Every commit message carries the measured before/after violation count for that commit and the
  test count. No number that was not measured.

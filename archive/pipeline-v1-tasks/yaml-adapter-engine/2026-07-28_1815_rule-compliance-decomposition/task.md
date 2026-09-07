# Task — Rule-Compliance Decomposition (three commits)

Close 25 of the 74 measured `CLAUDE.md` rule violations in `src/`, in three independent commits,
without touching the engine core.

## The three commits

| # | Target | Closes | Blast radius |
|---|---|---|---|
| C1 | `Resilience/Logic/EngineFailureClassifier.cs` | 5 × rule 5 | 3 call sites, all in `IntegrationEngine.cs` |
| C2 | The `IPaginator` contract (interface + 7 implementations) | 16 × rule 5 | 3 in `src`, **52 in `tests`** |
| C3 | `Workflow/Contracts/Models/MergeIntoConfig.cs` | 3 × rule 1, 1 × rule 5 | 2 call sites, both in `src` |

They are independent: no file appears in more than one commit, and no commit depends on another's
outcome. C1 first because it is the smallest and cannot reach the test suite's call sites; C2 second
because it is the largest by call-site count; C3 last.

## What this task is not

The three largest violations in the repository are deliberately excluded:
`IntegrationEngine.ExecuteOperationCoreAsync` (725 lines, 19 rule-5 violations),
`WorkflowRunner.RunAsync` (155 lines), `YamlIntegrationLoader` (655 lines). Also excluded: the
public-surface narrowing, the `IIntegrationEngine` single-implementation question, CI, `LICENSE`,
and the 56 undocumented rule-5 deviations outside C1–C3.

Rule 3 and rule 4 counts are therefore expected to be **unchanged** at the end of this task. Every
method over 100 lines and every class over 500 lines is out of scope.

## Deliverables

Three commits, each carrying its own code, test updates, `CHANGELOG.md` entry, `progress_log.md`
update and correct `state.json` step status. Plus `defect_register.md`, and a determinism run at the
end.

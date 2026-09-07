# Task — SRP Decomposition (task 2 of 3)

Decompose the three classes that hold the engine's remaining structural debt, for `CLAUDE.md`
rules 3, 4 and 7. SRP is the objective. Parameter counts must fall out of the resulting seams —
they are not targeted directly.

## Targets, measured 2026-07-29

| file | lines | largest method | rules |
|---|---|---|---|
| `Execution/Logic/IntegrationEngine.cs` | 1,941 | `ExecuteOperationCoreAsync` 729 | 3, 4, 7, 19 × 5 |
| `Workflow/Logic/WorkflowRunner.cs` | 952 | `RunAsync` 155 | 3, 4, 7, 8 × 5 |
| `Definition/Logic/Loading/YamlIntegrationLoader.cs` | 672 | none over 100 | 4, 7, 2 × 5 |

`ARCHITECTURE.md`'s outstanding-debt table understates all three (1,902 / 912 / 655). Its rule
counts are from C3; its file sizes appear to be from C1. Corrected in S0.

## Steps

| # | scope | src change |
|---|---|---|
| S0 | `rules_audit` + dependency-rule check, committed into the repo | no |
| S1 | `OperationPlanFactory`, `OperationPlan`, `OperationRun` | yes |
| S2 | `PageLoop`; `PageOutcome` replaces `loopEndedViaBreak` | yes |
| S3 | page-loop leaves + `OperationResultFactory` | yes |
| S4 | `ExecutionRequest`/`ExecutionOptions`; `ExecuteStageAsync` onto `IIntegrationEngine` | yes |
| S5 | `StageExecutor`, `MergeCoordinator` | yes |
| S6 | `YamlIntegrationLoader` split by responsibility | yes |

`Execution` before `Workflow` per `ARCHITECTURE.md`'s phase-3 order, and because S5's seam depends on
S4 putting `ExecuteStageAsync` on the interface. **Checkpoint after S4** — S0–S4 is plausibly a full
task, and re-scoping there beats running seven steps on one contract.

## What this task is not

- Not the public-surface narrowing (~102 public types), the `InternalsVisibleTo` decision, or D16.
  That is task 3.
- Not a parameter-count exercise. If a step closes rule-5 violations without reducing the number of
  responsibilities in the type, it has done the wrong thing.
- Not a rewrite. Behaviour preservation is the load-bearing claim on every step.

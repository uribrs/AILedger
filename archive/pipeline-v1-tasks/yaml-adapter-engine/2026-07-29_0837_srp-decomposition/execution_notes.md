# Execution Notes — SRP Decomposition

Appended per step during execution. Per-step outcome, deviations from the contract, and anything
surprising. Created at contract design so it exists before the first commit — task 1's `progress_log.md`
did not, on the predecessor branch, and the defect register absorbed what belonged here.

---

## Contract-design measurements, recorded so execution does not repeat them

Measured 2026-07-29T08:37:42Z at `df5851d`, tree clean:

| what | value | how |
|---|---|---|
| tests | 710 passed / 0 failed / 0 skipped, ~0.9 s | `dotnet test` |
| `IntegrationEngine.cs` | 1,941 lines | `wc -l` |
| `ExecuteOperationCoreAsync` | 729 lines | brace count, agrees with D8's independent 729 |
| `WorkflowRunner.cs` | 952 lines | `wc -l`; agrees with retrospective `:139` ("grew 923 → 952") |
| `RunAsync` | 155 lines | brace count |
| `YamlIntegrationLoader.cs` | 672 lines, 0 methods > 100 | `wc -l` + brace count |
| `src` / test `.cs` files | 127 / 77 | `find` |
| CI | none — no `.github/`, no `Jenkinsfile`, no workflow YAML | `find` + `ls` |
| adapters repo | `git status --porcelain` empty | direct |

### A measurement error made during contract design, kept visible

My ad-hoc brace counter reported `ReadStringInput` at 130 lines and `ReadStringListInput` at 127 — two
apparent rule-3 violations that would have raised the ledger's count from 3 to 5.

Both are false. `IntegrationEngine.cs:1734-1738` shows them as expression-bodied one-liners; because
their declaration line carries no `{`, the counter ran forward to an unrelated closing brace.
`ExecuteHydrateAsync`'s reported 120 lines is unverified for the same reason and is **not** recorded as a
violation anywhere in these artifacts.

Kept here rather than deleted because it is the task's own thesis demonstrated at contract time: an
unreliable instrument produced a number that read as a finding, in the direction of "more debt found."
It is also the concrete argument for S0 — no figure in these artifacts is authoritative until the
committed `rules_audit` reproduces it.

### Ledger drift found at contract design

`ARCHITECTURE.md`'s outstanding-debt table understates all three targets: 1,902 / 912 / 655 against
measured 1,941 / 952 / 672. The table carries an as-of marker of C3, but its file sizes appear to date
from C1 while its rule counts date from C3. Third recurrence of the same staleness (D8 fixed six wrong
numbers, D13 recorded it going stale one commit later). S0 corrects it.

---

## S0 — instruments

_pending_

## S1 — OperationPlanFactory / OperationPlan / OperationRun

_pending_

## S2 — PageLoop / PageOutcome

_pending_

## S3 — page-loop leaves / OperationResultFactory

_pending_

## S4 — ExecutionRequest / ExecutionOptions — CHECKPOINT

_pending_

## S5 — StageExecutor / MergeCoordinator

_pending_

## S6 — YamlIntegrationLoader

_pending_

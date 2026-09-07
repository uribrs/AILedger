# Task — Decompose CollectorExecutorRunner.cs

Decompose `CollectorExecutor/Execution/CollectorExecutorRunner.cs` (811 lines, 9 responsibilities)
into concern-based helper classes. **Strictly behavior-preserving** — a pure structural refactor,
no logic or feature changes.

User reviewed and signed off the FULL decomposition (see `prompt_contract.md` for the target
layout). The runner becomes a ~150-line conductor; step executors, the run-scoped scope object, the
HTTP request sender, the outcome factory, the failure-resolution runner, and the connection probe
move into their own files under `Execution/Steps/` and `Execution/Outcomes/`.

Out of scope this pass: `CollectorExecutorStepHelpers` (separate grab-bag), any logic change, any
public adapter-surface signature change.

Safety net: the 88 existing tests. Build clean + 88/88 green after **each** extraction step.

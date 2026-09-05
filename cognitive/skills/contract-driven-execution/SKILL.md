---
name: contract-driven-execution
version: 1.5.0
description: Direct-path executor for non-trivial work that has a finalized prompt contract. Performs the implementation, refactoring, research-driven coding, or agent-workflow execution against `prompt_contract.md` and updates task state. Typically invoked by `task-orchestrator` on the direct execution path. May also be used standalone when continuing an existing task under `ai/active/` where decomposition into workers is not warranted.
---

# Contract Driven Execution

## Purpose

Execute work against an explicit contract. Keep execution tied to task state instead of vague instructions.

This skill is the **executor**, not the planner. It does not decide whether to decompose, whether to research, or whether the contract is the right shape. Those decisions belong to `task-orchestrator`. If you find yourself making them here, stop.

The execution contract lives in `prompt_contract.md`. Local task state lives under:

```text
ai/active/<timestamp>_<task-slug>/
```

## Inputs

- `taskPath` — path to `ai/active/<timestamp>_<task-slug>/`.

This skill assumes a valid contract already exists at `taskPath`. If `prompt_contract.md` is missing or has empty Constraints or Success Criteria, stop and surface that to the caller. Do not attempt to design the contract from inside this skill.

## Core Rule

Execute strictly within the contract. Do not redesign scope. Do not work around constraints. Do not introduce changes outside the goal stated in `prompt_contract.md`.

If the contract is missing, the caller (typically `workflow-coordinator` or the user) must invoke `prompt-contract-designer` first.

## Required Task Files

For implementation tasks, locate the relevant active task directory and read these files before making changes:

```text
task.md
state.json
constraints.md
assumptions.md
decisions.md
prompt_contract.md
execution_notes.md
```

Read `state.json` first. It is the machine-readable source of truth for task status, current phase, required files, steps, blockers, validation, and verification.

## Workflow

### 1. Read Task State

Read in this order at `taskPath`:

1. `state.json` — current phase, workflow block (if present), task status.
2. `prompt_contract.md` — Goal, Constraints, Success Criteria, Execution Rules, Stop Conditions.
3. `assumptions.md`, `decisions.md`, `constraints.md`.
4. `orchestration_plan.md` if present — Research Decisions and Verification Obligations are informational for execution scope. **Problem Classification is not.** Every attention item whose planned handling names a `test:` or `guard:` artifact is a deliverable of this execution: write the test, or confirm the guard is present at its citation and tag it with the R-id and its name (`R2 (dock-mac-collision)`). An item left for the verifier to bounce back is a repair cycle you chose to spend.
5. `research/internal-recon.md` if present — files in scope, patterns to mirror, and landmines, already mapped. Read it **before** exploring the codebase yourself; re-deriving what recon already established is the duplicated discovery the pass exists to prevent. If it is absent or does not cover the ground you need, explore normally.
6. Any other `research/<topic-slug>.md` files referenced by `orchestration_plan.md`.

Do not start a new task directory from inside this skill. If `taskPath` does not exist, stop and report.

### 2. Validate The Contract

Before executing, confirm:

- `prompt_contract.md` has non-empty Constraints and Success Criteria.
- `state.json` does not conflict with the markdown files.
- `constraints.md` and `decisions.md` do not conflict with the user request.
- Any assumption blocking execution is `VALIDATED` (or `REJECTED` with a documented alternative), and carries an actor and a citation. A status with neither is not resolved, whatever it says. An assumption that is still `OPEN` and would change the implementation is a stop condition — report it to the orchestrator.

If `state.json` conflicts with the markdown files, stop and report the conflict.

Update `state.json`:

- Append a `workflow.skillsRun` entry recording that `contract-driven-execution` has started, with `completedAt` left null until the run finishes.

### 3. Execute From The Contract

Treat `prompt_contract.md` as the execution contract.

- Do not override constraints or decisions.
- Do not work directly from vague instructions when a task contract exists.
- Keep changes scoped to the contract goal.
- Update `state.json` when a step status changes, a blocker appears or resolves, or an assumption is validated or rejected.
- When execution produces evidence that moves an assumption, record it with actor `executor` and the citation that moved it — the test name, log line, correlation ID, or `file:line`. This is the cheapest moment to capture it; the verifier can only mark NEVER-TESTED for evidence nobody wrote down.
- Work with existing user changes; do not revert unrelated edits.

This skill does not run a verifier or code-reviewer pass. Those are owned by `task-orchestrator` and run after this skill returns.

### 4. Update State After Execution

After execution, update:

- `execution_notes.md` with what changed, what was validated, commands run, and any residual risks.
- `state.json` with final step statuses, current phase, blockers, and `lastUpdated`. Update the `workflow.skillsRun` entry created in step 2 with `completedAt` and the list of outputs.
- `assumptions.md` when assumptions are validated or rejected.
- `decisions.md` when a decision becomes stable and affects future execution.

If an assumption is validated and becomes a durable rule, move or summarize it in `constraints.md` or `decisions.md`.

Hand control back to `task-orchestrator`, which owns the verifier and code-reviewer passes that follow.

## Stop Conditions

Stop and surface the issue to the caller (orchestrator or user) when:

- `taskPath` does not exist or required task files are missing.
- The contract lacks constraints or success criteria.
- Task state conflicts with the prompt contract.
- An OPEN assumption would materially change the implementation.
- A required decision is missing and guessing may cause wrong code, unsafe changes, or wasted implementation.
- Sandbox or approval restrictions block a required write or command.

In all these cases the orchestrator decides the next step (clarify, re-research, restructure the contract). This skill does not improvise.

## Output

In the final response back to the orchestrator, report:

- What was created or changed.
- Updated assumption statuses, if any.
- Any blocker or residual risk that remains.
- The path of `execution_notes.md` for the orchestrator to inspect.

Do not run a verifier-style pass here. Verifier and code-reviewer run in the orchestrator after this skill returns.

---
name: contract-driven-execution
version: 1.5.0
description: Direct-path executor for non-trivial work that has a finalized prompt contract. Performs the implementation, refactoring, research-driven coding, or agent-workflow execution against the current PromptContract artifact and records findings in the governed task. Typically invoked by `task-orchestrator` on the direct execution path.
---

# Contract Driven Execution

## Purpose

Execute work against an explicit contract. Keep execution tied to task state instead of vague instructions.

This skill is the **executor**, not the planner. It does not decide whether to decompose, whether to research, or whether the contract is the right shape. Those decisions belong to `task-orchestrator`. If you find yourself making them here, stop.

The execution contract is the current `PromptContract` artifact in the context manifest. The kernel
supplies the governed task directory as `taskPath`.

## Inputs

- `taskPath` — governed task directory supplied by the kernel.

This skill assumes a valid contract already exists at `taskPath`. If `prompt_contract.md` is missing or has empty Constraints or Success Criteria, stop and surface that to the caller. Do not attempt to design the contract from inside this skill.

## Core Rule

Execute strictly within the contract. Do not redesign scope. Do not work around constraints. Do not introduce changes outside the goal stated in `prompt_contract.md`.

If the contract is missing, the caller (typically `workflow-coordinator` or the user) must invoke `prompt-contract-designer` first.

## Required Task Context

For implementation tasks, read the context manifest before making changes. It contains the current
contract, orchestration plan, constraints, claims, decisions, work scope, and stop conditions. The
kernel may also materialize `task.md`, `assumptions.md`, and `decisions.md` in `taskPath`; those are
read-only projections and must never be edited.

`execution_notes.md` is the worker-owned execution report and may be created or updated in
`taskPath`.

## Workflow

### 1. Read Task State

Read in this order:

1. The context manifest — task status, work scope, PromptContract, constraints, claims, decisions, and stop conditions.
2. The current OrchestrationPlan artifact — Research Decisions and Verification Obligations are informational for execution scope. **Problem Classification is not.** Every attention item whose planned handling names a `test:` or `guard:` artifact is a deliverable of this execution: write the test, or confirm the guard is present at its citation and tag it with the R-id and its name (`R2 (dock-mac-collision)`). An item left for the verifier to bounce back is a repair cycle you chose to spend.
3. `research/internal-recon.md` if present — files in scope, patterns to mirror, and landmines, already mapped. Read it **before** exploring the codebase yourself; re-deriving what recon already established is the duplicated discovery the pass exists to prevent. If it is absent or does not cover the ground you need, explore normally.
4. Any other `research/<topic-slug>.md` files referenced by the orchestration plan.

Do not start a new task directory from inside this skill. If `taskPath` does not exist, stop and report.

### 2. Validate The Contract

Before executing, confirm:

- The PromptContract has non-empty Constraints and Success Criteria.
- The manifest's constraints and decisions do not conflict with the task goal.
- Any assumption blocking execution is `VALIDATED` (or `REJECTED` with a documented alternative), and carries an actor and a citation. A status with neither is not resolved, whatever it says. An assumption that is still `OPEN` and would change the implementation is a stop condition — report it to the orchestrator.

The active governed run already records that `contract-driven-execution` has started. Never start,
complete, or mutate the run from inside the agent session.

### 3. Execute From The Contract

Treat `prompt_contract.md` as the execution contract.

- Do not override constraints or decisions.
- Do not work directly from vague instructions when a task contract exists.
- Keep changes scoped to the contract goal.
- When execution produces evidence that moves an assumption, record it with actor `executor` and the citation that moved it — the test name, log line, correlation ID, or `file:line`. This is the cheapest moment to capture it; the verifier can only mark NEVER-TESTED for evidence nobody wrote down. In a governed task, record the claim with `ailedger claim add` and the citation with `ailedger evidence add`; `executor` names the cognition, while `--actor` uses the active actor id.
- Work with existing user changes; do not revert unrelated edits.

This skill does not run a verifier or code-reviewer pass. Those are owned by `task-orchestrator` and run after this skill returns.

### 4. Update State After Execution

After execution, update `execution_notes.md` with what changed, what was validated, commands run,
and any residual risks. Record new truth through the CLI: `claim add` and `evidence add` for findings;
surface stable decisions or constraints to an actor holding `ProposeDecision` or `ManageConstraints`
rather than editing their projections. Raise a governed escalation when a blocker meets the task's
escalation rules.

Hand control back to `task-orchestrator`, which owns the verifier and code-reviewer passes that follow.

## Stop Conditions

Stop and surface the issue to the caller (orchestrator or user) when:

- `taskPath` does not exist or required manifest artifacts are missing.
- The contract lacks constraints or success criteria.
- Governed task state conflicts with the PromptContract.
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

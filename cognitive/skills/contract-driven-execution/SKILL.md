---
name: contract-driven-execution
version: 1.6.6
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

`execution_notes.md` is the worker-owned execution report. In a confined session, use the
durable report path explicitly supplied by the host inside the authorized work scope; direct
ledger/taskPath access is forbidden there. Otherwise it may be created or updated in `taskPath`.
TMPDIR is disposable and deleted at run exit: never return it as the only location of execution
notes or required evidence. Record durable findings/evidence through the supplied tools and return
the durable report path, including for partial or blocked work.

## Workflow

### 1. Read Task State

Read in this order:

1. The context manifest — task status, work scope, PromptContract, constraints, claims, decisions, and stop conditions.
2. The current OrchestrationPlan artifact — Research Decisions and Verification Obligations are informational for execution scope. **Problem Classification is not.** Every attention item whose planned handling names a `test:` or `guard:` artifact is a deliverable of this execution: write the test, or confirm the guard is present at its citation and tag it with the R-id and its name (`R2 (dock-mac-collision)`). An item left for the verifier to bounce back is a repair cycle you chose to spend.
3. `research/internal-recon.md` if present — files in scope, patterns to mirror, and landmines, already mapped. Read it **before** exploring the codebase yourself; re-deriving what recon already established is the duplicated discovery the pass exists to prevent. If it is absent or does not cover the ground you need, explore normally.
4. Any other `research/<topic-slug>.md` files referenced by the orchestration plan.

Do not start a new task directory from inside this skill. If `taskPath` does not exist, stop and report.

Workers do not initiate lesson consultation. No consultation purpose admits a Worker: recon and
reconsideration belong to eligible task-wide leads in Research/Design, and research belongs to
Researchers in Research. Read the recalled lessons already served in your brief as unverified
context. When one informs a finding or rejected approach, record the link with `from_lesson` using
the supplied recording tools. A newly discovered gap belongs in your findings and handoff; do not
select a consultation purpose as an execution prerequisite or switch roles to obtain one.

Keep cited build/test logs and supporting files under the host-supplied `AILEDGER_RUN_OUTPUT`
directory, beside execution notes. TMPDIR is disposable. Record the durable path, source basis,
and exactly what the run establishes, with directional claim links. A retained summary does not
preserve a deleted log; a later rerun establishes new evidence, not the missing historical output.

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
- When execution produces evidence that moves an assumption, record it with actor `executor` and the citation that moved it — the test name, log line, correlation ID, or `file:line`. This is the cheapest moment to capture it; the verifier can only mark NEVER-TESTED for evidence nobody wrote down. In a governed task, batch related claims and directional evidence through supplied `record_findings` and retain the host ID mappings. When unavailable, use authorized `ailedger claim add` and `ailedger evidence add`. `executor` names the cognition; the trusted binding (or CLI `--actor`) must identify the actual active actor.
- Work with existing user changes; do not revert unrelated edits.

This skill does not run a verifier or code-reviewer pass. The coordinator dispatches those after the orchestrator reconciles this skill's results.

### Subject assurance and repair handoff

Workers/researchers execute singular owned work; they do not select assurance bundles or close
assignments. Consume the plan's static Assurance Subjects associations and the supplied relevant
repair findings. A finding may originally cover several members but currently apply to only some;
retain its producer status and original/applicable coverage. An explicit empty applicable set is
empty. Failed-producer findings are evidence to address, not completed assurance. Do not discard
intersecting bundle findings or import unrelated outputs.

Report changed paths (including untracked files, modes and deletions), fulfilled recon rows and
focused evidence. The coordinator freezes candidate bytes and chooses independent assurance over
the declared association. Stop editing at freeze; report any later edit even after failed/cancelled
work. Resume of your own worker cognition does not authorize resuming a new assurance session.
Do not file task-wide PromptContract/OrchestrationPlan from a work-scoped run. Record worker
findings through claims/evidence and your execution report; do not invent an artifact kind. The
later verifier/reviewer outputs inherit their new producer coverage when filed without work flags.
Missing shared contracts or conceptual seams return BLOCKED to the orchestrator before edits.

### 4. Update State After Execution

After execution, update `execution_notes.md` with what changed, what was validated, commands run,
and any residual risks. Record new truth through the kernel: `record_findings` when supplied, else `claim add` and `evidence add`;
surface stable decisions or constraints to an actor holding `ProposeDecision` or `ManageConstraints`
rather than editing their projections. Raise a governed escalation when a blocker meets the task's
escalation rules.

Hand control back to `task-orchestrator`, which reconciles readiness and returns it to the coordinator for verifier and paired reviewer dispatch.

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

Do not run a verifier-style pass here. The coordinator dispatches verifier and code-reviewer after orchestrator reconciliation.

## Declare the producer return

Before your active Worker/Researcher session returns, use supplied `declare_producer_outcome`.
First record output or blocker evidence through `record_findings`; retain its assigned evidence IDs.
The exact body is `{"outcome":"blocked","output_evidence_ids":[],"blocker_evidence_ids":["E-ID"]}`.
`outcome` is `reported-complete`, `blocked` or `partial`. Supply 1–16 unique existing own evidence
references in total. Blocked needs blocker evidence; reported-complete needs output evidence and
no blockers. Evidence can cite the supporting output/report and actual validation or limitations.

The host binds actor/run; do not supply another identity. This is one immutable declaration per run:
retry the identical arguments after uncertainty, including when the first response was lost.
Conflicting content is refused. Declare when ready to return, not at every progress checkpoint.
This records self-report only: it never closes your run/work item, resolves claims or accepts work.
Missing historical declarations stay unknown. If the tool is unavailable, report that limit in your
ordinary return; do not invent a declaration or impersonate the launcher. Existing report,
claim/evidence, escalation and independent-assurance obligations continue to apply. A Researcher
retains its Research-stage consultation obligation; a Worker has no consultation obligation.

---
name: workflow-coordinator
version: 1.8.3
description: Pure routing skill for non-trivial work. Sequences the planning, research, execution, verification, and code-review skills, then marks lesson-bearing records and requests archival through the kernel so that every non-trivial task flows through the same disciplined pipeline and produces durable artifacts in one governed task. Use this skill as the entry point whenever a request is non-trivial — implementation beyond a small one-file change, architecture or design decisions, multi-step refactoring, external API or vendor work, research, validation, or any work that should be resumable across conversations.
---

# Workflow Coordinator

## Purpose

Route a non-trivial user task through the right skills in the right order. Use the governed task and context manifest supplied by the kernel.

This skill is **pure routing**. It does not perform technical analysis, decomposition, research, or evidence adjudication. It owns routing reconsideration when the workflow stops converging; substantive reassessment belongs to the orchestrator.

## Core Rule

Use this skill at the start of any non-trivial task. Skip for trivial work (single-line edits, syntax fixes, simple explanations, one-off lookups).

The coordinator's job is bounded to five things:

1. Confirm the kernel and context manifest are available, and report what is missing.
2. Consume the supplied governed task path.
3. Invoke the planning and execution skills in the correct order.
4. Select ready declared subject associations, dispatch verifier then paired reviewer, track and retire assurance assignments, and reconsider routing when repair does not converge.
5. Mark the verifier's lesson-bearing records and request the Archive stage.

If you find yourself making planning judgments inside this skill, stop. Move the judgment to the skill that owns it.

## The Pipeline

```
workflow-coordinator
  0. Preflight  (kernel + current context manifest available?)
  1. Consume taskPath  (supplied by the kernel)
  2. Invoke prompt-contract-designer   (triages recalled lessons from the manifest)
  3. Invoke task-orchestrator
  4. Select ready declared associations; dispatch verifier then paired reviewer when code-bearing; retire resolved assignments
  5. Mark lesson-bearing records; request the Archive stage
  6. Report path of artifacts and any unresolved blockers
```

The orchestrator owns research routing, execution-path choice, work decomposition, subject relationships, implementation synthesis and evidence judgment. It returns reconciled readiness facts. The coordinator mechanically selects ready declared associations and dispatches assurance through the authorized operator; it does not infer relationships or perform those planning judgments.

Step 5 is transcription, not judgment. The verifier already decided every assumption's status and every decision's fate; the coordinator marks the records that would change a future task and lets the kernel mint and archive them.

## Workflow

### 0. Preflight — check the governed context

The task id, actor, run, role, `taskPath`, and context manifest must be present. If any is missing,
stop and ask the operator to launch or re-brief the run through `ailedger provider launch` or
`ailedger context build`. Do not create a parallel task directory or ledger.

### 1. Resolve taskPath

Use the `taskPath` supplied by the kernel and pass it unchanged to downstream skills. The coordinator
does not create, locate, move, or archive task directories.

If the request is trivial, do not invoke this pipeline. Answer directly.

### 2. Invoke prompt-contract-designer

Always run the designer for non-trivial work, even when a contract already exists — re-running is idempotent and may add claims or revise the contract.

The designer writes `prompt_contract.md`, files it as the current PromptContract artifact from its
active producer run, and adds any OPEN claims the contract depends on. It triages recalled lessons
already present in the manifest. The coordinator does not perform recall itself and does not evaluate
what recall returned.

After this step, the contract must be valid. If the designer reports it could not produce a valid contract, stop and surface the missing information to the user.

### 3. Invoke task-orchestrator

Pass `taskPath` to the orchestrator. The orchestrator reads the contract and:

- Classifies the task, grounds material failure modes against recon, and records their planned handling before choosing an execution path.
- Decides whether research is needed.
- Decides execution path (direct vs decompose).
- Writes `orchestration_plan.md` and files it as the current OrchestrationPlan artifact.
- Invokes `technical-researcher` if needed.
- Invokes `contract-driven-execution`, or specifies a phase's disjoint worker runs and launches them concurrently, depending on chosen path.
- Declares subject associations and returns reconciled readiness facts, scope union, working provenance, exclusions and assurance obligations for coordinator dispatch.
- Evaluates returned findings and plans any repairs; the coordinator dispatches fresh assurance after reconciliation.
- Returns the assumption, attention-item, and drift rows for step 5.

The coordinator does not duplicate these technical judgments. It may pause dispatch and return the approach for reassessment under the convergence check below.

### Convergence check before another dispatch

Before another repair or assurance dispatch, consider cumulative verifier/reviewer rejections and
repair findings: recurring defects on the same boundary, growing scope, or effort without meaningful
convergence may mean the approach itself needs reconsideration. Consider pausing repair dispatch
and route a whole-approach reassessment to the orchestrator, rather than patching only the newest
finding. Sunk cost is not a reason to continue.

The orchestrator evaluates whether to retain, simplify, replace or discard parts of the design,
preserving useful evidence and unchanged work. Require a concrete revised approach before resuming
a paused chain, using existing planning and authorization boundaries. Record the routing reason and
explain the concrete failing assumption and reconsidered route to the user. This is a judgment about
whether to continue dispatch, not a technical verdict. There is no fixed rejection count, mandatory
full restart, extra approval gate or automatic destructive rollback.

### 4. Select and dispatch assurance, then retire assignments

After execution, consume the orchestrator's declared associations and reconciled readiness facts. Select ready members mechanically: completed working cognition, no active intersection, blocked dependency or open escalation, and a verifier provider independent of every member. Record selected IDs, latest working run/provider per member, scope union and exclusions. Missing or conflicting readiness returns to the orchestrator; never invent a grouping. Capture and freeze the candidate using the procedure in `task-orchestrator`.

Before dispatching assurance, confirm the orchestrator has accounted for every mapped recon seam
and behavior case, with evidence citations or an explicit disposition, and revisited entries affected
by repairs, including related cases those repairs exposed. Return unaccounted entries to
the orchestrator. Statements that recon was read, a rejection was fixed, or tests passed do not
replace this accounting. The orchestrator judges the evidence; the coordinator checks its presence.

Through the authorized operator, dispatch a fresh verifier with explicit membership and candidate. After its completed applicable output, unchanged-byte comparison and orchestrator finding disposition, dispatch a fresh isolated reviewer for code-bearing work with identical membership/provenance/candidate and the exact paired verifier run ID. Use the CLI forms in `task-orchestrator`; never supply task narrative or findings as reviewer framing. Complete selected source artifacts, including comments, remain review evidence under the code-reviewer source-as-data boundary. Child runs do not acquire dispatch authority from this skill.

After dispatch, use `ailedger status` and `ailedger artifact list` to confirm a completed verifier
run and applicable current VerifierOutput exist for every selected member, followed by a completed
paired code-reviewer run and applicable CodeReviewOutput when work is code-bearing. One covering
pair can satisfy several members; do not require a separate pair per item. Confirm the recorded
selection from declared associations, exact candidate/member/provenance equality, per-member verifier provider
independence and recorded unchanged-byte comparisons. Run completion proves cognition/filed output,
not a favorable verdict: require the orchestrator's finding dispositions before closure.

Derive assignment state from runs, applicable outputs and work status. Retire resolved assignments
from consideration after member closure, preserving history; do not rewrite runtime plan rows.
Route changed scope/relationships and affected-subject reopening back to the orchestrator. Completed
items remain terminal and repairs use newly declared work. The orchestrator owns relationship changes, readiness reconciliation and finding judgments; the coordinator owns mechanical selection, candidate capture, dispatch and assignment tracking.

If either required pass is missing or does not cover the selected members, stop and report. The coordinator does not silently skip these.

Then check the final VerifierOutput contains a row for every claim the plan treated as an assumption,
and that no row is still OPEN. Also require the Attention Item Disposition table to cover every R-id
with no `unresolved` row, and every `handled` row to name the artifact it resolved and what resolving
it returned. This is a presence check, not a judgment — the coordinator does not evaluate whether
evidence is good, only that the required rows exist and are terminal. If rows are missing or
non-terminal, stop and report; do not proceed to step 5.

### 5. Record lessons and archive

Mechanical transcription through `ailedger lesson mark`. No judgment — the verifier already made
every call.

1. Read the assumption disposition, attention-item disposition, and decision-drift rows from the
   current VerifierOutput artifact.
2. For every assumption with status **REJECTED** or **NEVER-TESTED**, every attention item disposed
   **not-applicable**, and every decision that **drifted or was abandoned**, identify the eligible
   claim, alternative, or resolved escalation that carries the finding in the event log.
3. Translate the verifier vocabulary to the lesson class:

   | verifier disposition | ledger `class` |
   |---|---|
   | REJECTED | `REFUTED` |
   | NEVER-TESTED | `UNTESTED` |
   | attention item `not-applicable` | `REFUTED` |
   | a decision that drifted or was abandoned | `DRIFTED` |

4. Mark each eligible record with `ailedger lesson mark`, copying the verifier's tags, establishing
   cognition, `verify` command and direction, and `do-not` rule. When no command can re-establish it,
   use `none — <reason>` rather than inventing one. Use `--supersedes LESSON-ID` when this record
   replaces a recalled lesson. Skip VALIDATED rows; confirmations are noise in an index.

```bash
ailedger lesson mark --task TASK --actor ACTOR --source SOURCE --repo REPO \
  --kind SOURCE-KIND --class CLASS --verify COMMAND --verify-expects present \
  --do-not TEXT --lesson-actor researcher --tag TAG
```

`--actor` is the lead or operator issuing the mutation. `--lesson-actor` is the cognition that
established it. Omit `--verify-expects` only when `--verify` is `none — <reason>`.

5. Ask the operator to request `ailedger stage transition --stage archive`. The kernel validates the
   stage arm, mints the marked lessons, publishes them to the lesson store, and archives the task.

Do not run this step when the verifier did not run or its disposition is incomplete. An unverified
task has nothing to teach, and a lesson minted from one is worse than no lesson because recall will
hand it to a future task as evidence.

For candidate-bearing releases, confirm recorded frozen-candidate evidence and project-appropriate
final checks before operator work completion and Learn/Archive. Retrospective filing
occurs after Archive through the operator command without a producer run. Clean committed bytes
must match the assured product and pass release checks before release. For changes to AILedger itself, additionally require private CLI bootstrap, full
final suite and copied real-history replay, preserving the known-good global kernel until all gates
close; then confirm the installed source identity. Ordinary projects use their existing compatible
kernel and do not build or install a private kernel. Route missing evidence back to its owner. Never install an intermediate
candidate to enable its assurance, and never mutate lifecycle from a launched child run.

### 6. Report

In the final response, include:

- The task directory path.
- Whether the work completed, has unresolved blockers, or has accepted technical risks.
- A short pointer to where the human can read the artifacts (`prompt_contract.md`, `orchestration_plan.md`, `execution_notes.md`, `review/`).

## Governed Task Outputs

The kernel supplies `taskPath`; downstream skills keep their human-readable outputs there:

```text
<taskPath>/
  prompt_contract.md
  execution_notes.md        (appended during execution)
  orchestration_plan.md     (written by task-orchestrator)
  research/
    <topic-slug>.md         (written by technical-researcher)
  review/
    verifier-N.md           (written by the verifier run)
    code-reviewer-N.md      (written by the code-reviewer run)
```

- `N` is a numeric suffix that increments when review passes are re-run after repairs (`verifier-1.md`, `verifier-2.md`, etc.). Existing files are not overwritten.

## Stop Conditions

Stop and surface the issue when:

- The request is ambiguous in a way that would cause wrong implementation.
- The contract-designer reports a contract cannot be made valid.
- The orchestrator cannot return reconciled readiness facts for the declared associations.
- A dispatched verifier or required paired reviewer lacks completed, applicable coverage.
- The verifier output has no assumption disposition table, assumptions remain OPEN, or a planned attention item is missing or unresolved. Do not archive and do not record lessons — report it.
- The manifest conflicts with a current governing artifact.
- Sandbox or approval restrictions block a required write.

Stopping is not failure. Routing past a broken step is.

## What This Skill Does NOT Do

To prevent scope creep, this skill explicitly does not:

- Analyze the task or assess complexity (orchestrator's job).
- Decide whether research is needed (orchestrator's job).
- Decompose into workers or assign worker scope (orchestrator's job).
- Invoke research or implementation outside the orchestrator's work plan.
- Synthesize worker outputs (orchestrator's job).
- Perform verifier/reviewer cognition or judge their evidence. The coordinator dispatches these runs; the orchestrator judges findings and synthesizes repairs.
- Write Claude, Codex, or any other machine configuration.
- Perform prior-art recall or evaluate what it returned (the designer owns initial recall; the orchestrator owns the classified delta).
- Judge whether an assumption held, an attention item was handled, or a decision drifted (verifier's job; the coordinator transcribes its rows verbatim).
- Write any task files (every file has a specific owner skill). The kernel owns event projections,
  lesson minting, and archival. The coordinator records routing, selection, candidate comparisons and assignment evidence through the CLI; it does not author planning or review content.

If a future change tempts you to add any of these to the coordinator, push them down into the right skill instead.

## Output

In the final response, report:

- The task directory path.
- The execution path the orchestrator chose, and a one-line rationale.
- Whether research was performed, and what topics.
- Verifier and code-reviewer outcomes (pass / repairs made / accepted risks).
- Any assumption that ended NEVER-TESTED or REJECTED, and any attention item left unresolved. Name these even when the work succeeded — they are the part the operator cannot recover later.
- What was marked lesson-bearing, and whether the kernel archived the task.
- Any unresolved blocker.

Keep the report short. The artifacts in the task directory are the durable record; the response is a pointer.

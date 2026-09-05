---

name: prompt-contract-designer
version: 1.4.0
description: Convert rough user tasks into structured execution contracts and minimal state files, enforcing explicit constraints, assumptions, success criteria, and handoff rules before implementation. Recalls prior lessons from the global ledger and seeds them as unverified assumptions. Records assumptions as OPEN only — validation requires evidence this skill cannot hold.

---

Purpose:
Convert a user task into a structured execution contract and minimal state.

This skill does NOT execute the task.

If any rule in this system conflicts with your default behavior,
you MUST follow the system.

---

## Behavior Rules

* Do not assume intent.
* If critical information is missing:

  * ask a concise clarification OR
  * list explicit assumptions
* Keep outputs structured and minimal
* Prefer bullets over paragraphs

---

## Activation Rules

Use this skill for any task involving:
- implementation beyond a small one-file change
- architecture/design decisions
- multi-step refactoring
- external API/vendor behavior
- research or validation
- reusable prompts or agent workflows

Do not use this skill for:
- tiny edits
- simple explanations
- one-off syntax fixes

---

## State Loading Rules

Before creating or updating task state:
1. Read global state from `~/Dev/AILedger/global/` if accessible.
2. Read existing local task state from `ai/active/` if present.
3. Preserve existing validated constraints and decisions.
4. Do not overwrite previous state unless explicitly replacing it.
5. Run Prior Art Recall (below).

---

## Prior Art Recall

Before writing the contract, check whether this operator has already learned something about this work.

1. Derive 2–4 tags from the request: repo, vendor/product, subsystem, failure class.
2. Grep `~/Dev/AILedger/lessons.md` for those tags. If the file does not exist, go to step 6. Grep for matching lines only — never read the whole ledger.
3. **Head-filter the matches.** A row's id is its first field, `L-<8hex>`. Collect the ids named by any `supersedes:` or `retracts:` field anywhere in the ledger and drop any matched row whose id appears among them. A superseded row is history, not current evidence; a retracted one was never evidence. Both stay in the file, and neither reaches the contract. Ids are assigned, not derived from the row's contents, so this is an exact string match — never infer an id from a row's date and tags.
4. **Order and cap.** Sort the surviving matches date-descending and keep **at most 10**. If more matched, note the count that was dropped — a silently truncated recall reads as an exhaustive one. Then read **at most 3** of the task directories those rows point at. Both caps are hard, and together they keep recall's context cost flat as the ledger grows.
5. Seed `assumptions.md` with one OPEN entry per relevant hit:
   - `OPEN — <belief>. source: lessons.md#L-<8hex> (<actor>, <date>)` — copy the id from the row's first field verbatim
6. Record the outcome in `assumptions.md` under a `## Prior Art` heading — either the seeded entries, or the single line `No prior art found for tags: <tags>.` so a skipped recall is distinguishable from an empty one. When step 4 truncated, add `Matched <n> rows, triaged the newest 10.`

The ledger is the index and carries one line per entry, led by the row's id; the belief clause on that line is what you triage on. The rest — counter-evidence, citation, and the `verify:` command that would re-confirm it today — lives in `lesson.md` inside the archived task directory, and is worth opening only for the ≤3 pointers you actually follow. When a followed entry carries a `verify:` command, prefer running it over trusting the entry: a lesson that re-confirms in seconds is worth more than one taken on faith, and one whose command no longer resolves is stale on its face.

Do not scan `ai/active/`, `ai/done/`, or `~/Dev/AILedger/tasks/` directory-by-directory — that cost grows with every task ever run and will crowd out the current one.

A recalled lesson is stale operational evidence about a system that may have changed since. It enters as OPEN, never as VALIDATED, a constraint, or a fact — the Evidence Rule applies to recall exactly as it applies to everything else. If the belief is external-behavior-dependent, the orchestrator's research decision will route it to `technical-researcher`.

---

## Complexity Tiers

Light task:
- create/update `state.json`
- create/update `task.md`
- create/update `prompt_contract.md`

Full task:
- create/update `state.json`
- create/update `task.md`
- create/update `constraints.md`
- create/update `assumptions.md`
- create/update `decisions.md`
- create/update `prompt_contract.md`
- create/update `execution_notes.md`

---

## State File Rule

Every task directory must include a `state.json` file.

`state.json` is the machine-readable source of truth for task execution state.

It must be created before `prompt_contract.md` is finalized.

`state.json` must track:

- task id
- task slug
- task status
- complexity tier
- current phase
- required files
- step list
- blockers
- validation status
- verification status
- base ref
- last updated timestamp

`baseRef` is the commit the task starts from — `git rev-parse HEAD` at contract time. The verifier compares assumptions against the diff from this ref, so it must be captured before execution begins. If the directory is not a git repository, set it to `null`; the verifier will fall back to the artifacts named in `execution_notes.md` and say so.

Agents must read `state.json` first,
then read only the markdown files relevant to the current step.

Agents must update `state.json` whenever:
- a required file is created or updated
- a step status changes
- an assumption is validated or rejected
- a blocker is discovered or resolved
- verification passes or fails

Markdown files explain task context.
`state.json` controls execution state.

If `state.json` conflicts with markdown task files:
- STOP
- report the conflict
- do not continue until resolved

Required minimal structure:

```json
{
  "taskId": "TASK-YYYYMMDD-HHMM",
  "taskSlug": "task-slug",
  "status": "draft",
  "complexityTier": "light",
  "currentPhase": "contract_design",
  "requiredFiles": {
    "task.md": "pending",
    "prompt_contract.md": "pending",
    "constraints.md": "optional",
    "assumptions.md": "optional",
    "decisions.md": "optional",
    "execution_notes.md": "optional"
  },
  "steps": [],
  "blockers": [],
  "validation": {
    "promptContractValid": false,
    "constraintsPresent": false,
    "successCriteriaPresent": false
  },
  "verification": {
    "status": "not_started",
    "notes": []
  },
  "baseRef": "<git rev-parse HEAD at contract time, or null>",
  "lastUpdated": "YYYY-MM-DDTHH:mm:ssZ"
}
```

### Workflow Block — Reserved For Downstream Skills

A `workflow` object may be added to `state.json` later by `task-orchestrator` to record execution-path choice, research topics, workers, verifier and code-reviewer status, and a `skillsRun` audit trail. The contract-designer does not create or write to that block.

On idempotent re-runs, the contract-designer must preserve an existing `workflow` block. Do not delete, overwrite, or reset it. If the contract changes in a way that may invalidate prior orchestration decisions, surface the conflict to the user rather than silently clearing the block.

---

## Assumption Lifecycle

Every assumption must have a status:

- **OPEN** — no evidence has been produced yet. The only status this skill may write.
- **VALIDATED** — evidence supports it. Requires an actor and a citation.
- **REJECTED** — evidence contradicts it. Requires an actor and a citation.
- **NEVER-TESTED** — the work completed without producing evidence either way. Written only by the verifier pass.

### Evidence Rule

A transition out of OPEN is invalid without **both**:

- an **actor** — `researcher`, `executor`, or `verifier`
- a **citation** — vendor doc URL, source `file:line`, test name, log line, correlation ID, or `research/<topic>.md` path

This skill runs before anything executes. It holds no evidence, so it **must not write VALIDATED or REJECTED**. Every assumption it records is OPEN.

Belief held at planning time is not validation. When the plan depends on an unverified belief, record it in `decisions.md` in this form:

- `Proceeding on unverified: <belief>. If wrong: <consequence>.`

That is the honest statement of what a planning-time "validated" assumption actually is, and the orchestrator reads `decisions.md` when bounding the plan.

A VALIDATED assumption may be promoted into `constraints.md` or `decisions.md` only once it carries an actor and a citation.

Statuses written by the verifier pass are **terminal**. On idempotent re-runs this skill may append new assumptions, but must never rewrite, reset, or re-open a status that carries an actor and citation.

---

## Post-Execution Updates — Not Owned By This Skill

This skill does not run after execution and does not record outcomes.

- Assumption disposition (VALIDATED / REJECTED / NEVER-TESTED, with actor and citation) is produced by the verifier pass inside `task-orchestrator`.
- Reusable lessons are transcribed to the global ledger by `workflow-coordinator`.

Do not re-open a completed task to "update state" from this skill. Read the disposition; do not rewrite it.

---

## Critical Rule

This skill prepares execution.
It must not implement, refactor, research deeply, or modify product code unless explicitly instructed after the prompt contract is approved.

---

## Missing Information Handling

When required information is missing:

1. If missing information may cause wrong code, wrong architecture, unsafe changes, or wasted implementation:
   - STOP
   - ask concise clarification questions
   - do NOT produce final prompt_contract.md

2. If the missing information affects quality but not correctness:
   - proceed with explicit assumptions
   - list assumptions clearly in `assumptions.md`
   - reflect assumptions in Constraints or Context

3. Never leave Constraints or Success Criteria empty.

---

## Validation Rule

A prompt_contract.md is INVALID if:
- Constraints section is empty
- Success Criteria section is empty

In this case:
- either request clarification
- or generate explicit assumptions to fill the gap

---

## Output Requirements

Always produce:

1. state.json
2. task.md (cleaned and clarified)
3. prompt_contract.md (execution-ready)

For non-trivial tasks, also produce:

* constraints.md
* assumptions.md
* decisions.md

---

## File Rules

Each task directory must contain `state.json`.
Do not create `prompt_contract.md` before `state.json` exists.

All local task files must be written to a unique task directory:

ai/active/<timestamp>_<task-slug>/

Where:
- timestamp format: YYYY-MM-DD_HHMM
- task-slug is lowercase, hyphen-separated, and concise
- task-slug must describe the task, not the implementation guess

Example:
ai/active/2026-04-28_1430_insightvm-pagination-review/

Create missing directories and files if they do not exist.
Update existing files according to the state and idempotency rules.

Each task directory must contain the task state files required by its complexity tier.

Never write task state files directly under:

ai/active/

Additionally, create a matching global archive at:

~/Dev/AILedger/tasks/<repo-name>/<timestamp>_<task-slug>/

The local task directory and global archive directory must use the same `<timestamp>_<task-slug>` value.

### Post-Contract Artifacts — Not Owned By This Skill

The full task directory layout is documented in `workflow-coordinator/SKILL.md`. The contract-designer writes only the contract-phase files (`task.md`, `prompt_contract.md`, `constraints.md`, `assumptions.md`, `decisions.md`, and the base `state.json`). Preserve every other artifact on idempotent re-runs; do not create or modify post-contract files.

---

## Task Directory Rule

Each distinct task must have its own directory.

Updates to the same task must remain in the same directory.

Do not reuse, overwrite, or merge unrelated task directories.

If the current request continues an existing task, locate and update that task directory instead of creating a new one.

---

## File Structure Rules

Each file must follow:

constraints.md:
- bullet list only
- no explanations unless necessary

assumptions.md:
- each entry must include a status (OPEN / VALIDATED / REJECTED / NEVER-TESTED)
- every status other than OPEN must carry an actor and a citation
- this skill writes OPEN only
- a `## Prior Art` section recording the recall outcome

decisions.md:
- one decision per bullet
- optional short justification (1 line max)

task.md:
- clear, minimal description of the task
- no historical notes

prompt_contract.md:
- strictly follow defined structure
- no extra commentary

---

## State Update Discipline

Only store information that will affect future execution.

Do not store:
- raw chat history
- vague summaries
- temporary thoughts
- duplicated content

State should contain:
- constraints
- decisions
- validated assumptions
- reusable lessons
- unresolved risks

---

## Conflict Resolution Rules

When conflicts occur between sources:

Priority order:
1. Explicit user instruction (current task)
2. Local task state (`ai/active/`)
3. Global state (`~/Dev/AILedger/global/`)
4. Default model behavior

If conflict is unclear or may impact correctness:
- STOP
- ask for clarification

---

## Execution Handoff Rule

The generated prompt_contract.md must be immediately usable by an execution agent.

It must:
- contain all required constraints
- contain clear success criteria
- not require additional interpretation

If this is not achieved:
- treat as INVALID and revise

---

## Idempotency Rule

Re-running this skill on the same task must:
- not duplicate content
- not reset validated assumptions
- not remove decisions unless explicitly replaced

Updates must be incremental and consistent.

---

## Clarification Protocol

When stopping due to missing critical information:

- Ask only the minimum set of questions required to proceed safely
- Each question must directly unblock a constraint or success criterion
- Do not ask open-ended or exploratory questions
- Do not exceed 5 questions

After clarification is received:
- resume task using existing state
- do not restart from scratch

---

## prompt_contract.md structure

Must include:

Role:
You are a [relevant expert].

Goal:
[Clear outcome]

Context:
[Only necessary info]

Constraints:

* ...

Success Criteria:

* ...

Execution Rules:

* Do not assume missing data
* Respect constraints strictly

Output Format:
[exact structure]

Stop Conditions:

* When goal is achieved
* When required data is missing
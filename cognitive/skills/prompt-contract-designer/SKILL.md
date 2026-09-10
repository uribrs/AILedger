---

name: prompt-contract-designer
version: 1.4.0
description: Convert rough user tasks into structured execution contracts, enforcing explicit constraints, assumptions, success criteria, and handoff rules before implementation. Consumes recalled lessons from the governed context and records them as unverified claims. Records claims as OPEN only — validation requires evidence this skill cannot hold.

---

Purpose:
Convert a user task into a structured execution contract and governed claims.

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

## Context Loading Rules

Before creating or updating a contract:
1. Read the context manifest supplied by `ailedger context build`.
2. Preserve existing validated claims, active constraints, and accepted decisions.
3. Do not replace a current artifact or event-backed record unless explicitly revising it through the CLI.
4. Run Prior Art Triage (below).

---

## Prior Art Triage

Before writing the contract, check the recalled lessons in the context manifest. Task-open tags and
the kernel's recall rules have already selected, head-filtered, ordered, and bounded this set.

For each relevant lesson, add an OPEN claim with `ailedger claim add --from-lesson LESSON-ID`.
When a recalled lesson carries a runnable verification command, ask the operator to use
`ailedger lesson recheck`; only the operator can approve and run stored commands.

A recalled lesson is stale operational evidence about a system that may have changed since. It enters as OPEN, never as VALIDATED, a constraint, or a fact — the Evidence Rule applies to recall exactly as it applies to everything else. If the belief is external-behavior-dependent, the orchestrator's research decision will route it to `technical-researcher`.

---

## Complexity Tiers

Light task:
- create or revise the PromptContract artifact

Full task:
- create or revise the PromptContract artifact
- add any OPEN claims the contract depends on
- propose any stable planning decisions through `ailedger decision propose`

---

## Governed State Rule

The event log is the source of truth. `task.md`, `assumptions.md`, `decisions.md`, and `state.json`
are kernel projections: read them when useful and never edit them. Record claims, evidence,
decisions, constraints, artifacts, and escalations through their `ailedger` commands.

The task and active producer run must already exist. The contract-designer does not create task
directories, start or complete runs, transition stages, or complete work items.

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

Belief held at planning time is not validation. When the plan depends on an unverified belief, use
that belief as an OPEN claim's statement and record its consequence with `claim add --consequence`:

- `Proceeding on unverified: <belief>. If wrong: <consequence>.`

That is the honest statement of what a planning-time "validated" assumption actually is, and the orchestrator reads the claim from its manifest when bounding the plan.

A VALIDATED assumption may be promoted into a constraint or decision only once it carries evidence with a citation. Use `ailedger constraint add` or `ailedger decision propose` through an actor holding the required capability.

Statuses written by the verifier pass are **terminal**. On idempotent re-runs this skill may append new assumptions, but must never rewrite, reset, or re-open a status that carries an actor and citation.

---

## Post-Execution Updates — Not Owned By This Skill

This skill does not run after execution and does not record outcomes.

- Assumption disposition (VALIDATED / REJECTED / NEVER-TESTED, with actor and citation) is produced by the verifier pass inside `task-orchestrator`.
- Reusable lessons are marked through `ailedger lesson mark` by `workflow-coordinator`.

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
   - do NOT file a final PromptContract artifact

2. If the missing information affects quality but not correctness:
   - proceed with explicit assumptions
   - add the assumptions as OPEN claims
   - reflect assumptions in Constraints or Context

3. Never leave Constraints or Success Criteria empty.

---

## Validation Rule

A PromptContract is INVALID if:
- Constraints section is empty
- Success Criteria section is empty

In this case:
- either request clarification
- or generate explicit assumptions to fill the gap

---

## Output Requirements

Always write `prompt_contract.md` under `taskPath`, then file it while the producer run is active:

```bash
ailedger artifact record --task TASK --actor ACTOR --run RUN --id ARTIFACT-ID \
  --kind PromptContract --title TITLE --body-stdin < <taskPath>/prompt_contract.md
```

A revision uses a new artifact id and `--supersedes ARTIFACT-ID`. Record OPEN assumptions with
`claim add`; propose stable planning decisions with `decision propose`.

---

## File Rules

Write `prompt_contract.md` only under the supplied `taskPath`. Do not create, relocate, archive, or
rename the governed task directory; the kernel owns its lifecycle.

### Post-Contract Artifacts — Not Owned By This Skill

The full task directory layout is supplied by the kernel briefing. The contract-designer writes only
`prompt_contract.md`; preserve every other owned artifact on idempotent re-runs.

---

## Task Directory Rule

Use only the `taskPath` supplied in the briefing. If it is missing, stop; do not infer or create one.

---

## File Structure Rules

`prompt_contract.md` must:
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

The governed record should contain:
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
2. The current context manifest and artifacts
3. Recalled lessons in the manifest
4. Default model behavior

If conflict is unclear or may impact correctness:
- STOP
- ask for clarification

---

## Execution Handoff Rule

The generated PromptContract must be immediately usable by an execution agent.

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
- resume task using the rebuilt context manifest
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

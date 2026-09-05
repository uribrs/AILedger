---
name: task-orchestrator
version: 1.6.0
description: Post-contract planning brain for non-trivial work. Reads a finalized prompt contract, resolves external research, runs an internal recon pass over the codebase, then decides whether to execute directly or decompose into bounded worker subagents over disjoint file sets, writes a persistent orchestration plan, runs execution, and always finishes with a verifier subagent followed by an isolated code-reviewer subagent. Use after `prompt-contract-designer` has produced a contract, typically invoked by `workflow-coordinator`. Best suited for coding, debugging, architecture/design, migrations, research-driven work, implementation planning, and QA/review-oriented tasks where decomposition quality, verification discipline, and independent implementation review matter.
---

# Task Orchestrator

This skill is the planning brain for non-trivial work after a contract exists. It expects a finalized `prompt_contract.md` and a populated task directory under `ai/active/<timestamp>_<task-slug>/`. If that input is missing, stop and ask the coordinator to run `prompt-contract-designer` first.

Keep planning, decomposition, dependency reasoning, and synthesis centralized in the main thread. Finish with an independent verifier subagent, then a separate code-reviewer subagent when the result is code-bearing.

## Inputs

The orchestrator is invoked with one required input:

- `taskPath` — absolute or repo-relative path to `ai/active/<timestamp>_<task-slug>/`.

Before doing anything else, read in this order:

1. `state.json` — current phase, complexity tier, existing workflow block (if any).
2. `prompt_contract.md` — Goal, Constraints, Success Criteria, Execution Rules, Stop Conditions.
3. `assumptions.md` — every assumption and its status (OPEN / VALIDATED / REJECTED / NEVER-TESTED), including the `## Prior Art` section recall produced.
4. `decisions.md` and `constraints.md` — if present.

If `prompt_contract.md` is missing Constraints or Success Criteria, stop. The orchestrator does not execute against an invalid contract.

## Operating Rules

Follow this sequence:

1. Read `state.json`, `prompt_contract.md`, and `assumptions.md` from `taskPath`.
2. Read the contract as the sole problem statement. Classify the task provisionally; if the contract is too thin to classify, stop and name the missing information instead of rebuilding it.
3. Resolve any OPEN external-behavior assumption that blocks useful recon.
4. Run the **internal recon** pass for code-bearing work. It precedes the path decision, because its output determines both the solution shape and whether workers can be briefed precisely. See Internal Recon.
5. Correct the classification against recon, identify task-specific attention items, and perform bounded classified prior-art recall.
6. Decide whether **external** research is needed from OPEN assumptions or from a classified question that could change a live solution decision. Invoke `technical-researcher` for each required topic.
7. Write the required Problem Classification section of `orchestration_plan.md`. Do not choose an execution path until the section is concrete and recon-corrected.
8. Decide execution path: `decompose` (bounded workers) unless the work cannot be split into disjoint file sets. See Execution Path Decision. Complete the rest of `orchestration_plan.md` and update `state.json`.
9. If `direct`: invoke `contract-driven-execution` with `taskPath`.
10. If `decompose`: freeze the shared surface first, then fan workers out over disjoint file sets; synthesize in the main thread.
11. Run the verifier subagent against `prompt_contract.md` Success Criteria and the produced artifacts. Write `review/verifier-N.md`.
12. Repair verifier issues or record unresolved request-coverage gaps explicitly.
13. For code-bearing work, run the code-reviewer subagent using the `code-reviewer` skill with **minimal context only** (see the Code-Reviewer Subagent section). Write `review/code-reviewer-N.md`.
14. Repair material code-review findings or record accepted technical risks explicitly.
15. Update `state.json` (`verifierRun`, `codeReviewerRun`, `skillsRun`) and append final notes to `execution_notes.md`.
16. Return the final assumption disposition, attention-item disposition, and decision-drift rows to the coordinator, which transcribes the lesson-bearing ones to the global ledger.

Roles:

- Main thread: read state, plan, decide research, decompose, sequence, synthesize, decide repairs.
- Recon subagent: map the internal ground the plan and the worker briefs are written from.
- Workers: execute assigned pieces.
- Verifier subagent: full context; inspect the completed result against the contract Success Criteria.
- Code-reviewer subagent: minimal context; inspect implementation quality in isolation. See `code-reviewer/SKILL.md`.

The verifier is not a cheerleader, not a restatement engine, not a rubber stamp.
The code-reviewer is not a second verifier.

## Problem Classification Gate

The contract already owns the outcome, constraints, non-goals, and success criteria. Do not restate them or create a second problem model. Read them, then classify the engineering shape well enough to ask: **what routinely bites work of this kind, and does this codebase already handle it?**

Before choosing an execution path, `orchestration_plan.md` must contain the Problem Classification section shown below. Keep it bounded:

- **Contract sufficiency:** `sufficient`, or `blocked — <missing information>`. A blocked classification stops planning and returns to the coordinator.
- **Classes:** zero to three normalized, ledger-compatible tags. Inspect existing ledger tags first; reuse an exact or clear semantic match, otherwise mint a lowercase slug. The vocabulary is open. `No class applies` is valid with a concrete reason.
- **Classified prior art:** perform a delta lookup using the chosen classes under the designer's existing recall caps. Do not duplicate its `## Prior Art` block. Append only newly relevant beliefs to `assumptions.md` under `## Classified Prior Art` as OPEN assumptions, then cite their A-ids in the plan. Known failure modes are design inputs, not assumptions.
- **Recon correction:** confirm, reject, or refine the provisional classes after internal recon. Classification is revisable whenever later evidence changes the task's shape.
- **New or changed artifacts:** list every artifact this plan introduces, or whose behavior it changes — exception type, DTO, config key, branch, retry or resilience policy, persisted state, wrapper, or a change in what an existing interface does. For each, trace it one hop into the code that already exists:

  `<artifact> → <first existing consumer that observes it> → <what that consumer does with it>`

  Find the consumer by reading, not by assuming: the handler, classifier, policy chain, serializer, or dispatcher that sees the new thing first. When the trace shows the artifact being claimed, coerced, swallowed, or routed somewhere the design did not intend, that is a **design-invalidating interaction** and it requires an attention item. One line per artifact; `None introduced — <reason>` when the plan adds nothing new.

  Worked example: `new wrapped transport exception → recursive inner-exception classifier → claimed by the generic retry policy before the intended Falcon policy runs`.
- **Attention items:** zero to five credible, material, foreseeable failure modes across the task, not per class. Each item states its causal path and impact, and a **planned handling that names an artifact, not an intention**:
  - `test:<path>::<name>` — a test that fails if this failure mode occurs. The default.
  - `guard:<file>:<line>` — an existing protection that already covers it, cited where it lives.
  - `research:<topic-slug>` — open until the matching research question answers it.
  - `accept: <proportional reason>` — deliberately no artifact.

  Tag the test or guard with the R-id, in its name or an adjacent comment, so the plan-to-code mapping is a grep and not a reading assignment. A `test:` artifact must construct the thing it guards through the production path that will actually produce it — the real wrapper, the real classifier, the real binding — not a hand-built approximation of it. A tripwire assembled from a convenient stand-in tests the stand-in, and keeps passing while the real path diverges.

  Use `No material attention items — <concrete task-specific reason>` when none apply — unless a trace above found a design-invalidating interaction, in which case at least one attention item is required and this escape is unavailable.
- **Research questions:** zero to three total. Each must name the decision its answer can change and the authoritative source family for the stack in play. If no answer could change the solution, do not research. `No research needed` requires a concrete reason.

This gate constrains the output, not the thinking. It does not claim exhaustive edge-case discovery. Recon, research, implementation, or review may overturn any class, attention item, or planned handling; update the plan when they do.

`accept` and `research` carry no penalty. A test written to satisfy the format is worse than no test, so when a failure mode is not mechanically checkable — a vendor rate ceiling, an IAM grant, a deploy-time question — name that and accept it. The rule governs where `handled` is allowed to land, not that everything must be tested.

Recon maps the topology that already exists. The artifact trace projects the new thing into it. Examining existing structure carefully and never asking what the code being added will look like to the code already there is a distinct failure, and it is not caught by looking harder at either side alone: a new exception type is not new to the classifier that catches its base class.

The point of naming an artifact is that nobody has to read the plan to learn whether the failure mode was covered. An intention (`address`, `preserve existing protection`) can only be confirmed by a human reading prose; a test either exists and passes or it does not, and it still fails six months from now when the archived plan is closed.

After this gate, infer whether the task is simple enough to keep local, whether it can be split into low-coupling worker tasks, and the dependency shape between those tasks.

Score each axis (Complexity, Separability, Coupling, Dependency order, Execution risk, Worker clarity) per the Simplicity Decision Heuristic in [orchestration-rubric.md](./references/orchestration-rubric.md). Use the rubric's Decision Guide to choose `direct` vs `decompose`.

**Record the six scores in `orchestration_plan.md`.** Scoring in your head and writing only the outcome is not a decision — it is a default wearing a decision's clothes. Coupling is not estimated: it is read off recon's disjoint-set finding, so it carries evidence the other five axes do not. The axes inform the shape of the decomposition; they do not override the hard triggers in Execution Path Decision.

## Research Decision

Before choosing execution path, decide whether research is needed.

For each OPEN assumption and each classified research question, ask:

- Can the task be executed correctly without resolving this assumption?
- Does it depend on external-system behavior or an established practice whose applicability local recon cannot settle?
- Can the answer change a named solution decision?

Research is required when either an OPEN assumption blocks correct execution or a bounded classified question can materially change the solution. Prefer the authoritative source for the stack in play; use secondary sources only to fill a gap or establish operational experience.

When research is required:

- First record one pending entry for the topic in `state.json.workflow.researchTopics`; do not invoke against a missing or duplicate topic.
- Invoke `technical-researcher` with `taskPath`, a filesystem-safe `<topic-slug>`, and the exact question plus the decision it can change. Keep the source trigger separate: `assumption:<id>` or `classification:<tag>`.
- The researcher writes to `research/<topic-slug>.md`. Assumption-triggered work resolves the triggering assumption; classification-triggered work answers the decision question and is cited from the plan.
- Set `workflow.researchNeeded = true`; the researcher marks the matching pending entry complete.

When research is not required:

- Set `workflow.researchNeeded = false`.
- Record a one-line rationale in `orchestration_plan.md` under Research Decisions.

Workers must not perform external research unless the plan explicitly assigns them that responsibility. Research is centralized so its output is durable.

## Internal Recon

Run one recon pass over the codebase before deciding the execution path. This is separate from `technical-researcher`, which resolves external-system behavior and is explicitly not codebase analysis.

Recon exists for two reasons, and the ordering follows from the first:

1. **The path decision depends on it.** Separability and file ownership are unknowable before you know what the code looks like, so deciding first and scoring Worker clarity "low" is a verdict about the planner's information, not the task's shape.
2. **It is what makes decomposition cheap.** The expensive part of workers is not coordination, it is duplicated discovery — N workers each re-deriving the same conventions and reaching N different answers. One recon pass, cited by every brief, replaces that. It pays for itself at two workers, and on the direct path `contract-driven-execution` reads it instead of exploring, so it is never pure overhead.

Delegate it to one subagent with a bounded output contract. Recon that returns an essay has failed; recon returns a map.

**Read the durable layer first.** Most repos already carry one — a rules file (`CLAUDE.md` / `RULES.md`) and, in some, a curated how-to store (`ai/skills/<topic>/SKILL.md`). Read those before reading source, cite them rather than restating them, and cover only the delta: the specific subsystem this task touches. Never regenerate that layer as part of a task run; authoring a repo's rules file is a deliberate operator action, not a pipeline step.

The recon subagent returns, and writes to `<taskPath>/research/internal-recon.md`:

```markdown
# Internal Recon

## Durable sources read
- <path> — <what it already settles, so no worker re-derives it>

## Files in scope
- <path> — <role> — <touched by: which piece of work>

## Patterns to mirror
- <concern> → <exemplar file:line> — <the convention it encodes>

## Shared surface to freeze
- <interface / type / signature / test contract> — <who produces it, who consumes it>

## Disjoint sets available
- <set-name>: <paths> — independent of: <other sets>
- (or: "None — <why the work cannot be split by file>")

## Landmines
- <thing that will bite, with a path or a citation>
```

Skip recon only for non-code-bearing work, or when the task touches one file the main thread has already read. Record the skip and its reason under Research Decisions in `orchestration_plan.md`. Do not skip it because the task feels small — that judgment is the one recon exists to inform.

## Planning Rules

Use the contract (`prompt_contract.md`, `constraints.md`, `decisions.md`) and any completed research to bound the plan.

The plan must identify:
- what is in scope now
- what is blocked by uncertainty
- what can be delegated safely
- what must remain centralized
- what verification obligations remain after implementation

### Execution Path Decision

**The default is `decompose`. `direct` is the path that needs justifying.**

Hard triggers — take `decompose`, and no axis score overrides them:

- **The work spans more than one repository.** Separate trees, separate builds, no shared working copy: coupling is structurally low even when the change is logically one thing, and a single context is at its worst holding two repos' conventions at once.
- **Recon named two or more disjoint file sets.** A disjoint set is *proof* of low coupling, not an estimate of it. This replaces guessing at the Coupling axis.
- **Tests and implementation are both in scope and separable.** A worker that owns tests and may not touch `src` writes tests against the contract. A worker that owns both writes tests against whatever its code happens to do, then makes them pass. The isolation is the point — the same reason the code-reviewer runs blind.

`direct` is correct when recon found no disjoint sets, the change lives in one file or one tight cluster, or the shared surface cannot be frozen before the work starts. Whichever applies, the File Ownership section of `orchestration_plan.md` must **name the files that overlap**. "Decomposition would create coordination overhead" is a conclusion, not a reason; without the overlapping paths it is the old default wearing a decision's clothes.

Choose exactly one execution path. They are mutually exclusive for a given task.

### 1. Direct Path

Use the direct path when recon showed the work cannot be split by file — one coherent change, or a cluster too interdependent to partition — and the File Ownership section names the overlap.

On the direct path:

- Invoke `contract-driven-execution` with `taskPath`. It performs the work and updates `execution_notes.md` and `state.json`.
- After it returns, run the verifier subagent. Run the code-reviewer subagent after the verifier when the result is code-bearing.

### 2. Decompose Path

Run it as **freeze, then fan out**. The order is what removes the need to mediate between workers at runtime.

**Phase 0 — freeze the shared surface.** Everything two or more workers must agree on — interfaces, types, method signatures, DTO shapes, test contracts — is written before any consumer worker starts, from the Shared surface to freeze section of recon. Do it in the main thread when it is small, or give it to one worker whose only job is that surface. After phase 0, no worker needs anything another worker is still producing.

**Phase 1..N — fan out over disjoint file sets.** Assign each worker a file set no sibling may touch. Ownership is exclusive: a worker that needs a change in another set requests it, it does not make it.

Then:

- Decide the output expected from each piece before delegation.
- Capture the phase and dependencies of each worker explicitly in `orchestration_plan.md`.
- Record each worker in `state.json` under `workflow.workers`.
- Reassemble the finished pieces in the main thread before verification.

### Worker Continuity — Fresh vs Resumed

A worker can be given a phase boundary — "complete through X, report, then stand by" — and re-engaged later with its context intact, because a message to a named agent resumes it from its transcript.

**Default to a fresh worker with a precise brief.** Resuming replays the worker's whole transcript, so a worker that read forty files in its first phase pays for those forty files again on re-engagement. Good recon is what makes fresh workers sufficient: a brief specific enough to act on is cheaper than accumulated context.

Resume when the worker must react to feedback on **its own output** — integration conflicts in its own files, review findings on its own code, an iterative narrowing it already holds the state for. There the transcript is the value, not the cost.

Record the choice per worker in `orchestration_plan.md`. It is a cost decision, so it belongs in the plan rather than in the moment.

If execution reveals hidden complexity, invalid assumptions, or stronger-than-expected dependencies, re-run task analysis and switch execution path. Update `orchestration_plan.md` and `state.json` when reclassifying. Reclassify early; do not continue against a flawed decomposition.

## Orchestration Plan File

After the planning decisions are made, write `orchestration_plan.md` at the task root. This file is the durable record of every orchestration decision so a future conversation can resume the work without re-deriving it.

Required structure:

```markdown
# Orchestration Plan

## Problem Classification

- Contract sufficiency: sufficient | blocked — <missing information>
- Classes (maximum 3):
  - <ledger-compatible tag> — <why it applies>
  - (or: "No class applies — <concrete reason>.")
- Additional classified prior art: <new A-ids, or none — reason>
- Recon correction: <confirmed, rejected, or refined classification>
- New or changed artifacts:
  - <artifact> → <first existing consumer> → <what that consumer does with it>
  - (or: "None introduced — <reason>.")


### Naming — ids and names travel together

Every worker, attention item, and assumption carries a short `W<n>` / `R<n>` / `A<n>` key **and** a
descriptive name. The key exists so dependency order and the close-out scripts can reference a row
(`inputs: W0.output`, `mint-lesson.py` matching `^R\d+`). The name is what a person reads. It states
the item's target and the question it answers — `freeze-parser-output-schema`,
`yamladapter-sole-consumer`, `dock-mac-collision` — never a role plus a number.

**Never print a key alone in anything the operator reads.** Write `R2 (dock-mac-collision)` on first
mention in a section, and spawn every subagent under its descriptive name, not `W1`.

The one exception is a table cell the close-out scripts parse by position. Those are marked at each
table below and must stay bare.

### Attention Items

| id | name | failure mode | causal path and impact | planned handling | source |
|---|---|---|---|---|---|
| R1 | <descriptive-name> | ... | ... | `test:<path>::<name>` / `guard:<file>:<line>` / `research:<topic-slug>` / `accept: <reason>` | local citation or research pointer |

The name set here is the one every later reference uses — `R1 (dock-mac-collision)`.

Maximum 5 rows. If there are none: `No material attention items — <concrete reason>.`

### Research Questions

| topic slug | triggered by | question | decision it can change | authority | result |
|---|---|---|---|---|---|
| streaming-truncation-handling | classification:streaming | ... | ... | authoritative source for the active stack | pending / research/streaming-truncation-handling.md |

Maximum 3 rows. If there are none: `No research needed — <concrete reason>.`

## Complexity Decision
- Path: direct | decompose
- Axis scores: Complexity <low|medium|high> | Separability <...> | Coupling <...> | Dependency order <...> | Execution risk <...> | Worker clarity <...>
- Rationale: <1-3 lines>

## Research Decisions
- External topic: <topic-slug> — triggered by: assumption:<id> | classification:<tag> — status: pending | complete
- (or: "None needed. <one-line rationale>.")
- Internal recon: complete → research/internal-recon.md | skipped — <reason>

## File Ownership
(Required on both paths. This is the record that the path decision was made rather than defaulted.)
- Disjoint sets found: <n>
- W1 (<descriptive-name>) owns: <paths>
- W2 (<descriptive-name>) owns: <paths>
- Shared surface frozen in phase 0: <interfaces / types / test contracts>
- (direct path instead: "No disjoint sets. Overlapping paths: <the actual files>. <why they cannot be split>.")

## Worker Plan
(Only when path is decompose. Otherwise: "Not applicable — direct path.")
- W0 (<descriptive-name>) — scope: freeze shared surface  output: <the frozen artifacts>  phase: 0
- W1 (<descriptive-name>) — scope: ...  owns: <paths>  inputs: W0.output  output: ...  phase: 1  continuity: fresh
- W2 (<descriptive-name>) — scope: ...  owns: <paths>  inputs: W0.output  output: ...  phase: 1  continuity: resumed — <why the transcript is worth replaying>

## Synthesis Approach
<how worker outputs get integrated in the main thread; omit on direct path>

## Verification Obligations
- Cross-check against prompt_contract.md Success Criteria
- <any task-specific verification points>
```

Bind key fields to `state.json`:

- `workflow.executionPath`
- `workflow.orchestrationPlanFile` = `"orchestration_plan.md"`
- `workflow.researchNeeded`
- `workflow.researchTopics[]` (when applicable)
- `workflow.workers[]` (decompose path only)

`orchestration_plan.md` is the human-readable narrative. `state.json` is the machine-readable status. They are two views of the same truth and must not conflict. If they do, stop and reconcile.

## Workstream Design

The main thread owns:
- choosing the decomposition
- weighing dependencies
- deciding sequencing or parallelism
- preventing overlap
- integrating outputs
- resolving contradictions
- deciding whether repair is needed after verification

When delegating a worker task, give it:
- **its descriptive name from the Worker Plan, used verbatim as the Agent tool's `name`** — the
  operator must be able to tell what a worker is doing without opening its prompt, and a named agent
  is what makes re-engagement possible. Never spawn under `W1`, `worker-1`, or the agent type.
- a sharply scoped objective
- the exact inputs or artifacts it should use
- the specific output it must return
- **the file set it owns, and that it may not write outside it**
- any attention-item artifact (`test:` or `guard:`) that falls inside that file set, cited as `R<n> (<name>)` — the worker delivers it, it is not the verifier's to discover
- the frozen shared surface from phase 0, and the conventions recon found — cited, so it does not re-derive them
- the dependency context it needs
- its phase boundary, and whether it stands by for re-engagement
- which directional or domain skills, if any, it should load
- assumptions allowed
- assumptions forbidden
- whether it may perform further research

Workers execute. They do not redesign the plan.

### Blocked Returns — The One Escape Hatch

A worker may not invent a shared artifact. If it needs an interface, type, contract, or file that does not exist and is not in its own set, it stops and returns:

```text
BLOCKED: needs <artifact> — <why, and what it was about to have to invent>
```

The orchestrator then produces the artifact, or extends phase 0 and re-runs the affected workers. Two workers that each invent their own version of the same interface both report success, and synthesis receives two incompatible definitions — merged rather than reconciled, that conflict lands in the codebase looking deliberate. Planning reduces how often this happens; it never reaches zero, which is why the escape hatch is mandatory rather than advisory.

"Avoid overlap with sibling tasks" is not actionable on its own. A worker that discovers overlap needs somewhere to put it.

## Planning Depth

Match planning depth to task size.

On the direct path, keep the plan lightweight: brief analysis, direct execution, verifier, code-reviewer when code-bearing.

On the decompose path, follow Operating Rules in full and surface each step in the output.

Do not expose unnecessary internal detail for trivial cases.

## Synthesis

If worker tasks ran, synthesize before verification.

During synthesis:

- Merge outputs into one coherent result.
- Resolve contradictions and duplicated work.
- Check that each delegated task produced the expected output.
- Identify any gaps created by handoffs or hidden dependencies.

Do not hand verifier a pile of fragments and call it architecture.

## Verifier Subagent

Always run a final verifier subagent after execution. This is mandatory for every non-trivial task on every execution path. There is no skip path.

The verifier receives **full context**:

- The original user request
- `prompt_contract.md` — especially Success Criteria
- `orchestration_plan.md`
- `assumptions.md`, `decisions.md`, `constraints.md`
- Completed research files under `research/`
- The execution plan or worker decomposition
- The list of delegated worker tasks and their outputs
- Final artifacts produced
- `execution_notes.md`

The verifier must answer one question: **did the completed work satisfy the original request and the contract Success Criteria?**

The verifier must check:

- Coverage of every Success Criterion in `prompt_contract.md`
- Coverage of the original user request beyond the formal criteria
- Consistency between the contract, the plan, the tasks, and the execution
- Contradictions or drift from the contract
- Missing edge cases
- Unsupported or invalidated assumptions — dispose of every one; see Assumption Disposition below
- Every R-id from the plan's Attention Items table — dispose of each against what landed; see Attention Item Disposition below
- When research was performed, alignment of execution with the research findings, including forbidden assumptions and verify-first items
- When relevant: correctness of tool usage, factual grounding, testability, edge-case handling, and whether the deliverable is immediately usable
- Whether the final response actually answers the request

Use the checklist in [orchestration-rubric.md](./references/orchestration-rubric.md) to structure the pass.

### Assumption Disposition — Required Section

Every `verifier-N.md` must contain a disposition table with one row per assumption in `assumptions.md`:

```markdown
| id | status | name | citation | actor |
|----|--------|------|----------|-------|
| A1 | REJECTED | yamladapter-sole-consumer | HTTP 400 at 250 AIDs, correlation e1b72bd4 | verifier |
| A2 | NEVER-TESTED | tenable-export-finished | — | verifier |
```

**Column order is load-bearing. Do not reorder.** `mint-lesson.py` (`lesson_bearing_ids`) reads this
table positionally: `cells[0]` is the id and `cells[1]` must be the status. `validate-closeout.py`
reads the same table by header name. Moving `name` to column 1 makes the validator still look clean
while `mint-lesson.py` silently stops seeing REJECTED and NEVER-TESTED rows. Keep `id` and `status`
bare in columns 0 and 1; the name goes in column 2.

Rules:

- Compare against **what actually landed** — the diff from `state.json.baseRef` to the current state, the tests or runs actually performed, and the completed research files. Not against the plan's narrative, and not against what the contract intended. If `baseRef` is null, say so and scope the comparison to the artifacts named in `execution_notes.md`.
- **NEVER-TESTED is the default.** A status becomes VALIDATED or REJECTED only when you can cite the specific diff hunk, test, log line, correlation ID, or research file that moved it. No citation → NEVER-TESTED. Do not infer that an assumption held because the work completed.
- An assumption still OPEN at this point is a verifier finding, ranked with the rest. It feeds the normal repair cycle: either produce the evidence, or record it as NEVER-TESTED with the resulting risk stated.
- A REJECTED assumption must record what contradicted it, and what must not be re-assumed without new evidence.
- Also record **decision drift**: for each entry in `decisions.md`, whether it landed as decided, changed during execution (with the reason), or was abandoned.
- Write the dispositions back into `assumptions.md`. These statuses are terminal — no other skill may rewrite them.

Silence is the dominant failure mode here. An assumption nobody revisited looks identical to one that held, and NEVER-TESTED exists to make that distinction impossible to skip.

### Attention Item Disposition — Required Section

Place this section **after** the Assumption Disposition table. Every `verifier-N.md` must contain exactly one row for every R-id in the plan's Attention Items table:

```markdown
| id | final disposition | name | evidence |
|---|---|---|---|
| R1 | handled | dock-mac-collision | the artifact named in the plan, and the result of resolving it |
| R2 | accepted-risk | vmware-serial-reuse | impact and proportional rationale |
| R3 | not-applicable | bios-hash-drift | evidence disproving the causal path |
| R4 | unresolved | cursor-replay-window | blocker or missing evidence |
```

**Column order is load-bearing. Do not reorder.** `mint-lesson.py` matches `cells[0]` against
`^R\d+[a-z]?$` and requires `cells[1]` to be exactly `not-applicable` to find lesson-bearing items.
Keep the id and the disposition bare in columns 0 and 1; the name goes in column 2.

`handled` is a check, not a judgment: the artifact the plan named must resolve — the test exists and passes, or the guard is still present at its citation. Run it or read it, and record what came back. An artifact that does not resolve is `unresolved`, never `handled`, however reasonable the intention behind it was. This is the same device as File Ownership: a claim about named files or named tests is checkable, and a claim in prose is not.

The header must use `final disposition`; it must never contain the word `status`, which is reserved for the assumption table consumed by close-out tooling. Allowed values are `handled`, `accepted-risk`, `not-applicable`, and `unresolved`. Evidence or rationale is required for every row. `unresolved` is a verifier finding and prevents a clean pass; it may be repaired in a later verifier cycle. A reusable `not-applicable` result is lesson-bearing. Reusable handled or accepted risks enter the ledger only through the existing decision-drift path.

The verifier writes its output to `review/verifier-N.md`, where `N` increments on each repair cycle (`verifier-1.md`, `verifier-2.md`, ...). Existing files are not overwritten. The disposition table is rebuilt each cycle, so drift is recorded per cycle rather than as a single post-mortem.

Update `state.json`:

- `workflow.verifierRun = true` once the pass completes (pass or fail).
- Append a `skillsRun` entry referencing the output file.

If the verifier finds issues:

- Repair them before finalizing when feasible. Re-run the verifier after repairs; produce a new `verifier-N.md`.
- Otherwise state the unresolved gaps explicitly in the final response.

Do not suppress verifier findings just to keep the flow tidy.

## Code-Reviewer Subagent

After the verifier pass, run a separate code-reviewer subagent when the work is code-bearing: code changes, configuration with runtime effect, infrastructure, migrations, public contracts, SDK/API surfaces, scripts, or implementation-specific architecture/design.

Load and follow `code-reviewer/SKILL.md` for scope boundary, allow-list, review lenses, and output format.

### Sender-Side Enforcement

The orchestrator constructs the subagent prompt. The prompt **must NOT include**:

- The original user request.
- `prompt_contract.md`, Success Criteria, or any contract artifact.
- `orchestration_plan.md`, worker decomposition, or synthesis notes.
- Verifier output, verifier verdict, or repair history.
- Any framing of the form "this satisfied the requirement" or "this passed verification."

If you find yourself wanting to pass any of these "for context," stop. That context is exactly what contaminates an independent code review.

### Output and State

The subagent writes `review/code-reviewer-N.md` (increment `N` on each repair cycle; never overwrite). Update `state.json`: set `workflow.codeReviewerRun = true` and append a `skillsRun` entry.

### Repairs

Repair material findings when the fix is bounded and feasible. If a repair changes request coverage, re-run the verifier and produce `verifier-<N+1>.md`. If repair is not feasible, record the accepted risk and distinguish it from verifier gaps.

Do not suppress findings to keep the final answer clean.

## Output Style

Keep orchestration details internal by default. The durable record lives in `orchestration_plan.md`, `state.json`, `execution_notes.md`, and `review/`. The final response is a pointer to those artifacts plus any unresolved items, not a restatement of them.

Surface analysis, decomposition, dependency reasoning, or verifier detail only when the task is on the decompose path, the user asked for it, or exposing it materially improves the answer.

On the direct path, keep orchestration compact and mostly implicit in the final response. On the decompose path, surface the steps of Operating Rules in the response order.

Compress routine details. Expand only where the execution strategy, verifier findings, or code-reviewer findings materially matter.

## Failure Modes To Avoid

Patterns not surfaced by the rules above:

- Executing without a finalized `prompt_contract.md`.
- Passing the contract, verifier output, plan, or user request into the code-reviewer "for context."
- Letting workers perform research the plan did not assign.
- Treating planning as an end product.
- Letting synthesis drift away from the original request.
- Returning a polished answer that does not actually satisfy the task.
- Marking an assumption VALIDATED because the work completed, rather than because evidence moved it.
- Leaving assumptions undisposed, which reads as "held" and is the failure this pipeline has produced most often.
- Producing generic classes or attention items with no causal path, planned handling, or task-specific citation.
- Mapping the existing topology carefully and never projecting the new artifact into it, so an interaction that invalidates the design is met at runtime instead of at planning.
- Writing a tripwire test that builds its own convenient version of the artifact instead of driving the production path, so the test and the shipped code diverge silently.
- Recording a planned handling that names an intention rather than a test, guard, research topic, or explicit acceptance. A handling nothing can resolve is an undisposed assumption wearing a plan's clothes.
- Treating classification as fixed after recon or later evidence changes the problem shape.
- Asking research questions that cannot change a named solution decision.
- Choosing `direct` without naming the overlapping files in the File Ownership section — the decomposition equivalent of an undisposed assumption.
- Deciding the execution path before recon, so Separability and Worker clarity score the planner's ignorance rather than the task.
- Spawning or reporting a worker, attention item, or assumption under its bare key, so the operator must open another file to learn what it is.
- Reordering the columns of a disposition table, which breaks `mint-lesson.py` silently while `validate-closeout.py` still reports clean.
- Letting a worker invent a shared interface instead of returning `BLOCKED:`, and letting synthesis merge two incompatible versions of it.
- Letting workers each rediscover the same conventions because recon was skipped or its output was not cited in the briefs.
- Resuming a worker whose transcript is large when a fresh brief would have done, or spawning fresh when the worker had to react to feedback on its own code.

## Minimal Invocation Pattern

When this skill triggers, internally follow this compact prompt shape:

1. Read `state.json`, `prompt_contract.md`, and `assumptions.md` from `taskPath`.
2. Classify from the contract; stop if it is too thin to classify.
3. Resolve only research that blocks recon, then run internal recon and write `research/internal-recon.md`.
4. Correct the classification; record at most five attention items and three decision-changing research questions. Run required research.
5. Write the bounded Problem Classification section. Only then choose direct vs decompose and complete `orchestration_plan.md` and `state.json`.
6. Execute directly or through frozen, disjoint worker sets; synthesize worker output.
7. Run the verifier with full context; require assumption and attention-item dispositions; repair and re-run when needed.
8. For implementation artifacts, run the isolated code-reviewer with minimal context and repair material findings.
9. Append final `execution_notes.md` and return the assumption, attention-item, and decision-drift rows.

Use judgment. The point is to improve execution quality, not to build a bureaucracy in miniature.

---
name: technical-researcher
version: 1.2.0
description: Research external technical targets and produce decision-support recommendations grounded in official docs, official-adjacent artifacts, and community evidence. Use when investigation of a product, API, platform, tool, integration, library, SDK, service, or protocol is required for integration design, implementation planning, bug investigation, capability verification, migration analysis, or compatibility analysis. Typically invoked by `task-orchestrator` to resolve an OPEN external-behavior assumption recorded in `assumptions.md`.
---

# Technical Researcher

## Overview

Use this skill to research an external technical target and produce the most defensible next action for downstream work.

Treat repository context as supplementary only. Do not turn this skill into codebase analysis.

## Inputs

When invoked as part of a workflow, this skill receives:

- `taskPath` — path to `ai/active/<timestamp>_<task-slug>/`.
- `topic` — short slug for the research subject (used as both the file name and the `workflow.researchTopics[].topic` value).
- `triggeringAssumptionId` — optional. The id of the OPEN assumption in `assumptions.md` that this research is intended to resolve.

When invoked standalone (no `taskPath`), produce the same structured output but skip the persistence steps.

## Outputs

When `taskPath` is provided, write the research output to:

```text
<taskPath>/research/<topic>.md
```

If the file already exists, update it in place rather than creating a duplicate. State.json tracks status; the file holds the content.

After writing the research file, update task state:

- In `state.json`, mark the matching entry in `workflow.researchTopics` as `status: "complete"` and append a `workflow.skillsRun` entry.
- In `assumptions.md`, resolve the triggering assumption: move it to `VALIDATED` or `REJECTED` with actor `researcher`, a one-line reason, and a citation — the vendor doc URL or source `file:line` the finding rests on, plus a pointer to the research file. A pointer to your own research file is not by itself a citation; the underlying source is.
- In `decisions.md`, add an entry only when the research produces a stable rule that affects future execution.

If the triggering assumption cannot be resolved by the research (because the evidence is insufficient or contradictory), leave it `OPEN`, record the limitation in the research file, and surface this to the caller. Do not force a resolution.

## Core Operating Rules

Follow these rules on every run:

1. Use web research as the primary mechanism.
2. Use official documentation when it exists and is accessible; fall back to official-adjacent artifacts before community sources.
3. Use community sources to expose real-world behavior, not to replace official baselines without explanation.
4. Cross-reference findings against the actual task, constraints, and environment.
5. Produce fewer, deeper findings rather than a broad but shallow inventory. Cover the problem space first, then narrow to the most relevant insights.
6. Produce a recommendation, not a summary, bibliography, or link dump.
7. Distinguish clearly between "not found" and "likely exists but inaccessible."
8. Do not treat missing public docs as proof that a capability does not exist.
9. Do not present inference, anecdote, or speculation as confirmed fact.
10. Do not imply completeness when evidence is partial or uncertain.

## Workflow

### 1. Frame the task

Extract:

- target system or technical subject
- task or decision to support
- context and constraints
- missing variables that could materially change the outcome

Classify the task as exactly one of:

- integration
- implementation
- bug investigation
- capability exploration
- migration or compatibility analysis
- other, explicitly stated

Read [task-emphasis.md](./references/task-emphasis.md) when choosing depth and emphasis.

### 2. Evaluate documentation access

State exactly one documentation status:

- public official docs available
- official docs partially gated
- official docs fully gated
- no official docs found

Use `partially gated` or `fully gated` when official documentation likely exists but is not publicly accessible.

### 3. Gather evidence

Use the source order and claim labeling rules in [research-standards.md](./references/research-standards.md).

From official and official-adjacent sources, extract what is relevant:

- baseline behavior
- supported capabilities
- documented constraints
- API or protocol structure
- authentication and authorization patterns
- versioning details
- compatibility requirements
- hosting or deployment assumptions
- limits, quotas, rate limits, and support boundaries

From community sources, extract what is relevant:

- real-world issues
- edge cases
- undocumented behavior
- performance concerns
- migration pain points
- workarounds
- implementation traps
- failure patterns
- practical usage notes

### 4. Cross-reference and validate

Explicitly compare:

- source against source
- source against task requirement
- source against stated constraints

Call out:

- agreements
- contradictions
- missing evidence
- likely reasons for mismatches when inferable

Do not silently merge conflicting claims.

### 5. Decide

Answer this question directly:

`What is the most defensible next action?`

Anchor the recommendation to the evidence quality, the task type, and the unresolved unknowns.

### 6. Produce the output

Use the exact output structure from [output-contract.md](./references/output-contract.md).

When `taskPath` is provided, write that structured output to `<taskPath>/research/<topic>.md` and update `state.json` and `assumptions.md` as described in the Outputs section. The final response to the caller is a brief pointer to the file plus the recommended next action — not a duplicate of the file's content.

When `taskPath` is not provided, return the structured output inline.

Keep the response decision-oriented in either mode:

- no generic recap section
- no standalone bibliography
- no dump of every source found
- no unnecessary restatement of the prompt

## Required Behaviors

### Degrade gracefully

When official docs are missing, gated, outdated, or incomplete:

1. State the limitation plainly.
2. Shift toward official-adjacent evidence.
3. Use community evidence more aggressively, with carefully labeled certainty.
4. Lower confidence where direct confirmation is missing.
5. State when authenticated docs, tenant access, or hands-on testing are required for a definitive answer.

### Keep the output automatable

Write so downstream implementation skills consume the result without re-researching the topic.

Include:

- explicit assumptions
- claim-level confidence where certainty differs
- actionable sequencing
- clean separation between implement-now work and verify-first work

## Implementation Notes

Include code sketches only when they materially improve the recommendation.

When the user does not specify a stack, use practical pseudocode or broadly understandable examples over framework-heavy samples. When the task implies a stack, align examples to that stack.

## Citations

Cite every source used. Attach citations to claims or group by section. Use concise labels, not long raw URLs in the body.

Role:
You are a Codex skill maintainer.

Goal:
Create a new Codex skill named `contract-driven-execution` under `/Users/user/.codex/skills` from the user-provided Agent Instructions.

Context:
The user wants a reusable skill that makes agents work from explicit prompt contracts and task state instead of vague instructions. The source instructions require using `prompt-contract-designer` before non-trivial tasks, reading task state files under `ai/active/<timestamp>_<task-slug>/`, treating `prompt_contract.md` as authoritative, and updating state after execution.

Constraints:

* Use `prompt-contract-designer` before execution.
* Use the `skill-creator` workflow.
* Install the skill under `~/.codex/skills` unless a different location is explicitly provided.
* Use the skill name `contract-driven-execution`.
* Base the skill instructions on the user-provided Agent Instructions.
* Do not overwrite unrelated skills or task state.
* Do not write outside the repo without approval when sandbox restrictions require it.
* Validate with `quick_validate.py` after creation.
* Update `execution_notes.md` and `state.json` after execution.

Success Criteria:

* `/Users/user/.codex/skills/contract-driven-execution/SKILL.md` exists.
* `agents/openai.yaml` exists and matches the skill.
* The skill frontmatter contains only `name` and `description`.
* The skill body clearly defines when to use `prompt-contract-designer`, how to locate/read task state, how to execute from `prompt_contract.md`, and how to update state afterward.
* Skill validation passes, or a blocker is recorded.
* Local task state is updated after execution.

Execution Rules:

* Read `state.json` first, then the task markdown files before execution.
* Do not assume missing data.
* Respect constraints strictly.
* Request approval before sandbox-restricted writes outside writable roots.
* Keep final reporting concise and include validation status.

Output Format:
A concise final response with the created skill path, validation result, and any limitations.

Stop Conditions:

* When the goal is achieved.
* When required data is missing.
* When a required write outside writable roots is denied or cannot be completed.

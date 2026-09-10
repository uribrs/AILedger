# Orchestration Rubric

Use this reference when deciding between the `direct` and `decompose` execution paths, when specifying the final verifier run against the current PromptContract artifact's Success Criteria, and when specifying the isolated code-reviewer run. Do not load it for obvious trivial cases.

## Simplicity Decision Heuristic

Score each axis as low, medium, or high. Record all six scores in `orchestration_plan.md` — an unrecorded score cannot be reviewed, and the bias toward `direct` at the end of the Decision Guide only stays honest if the signals it overrode are visible.

### Complexity

- Low: one clear action or one tightly related set of changes
- Medium: several steps with some judgment or iteration
- High: many moving parts, broad scope, or significant uncertainty

### Separability

- Low: work shares one context and frequent back-and-forth
- Medium: some parts can separate, but integration matters
- High: parts can proceed independently with bounded handoffs

### Coupling — a predicate, not a score

Do not estimate this one. Read it off internal recon:

- Low: recon named two or more **disjoint file sets**. A disjoint set is proof, not an estimate.
- High: recon found no split, and the overlapping paths are named in the plan.

If recon has not run, this axis has no value yet and the path decision is premature.

### Execution Risk

- Low: failure is easy to spot and cheap to fix
- Medium: some regressions, omissions, or confusion are likely
- High: silent errors, high rework cost, or strong correctness demands

### Dependency Order

- Low: tasks are mostly independent
- Medium: some ordering exists, but boundaries remain usable
- High: most tasks depend directly on prior task outputs

### Worker Clarity

- Low: pieces are hard to define without ambiguity
- Medium: tasks can be defined, but require careful wording
- High: each piece can be assigned with explicit inputs and outputs

## Decision Guide

**Run recon before reading this section.** Deciding first makes Separability and Worker clarity measure how little you currently know.

### Hard triggers — `decompose`, regardless of the axis scores

- The work spans more than one repository.
- Recon named two or more disjoint file sets.
- Tests and implementation are both in scope and separable.

### `direct` (dispatch to `contract-driven-execution`)

Correct when recon found no split. Supporting signals: Complexity low or medium, Dependency order high, one tight cluster of changes. The plan must name the **overlapping files** — not merely assert coordination overhead.

### If the signals are mixed

Take `decompose` when a disjoint file set exists, `direct` when it does not. The file sets settle it; the remaining axes shape *how* to decompose, not *whether* to.

Adding workers is cheap to talk about and expensive to coordinate — but that cost is duplicated discovery, not coordination itself, and recon is what removes it. Two workers over disjoint files with a frozen shared surface cost close to nothing to coordinate. Do not charge that case the price of a six-worker interdependent split.

## Decomposition Checklist

Before delegating any worker task, confirm:

- The objective is specific
- Inputs are explicit
- Output is bounded
- **The worker owns a named file set, and no sibling shares a path in it**
- **Everything two workers must agree on was frozen in phase 0**
- The worker knows to return `BLOCKED:` rather than invent a shared artifact
- The brief cites recon's conventions instead of leaving the worker to re-derive them
- Dependencies and phase are explicit
- The main thread has a clear integration plan

If the file sets are not disjoint, fix the split — do not fall back to `direct` while a workable partition exists. If no partition exists, name the overlap and go direct.

## Verifier Checklist

The verifier should inspect the final state against these anchors:

1. The original user request
2. `prompt_contract.md` — especially Success Criteria and Constraints
3. `orchestration_plan.md` — execution path, research decisions, worker plan
4. Claims, decisions, and constraints in the current context manifest, plus any completed `research/<topic>.md` files
5. Delegated worker outputs and the synthesized result
6. `execution_notes.md` and the final produced artifacts

Questions to answer:

- Was every Success Criterion in `prompt_contract.md` satisfied?
- Did execution cover the stated request beyond the formal criteria?
- Did execution honor every Constraint?
- Did the execution-path decision (direct vs decompose) still make sense in hindsight? On the `direct` path, does the File Ownership section name the overlapping files, or does it only assert coordination overhead? An unjustified `direct` is a finding, ranked with the rest.
- Did any worker write outside its declared file set, or invent a shared artifact instead of returning `BLOCKED:`?
- Did the work drift from the contract, the orchestration plan, or the worker tasks without justification?
- Are there contradictions across outputs, claims, or files?
- Are edge cases or failure paths missing?
- Is every assumption disposed with an actor and a citation? Which are NEVER-TESTED, and what risk does each carry?
- Did any governed decision change or get abandoned during execution, and was the reason recorded?
- When research was performed, does the execution align with the research findings (including forbidden assumptions and verify-first items)?
- When relevant, are tool usage, factual grounding, testability, immediate usability, and edge-case handling sound?
- Is the output complete enough to be useful now?
- Does the final answer actually answer the user, rather than merely describe the work?

The verifier must prioritize the original user request and the contract Success Criteria over the orchestration plan. If the plan is incorrect or incomplete, it must be challenged.
A consistent result is not sufficient if it does not satisfy the original request.

## Code-Reviewer Boundary

For invocation context, allow-list, deny-list, scope separation, and review lenses, see `code-reviewer/SKILL.md`.

## Repair Policy

For the verifier and code-reviewer repair flow, see the Verifier Run and Code-Reviewer Run sections in `task-orchestrator/SKILL.md`.

## Practical Biases

Use these biases to keep orchestration proportionate in both directions:

- Bias toward the `direct` path for tasks that genuinely live in one file or one tight cluster.
- Bias against worker delegation when scopes are fuzzy — **and toward fixing the scope rather than abandoning the split**, when recon shows a partition exists.
- Bias toward `decompose` whenever a disjoint file set exists, even for medium tasks. Two workers over separate files is the cheap case, not the expensive one.
- Bias toward one thinking layer in the main thread: plan, sequence, and synthesize centrally; do not let workers redesign.
- Bias toward doing discovery once, centrally, and citing it — never N workers rediscovering the same conventions.
- Bias toward explicit verification every time, regardless of path.
- Bias toward explicit scope separation between verifier (full context) and code-reviewer (minimal context).
- Bias toward resolving OPEN external-behavior assumptions through `technical-researcher` before execution, rather than letting workers guess.

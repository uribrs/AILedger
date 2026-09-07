# Orchestration Plan

## Complexity Decision

- Path: **direct**, run three times — once per commit, with the full review gate between passes.
- Rationale: three file-disjoint edits in one repo where compile → test → re-measure iteration matters
  more than handoff, and integration is trivial because the commits share no file. Worker
  decomposition would give each agent the whole repo and the whole build to reproduce, for an
  integration step that is `git commit`. The parallelism the operator asked for is in the **review**
  passes, not execution.
- Scored: Complexity medium, Separability high, Coupling low, Dependency order forced by the review
  gate rather than by data, Execution risk low for C1/C3 and medium for C2, Worker clarity high.

## Research Decisions

None needed. All three OPEN assumptions (A1, A2, A3) concern this repository's own code and are
resolved by reading call sites and mutation-testing existing tests. No external-system behaviour is
in question — no vendor API, no library internal, no protocol detail. `workflow.researchNeeded = false`.

## Worker Plan

Not applicable — direct path.

## Review Gate — nine passes, three per commit

Per commit, before the next begins:

| pass | context | lens |
|---|---|---|
| verifier | full — contract, assumptions, decisions, diff, measurements | did this close what it claims, without drift? |
| code-reviewer A | **minimal** — the diff and the touched files only | correctness and behaviour preservation |
| code-reviewer B | **minimal** — the diff and the touched files only | design: do the new types earn their place? |

Sender-side enforcement, checked before each reviewer is dispatched: no contract, no plan, no
verifier output, no user request, no sibling reviewer's output, and **no mention that a rule audit or
`CLAUDE.md` motivated the change**. A reviewer told "this satisfies rule 5" grades the rule instead of
the code. The two code reviewers get deliberately different lenses so the second pass is independent
rather than redundant.

Outputs: `review/verifier-C{n}.md`, `review/code-reviewer-C{n}-a.md`, `review/code-reviewer-C{n}-b.md`.
Never overwritten.

## Deliberate departure: reviews run pre-commit

The predecessor branch reviewed after committing, which produced 6 of its 16 commits as
"fix: N review findings" — every review finding cost a SHA, and the operator asked for three commits
here. So each commit's three review passes run against the **working-tree diff**, findings that matter
are folded in, and the commit lands once. The branch ends with exactly three commits.

Cost of this choice, stated: the reviewed artifact is not yet a commit, so a reviewer cannot cite a
SHA. Mitigation: `execution_notes.md` records the diff each review saw, and the final SHA is written
into the review file after the commit lands.

## Synthesis Approach

Not applicable — direct path, and the three commits are file-disjoint by A4, so there is nothing to
merge. The only cross-commit integration is the final re-measurement, which must show
rule 5 = 41, rule 1 = 2, rule 3 = 3, rule 4 = 3, total = 49 — or report the discrepancy.

## Verification Obligations

- Every Success Criterion in `prompt_contract.md`, checked individually rather than in aggregate.
- A1, A2, A3 each moved to VALIDATED or REJECTED with the evidence recorded. None left OPEN.
- **A5's consequence, which is the sharpest risk in C1:** the classifier is unreachable from tests, so
  its 709-test coverage is entirely indirect. Break its behaviour and watch a specific test fail
  before claiming the refactor is covered. "The suite is green" is not evidence here.
- **A1's gate, which is the sharpest risk in C2:** 52 test call sites change. Mutation-test at least
  5 across at least 3 paginators. A test that keeps compiling while asserting less is the exact
  failure the predecessor branch shipped twice.
- No new record whose members any consumer only partly uses. Checked per record, per call site.
- Host boundary: `git status --porcelain` in `cymulate-integration-adapters` empty at the end.
- Determinism: ≥12 trx runs, counts parsed out of the trx XML, zero failures.
- Documentation: `CHANGELOG.md` in each commit; `progress_log.md` current at each boundary;
  `state.json` step status and review slots filled at each boundary; commit messages carrying only
  measured numbers.

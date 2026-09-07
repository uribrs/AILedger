# Prompt Contract — Rule-Compliance Decomposition

## Role

You are a senior .NET engineer closing measured rule violations in a library whose only safety net is
its test suite, on a branch whose predecessor cost the operator a day by claiming more than it
verified.

## Goal

Close 25 of the 74 measured `CLAUDE.md` violations in `src/` across **three independent commits**,
changing no host-visible signature and leaving the engine core untouched.

## Context

Repo `/Users/user/Dev/yaml-adapter-engine`, branch `quality-upgrade` off `dev` at `3af1248`, tree
clean.

**Baseline, verified before contract design — treat as fact:** 709 passed / 0 failed / 0 skipped.

**Violation baseline**, from a brace-accurate parser over 192 files with 0 parse failures, validated
against `IntegrationEngine.cs` = 1,936 lines and `ExecuteOperationCoreAsync` = 725 lines. Parser:
`/private/tmp/claude-501/-Users-user-Dev-yaml-adapter-engine/172b0760-6cbd-482c-bffc-926b08d93d22/scratchpad/rules_audit.py`.

| rule | baseline | target | note |
|---|---|---|---|
| 1 — behaviour in `Contracts/` | 5 | 2 | C3 closes 3 |
| 3 — methods > 100 lines | 3 | 3 | all three out of scope |
| 4 — classes > 500 lines | 3 | 3 | all three out of scope |
| 5 — 4+ effective params | 63 | 41 | C1 closes 5, C2 16, C3 1 |
| **total** | **74** | **49** | across 24 of 118 `src` files |

Effective params exclude `CancellationToken` and constructors, per rule 5's carve-outs.

**Read first, authoritative, do not re-derive:** `CLAUDE.md` (the seven rules, the "Before you change
anything" checks, the public-surface policy) and `ARCHITECTURE.md`. Then `constraints.md`,
`assumptions.md`, `decisions.md` in this directory.

**Read as binding process constraint, not background:**
`../2026-07-28_1258_sink-inversion/retrospective.md`. Its eleven checks are the reason this contract
is shaped the way it is. The pattern it names — *a claim made at a wider scope than what was
verified, always in the direction of "done"* — is the thing to watch for in yourself.

## Execution Steps

### C1 — `EngineFailureClassifier` parameter records → closes 5 × rule 5

`Resilience/Logic/EngineFailureClassifier.cs`, 297 lines, `internal sealed`. Violations:
`ClassifyResponse` (9p), `MatchRule` (6p), `EvaluateRules` (6p), `BuildDecision` (5p),
`IsExpiredCursor` (4p).

Shape is fixed by **D1**: three composed `internal` records in `Resilience/Contracts/Models/`, one
type per file. Resolve **A3** first by reading both call sites (`IntegrationEngine.cs:580` and
`:1821`). If the hydrate path needs any member of the wider context, the composition is wrong — stop
and report; do not fall back to one flat carrier.

Call sites: 3, all in `IntegrationEngine.cs` (`:467` `ClassifyException`, `:580` `ClassifyResponse`,
`:1821` `MatchRule`). **Zero test call sites** — A5. Because coverage here is only end-to-end, break
the classifier's behaviour and watch a test fail before claiming the refactor is covered.

### C2 — `IPaginator` parameter record → closes 16 × rule 5

`Pagination/Contracts/Interfaces/IPaginator.cs`, six paginators in `Pagination/Logic/Paginators/`,
and `NoOpPaginator` inside `PaginatorFactory.cs`. Two methods × 8 types = 16.

Shape is fixed by **D2**: one `public` record for the `(PaginationConfig, PaginationState)` pair —
`public` deliberately, per **D3**. `UpdateState`'s optional `responseHeaders` default does not
survive, per **D4**.

**This is the risk in the task and must not be described as mechanical.** 3 call sites in `src`
(`IntegrationEngine.cs:948`, `:996`, `:1376`) and **52 in `tests/`**. **A1 gates this commit:** for at
least 5 of the 52 sites spanning at least 3 paginators, break the paginator's behaviour and confirm
the updated test still fails. A test that keeps compiling while asserting less is the specific failure
this gate exists to catch.

### C3 — extract the merge-join parser out of `Contracts/` → closes 3 × rule 1, 1 × rule 5

`Workflow/Contracts/Models/MergeIntoConfig.cs`, 296 lines. Its three bodied methods are one concern
and move together to `Workflow/Logic/Merging/`: `TryParseJoin` (82 lines, 2p),
`TryParseExpression` (55 lines, 4p), `CountOccurrences` (11 lines, 2p).

Two corrections to any earlier framing, per **D6**: `TryParseJoin` is 82 lines, **under** the limit,
so C3 closes **zero** rule-3 violations; and it takes **2** parameters, not 4. The single rule-5
closure is `TryParseExpression`, and only if its three `out` parameters become a result type — **A2**.
If that reshape is unsafe, C3 closes rule 1 only and the final total is reported as 50, not 49.

Call sites: 2, both in `src` — `YamlIntegrationLoader.cs:420` and `MergePlan.cs:44`.
`MergePlan.Create` and `XmlShapingConfig.ToOptions` stay where they are — **A6**. Rule 1 ends at 2,
not 0.

## Per-Commit Review Gate — operator directive, binding

**Each commit gets ONE verifier pass and TWO INDEPENDENT code-reviewer passes before the next commit
begins.** Not once for the task.

- The two code reviewers do not see each other's output.
- All three review prompts carry **minimal context only**: no contract, no plan, no verifier output,
  no user request, and no mention that a rule audit motivated the change.
- Findings that matter: **fix immediately, do not consult.**
- Findings that do not: record in `defect_register.md`, surface in the final report.
- Each commit lands and work proceeds. No approval gate.

Precedent: on the predecessor branch the reviewers found more real defects than the 709-test suite,
including a live regression that all 709 tests passed over.

## Success Criteria

- Three commits on `quality-upgrade`, one per C1/C2/C3, no file in more than one.
- Re-measured with the same parser: rule 5 = 41, rule 1 = 2, rule 3 = 3, rule 4 = 3, total = 49.
  A different result is **reported as a discrepancy**, never reconciled by moving the target.
- Tests after each commit: ≥ 709 passed, 0 failed, 0 newly skipped.
- Determinism: ≥ 12 runs with `--logger trx`, counts parsed from the trx files, zero failures across
  all runs. Actual numbers reported.
- No new grab-bag record: every new record's members are used in full by every consumer that receives
  it.
- `git status --porcelain` in `cymulate-integration-adapters` is empty. No `Cymulate.*` reference
  added.
- One top-level type per file for every new type; each in its concept's `Contracts/Models/` or
  `Logic/`.
- A1, A2 and A3 each resolved to VALIDATED or REJECTED with the evidence recorded — not left OPEN.
- Per commit: `CHANGELOG.md` entry, `progress_log.md` update, correct `state.json` step status, all
  **in that commit**.
- Every commit message carries the measured before/after violation count for that commit and the test
  count.

## Execution Rules

- Do not assume missing data. Read the call sites; do not infer them.
- Before writing "fixed": grep the symbol and count the sites.
- Before claiming a test covers something: break the behaviour and watch it fail.
- Never write a number you have not measured, least of all in a commit message.
- Respect constraints strictly. Scope creep in execution is a defect, not initiative.
- Surface defects in `defect_register.md` as they surface; do not absorb them into a refactor.
- Report honestly: if a gate cannot be met, say so with the numbers.

## Output Format

- Code in `src/` and `tests/`.
- `progress_log.md` — created before the first commit, updated in each.
- `execution_notes.md` — per-commit outcome, deviations, anything surprising.
- `defect_register.md` — findings with status.
- `state.json` — step statuses and review slots filled at every commit boundary.
- `review/verifier-C{1,2,3}.md`, `review/code-reviewer-C{1,2,3}-{a,b}.md`.
- Final report: **final state of the whole task only, not per-step narration.** Baseline vs final
  counts per rule, the three SHAs, test counts, determinism numbers, what each reviewer found and
  what was done about it, and anything left open.

## Stop Conditions

- A3 fails: the hydrate path needs the wider context, so C1's composition is wrong.
- A1 fails: a test call site cannot be translated without weakening an assertion.
- A change would alter a host-visible signature, or require touching the monorepo.
- Test count falls below 709, any test fails, or determinism cannot be shown across 12 runs.
- A fix causes more failures than it resolves.
- Closing a violation would require touching an out-of-scope file.

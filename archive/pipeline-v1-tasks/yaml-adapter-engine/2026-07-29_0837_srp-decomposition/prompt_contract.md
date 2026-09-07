# Prompt Contract — SRP Decomposition

## Role

You are a senior .NET engineer decomposing the three largest classes in a library whose only safety net
is its test suite — and whose test suite has already been proven, twice, to pass over broken behaviour.

## Goal

Decompose `IntegrationEngine`, `WorkflowRunner` and `YamlIntegrationLoader` for `CLAUDE.md` rules 3, 4
and 7, so that each resulting type has one reason to change. Parameter-count reductions must be a
*consequence* of the new seams, never the objective.

If a step reduces rule-5 violations without reducing the number of responsibilities in the type, it has
done the wrong thing and must be reworked.

## Context

Repo `/Users/user/Dev/yaml-adapter-engine`, branch `quality-upgrade` (published), base `df5851d`, tree
clean. Task 2 of 3; task 1 is `ai/active/2026-07-28_1815_rule-compliance-decomposition` (complete).

**Baseline, measured 2026-07-29T08:37:42Z — treat as fact:** 710 passed / 0 failed / 0 skipped, suite
completes in ~0.9 s. 127 `src` files, 77 test files.

| target | lines | largest method | rules |
|---|---|---|---|
| `Execution/Logic/IntegrationEngine.cs` | 1,941 | `ExecuteOperationCoreAsync` 729 | 3, 4, 7, 19 × 5 |
| `Workflow/Logic/WorkflowRunner.cs` | 952 | `RunAsync` 155 | 3, 4, 7, 8 × 5 |
| `Definition/Logic/Loading/YamlIntegrationLoader.cs` | 672 | none over 100 | 4, 7, 2 × 5 |

**`ARCHITECTURE.md`'s ledger understates all three** (1,902 / 912 / 655) and its violation counts
(48 / 1 / 3 / 3 / 41) have **no reproducible source** — the parser that produced them died with a session
temp directory. Re-measure first; do not trust any figure in this contract that S0 can check.

**Why this task exists, and why in this order.** Task 1 closed rule 5 and rule 1 on the very classes
this task takes apart. `CLAUDE.md` rule 7: 3/4/5 are smoke detectors, SRP is the fire, and the question
is never "how do I get under the number?" Task 1 asked exactly that question. **Task 2 may legitimately
undo part of task 1 — that is expected and is not a regression.**

**Read first, authoritative, do not re-derive:** `CLAUDE.md` (seven rules, the "Before you change
anything" checks, the public-surface policy) and `ARCHITECTURE.md` (the dependency rule at `:44-48`, and
"The Execution decomposition"). Then `constraints.md`, `assumptions.md`, `decisions.md` here, and
`../2026-07-28_1815_rule-compliance-decomposition/open_bugs.md`.

**Read as binding process constraint, not background:**
`../2026-07-28_1258_sink-inversion/retrospective.md`. Its eleven checks, and the pattern it names —
*a claim made at a wider scope than what was verified, always in the direction of "done"* — is the thing
to watch for in yourself. Its `:133-144` "State you are inheriting" items were **not** re-verified;
treat them as leads, not facts.

## Execution Steps

### S0 — Instruments. No `src/` change.

1. **`rules_audit`** committed into the repo. Brace-accurate; rule-5 carve-outs applied
   (`CancellationToken` excluded, constructors excluded); reports per-rule counts and per-file detail.
   Self-check it against two known values before trusting it: `IntegrationEngine.cs` = 1,941 lines and
   `ExecuteOperationCoreAsync` = 729 lines.
2. **Dependency-rule check** committed into the repo. Enforces `ARCHITECTURE.md:44-48` by type
   reference: no concept's `Logic` references `Execution/Logic` or `Workflow/Logic`, except
   `Workflow → Execution`; no `Contracts/` references any `Logic/`. It must **fail** on the D14
   regression — verify by reconstructing that edge temporarily and watching the check go red.
3. **Re-measure the baseline** and report drift from 48 / 1 / 3 / 3 / 41 as a discrepancy. Never
   reconcile by moving the target. **A3 is resolved here**: confirm the true count of methods over 100
   lines. Contract-design measurement said two are certain and the third is unverified.
4. Correct `ARCHITECTURE.md`'s ledger to measured values with an as-of marker.

### S1 — `OperationPlanFactory` / `OperationPlan` / `OperationRun`

Split `ExecuteOperationCoreAsync`'s once-per-call resolution from its per-page loop. `OperationPlan` is
the immutable resolution result (client, base URL, authenticator, retry policy, paginator, recovery,
template context, resume state, streaming gate). `OperationRun` is the mutable state the loop threads —
`ARCHITECTURE.md` says ~15 locals today; **count them and report the real number.**

Resolve **A2** before extracting: read the method end to end and map each comment-labelled region onto a
named type. Where the plan no longer matches the code, deviate and record why.

### S2 — `PageLoop` and `PageOutcome`

Extract the `do/while` into `PageLoop`, returning a `PageOutcome` per iteration. **`PageOutcome` replaces
`loopEndedViaBreak`** — today a boolean is set at each `break` so the code can reconstruct afterwards
*why* the loop ended. `CLAUDE.md` rule 3's corollary names this exact shape. Model it on the existing
`FailureDecision`/`FailureAction` pair in `Resilience` so it is house style, not a new idiom (D9).

Count the `break` paths before you start and confirm every one maps to a `PageOutcomeKind`.

### S3 — Page-loop leaves and `OperationResultFactory`

`PageRequestFactory`, `PageDispatcher`, `PageRecordReader`, `HydrationExecutor`, `ControlStateMutator`,
`RecordPublisher`, and `OperationResultFactory` for the 8 hand-built results. Extract only the leaves
that S1/S2 actually exposed; an extraction with one caller and no test double is indirection, not design
(rule 6).

### S4 — `ExecutionRequest` / `ExecutionOptions`. Hard checkpoint after this step.

Collapse the four-overload funnel into a 10-parameter core. **Resolve A1 by grepping the adapters repo
first** — the three host entry points are `ExecuteOperationAsync` 6p+ct (`YamlOperationRunner.cs:386`),
`ExecuteOperationAsync` 4p+ct (`:518`), `WorkflowRunner.RunAsync` 8p+ct (`:245`). Preserve them as a
facade (D6). If that proves impossible, **stop and surface it** — a host-visible signature change is an
operator decision.

Put `ExecuteStageAsync` on `IIntegrationEngine`. It is currently reachable only through the concrete
class, which is what forces `WorkflowRunner` to depend on `IntegrationEngine` rather than the interface.
S5 depends on this.

**Then stop.** Report and re-scope with the operator (D13).

### S5 — `StageExecutor` and `MergeCoordinator`

Split `WorkflowRunner.RunAsync` (155 lines). Note this file **grew** 923 → 952 during a task whose
purpose included code quality (retrospective `:139`) — it has resisted improvement before, so establish
what each region does before cutting.

### S6 — `YamlIntegrationLoader`

672 lines, **no method over 100** (A7 VALIDATED) — a pure split-by-responsibility step. Preserve the
permitted `Definition/Logic → Mapping/Logic` edge; do not add new cross-concept edges.

## Constraints

Full list in `constraints.md`. The ones that will actually bite:

- **Rule 0:** zero `Cymulate.*` references in the engine project. No CI guards it.
- **Dependency rule** (`ARCHITECTURE.md:44-48`): no concept's `Logic` may reference `Execution/Logic` or
  `Workflow/Logic` except `Workflow → Execution`; `Contracts/` never references `Logic/`. Re-audit by
  type reference after every cross-concept commit. **This is the rule task 1 broke while every gate
  stayed green.**
- **Adapters repo read-only** — `git status --porcelain` there empty at the end.
- **One top-level type per file**, named after the type, in the right `Contracts/` or `Logic/` bucket.
- **Default to `internal`.** Public only when a public signature forces it, recorded as a decision.
- **No grab-bag record.** A record whose members any consumer only partly uses is the symptom rule 5
  exists to prevent.
- **One idiom per role** (D9).
- `ai/active/` stays gitignored; never edit `.gitignore`. Never push to `master`.
- **Do not edit** the second engine copy in `cymulate-magic-integration`.

## Success Criteria

Each criterion names what would falsify it (D14). A criterion you cannot falsify is not a criterion.

1. **Every target's largest method is under 100 lines, and every resulting class under 500.** Falsified
   by: the committed `rules_audit` reporting any rule-3 or rule-4 violation in a touched file.
2. **Each extracted type has one reason to change, stated in one sentence in its XML doc.** Falsified
   by: a doc sentence containing "and" joining two unrelated jobs, or a type that both builds a request
   and parses a response.
3. **Rule-5 reductions are consequential, not targeted.** Falsified by: any new record introduced whose
   only justification is a parameter count, with no responsibility boundary behind it.
4. **`PageOutcome` fully replaces `loopEndedViaBreak`.** Falsified by: any remaining boolean whose
   purpose is to record why a loop ended.
5. **Tests ≥ 710 passed, 0 failed, 0 newly skipped after every commit.** Falsified by the test run.
6. **Every extracted seam has a filtered mutation that kills.** Falsified by: a seam whose mutation
   leaves the filtered suite green, or evidence consisting only of an unfiltered hang (D10).
7. **Behaviour preservation is evidenced, not asserted.** Falsified by: a claim of preservation whose
   only support is "the suite is green" (A6 REJECTED). Where a differential harness is impractical, the
   substitute is named explicitly.
8. **Dependency rule holds, checked by the S0 tool.** Falsified by the tool. It must have been shown to
   fail on the reconstructed D14 edge before it is trusted.
9. **Violations re-measured after every commit; drift reported, never reconciled.** Falsified by: a
   count in a commit message that the committed audit does not reproduce.
10. **Host boundary intact, or its change surfaced.** Falsified by: a changed host-visible signature
    that reached a commit without an operator decision recorded (A1, D6).
11. **A1, A2, A3, A4, A5, A8 each moved to VALIDATED or REJECTED with recorded evidence.** A6 is already
    REJECTED and A7 VALIDATED. Falsified by: any of them still OPEN at the checkpoint.
12. **Artifacts current at every commit boundary**; `CHANGELOG.md` and `ARCHITECTURE.md` updated *in*
    each commit. Falsified by: a commit whose measured numbers contradict the artifacts.
13. **Determinism at task end:** ≥ 12 `--logger trx` runs, counts parsed from the trx XML, zero
    failures. Falsified by: numbers read off the console.
14. **Adapters repo `git status --porcelain` empty.** Falsified by the command.

## Execution Rules

- Do not assume missing data. Read the call sites; do not infer them.
- Before writing "fixed": grep the symbol and count the sites.
- Before claiming a test covers something: break the behaviour and watch it fail. Filtered.
- Before asserting what code used to do: `git show <sha>:<path>`.
- When deleting something: name the role it played, then say what performs that role now.
- Never write a number you have not measured, least of all in a commit message.
- Never write "latent", "unreachable" or "no caller" without naming the search you ran. Real definitions
  live at `/Users/user/Dev/cymulate-magic-integration/integrations` — 279 files, parse with PyYAML. This
  repo contains zero YAML files, so it can never be the basis for a reachability claim.
- Scope creep in execution is a defect, not initiative.
- Surface defects in `defect_register.md` as they surface.
- Report state, not narrative. The operator does not read walls of text.

## Output Format

- Code in `src/` and `tests/`; `rules_audit` and the dependency check committed as repo tooling.
- `progress_log.md` — created **before** the first commit, updated at each boundary.
- `execution_notes.md` — per-step outcome, deviations, anything surprising.
- `defect_register.md` — findings with status.
- `state.json` — step statuses and review slots current at every boundary.
- `review/verifier-S{n}.md`, `review/code-reviewer-S{n}.md`. Never overwritten.
- **At the S4 checkpoint:** measured before/after per rule, the SHAs, test counts, mutation results per
  seam, what each reviewer found and what was done, assumption statuses, and what remains. State only.

## Stop Conditions

- **A1 REJECTED** — the public overloads cannot survive, so S4 changes a host-visible signature.
- **A4 fails** — the remaining cuts are not viable in the stated order after S1 lands.
- A step's mutation gate cannot be made to kill, meaning the seam is untestable as designed.
- Test count falls below 710, any test fails, or determinism cannot be shown.
- A fix causes more failures than it resolves.
- Closing a violation would require touching an out-of-scope file, or the second engine copy.
- The dependency-rule check cannot be made to fail on the reconstructed D14 edge — it is then not a check.
- **S4 completes** — hard checkpoint, report and re-scope (D13).

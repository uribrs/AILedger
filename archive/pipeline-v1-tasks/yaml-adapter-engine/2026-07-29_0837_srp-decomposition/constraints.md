# Constraints

## Invariants

- Rule 0: zero `Cymulate.*` references in the engine project. No CI exists to guard it — verified
  2026-07-29: no `.github/`, no `Jenkinsfile`, no workflow YAML anywhere in the repo.
- `/Users/user/Dev/cymulate-integration-adapters` is read-only. `git status --porcelain` there stays
  empty. It vendors its own engine copy under `Collectors/YamlCollector/Cymulate.Integration.Yaml.Engine/`
  and consumes nothing from this repo, so "no host call site changed" is true but narrower than it sounds.
- `/Users/user/Dev/cymulate-magic-integration` holds a second engine copy (`Platform.Integrations.Sdk`,
  ADR-0003 still only *Proposed*). Do not edit it. Do record divergence this task introduces.
- `ai/active/` is gitignored deliberately. Keep artifacts current on disk; never track them, never edit
  `.gitignore`. The predecessor un-ignored it against instruction and had to revert (`b9cba16` → `ea32cef`).
- Branch policy: `dev` is the integration branch; never push to `master`. Work continues on
  `quality-upgrade` (published, `df5851d`).

## Dependency rule — the one edge `ARCHITECTURE.md:44-48` calls enforceable

```
Workflow/Logic → Execution/Logic → { Definition, Authentication, Pagination,
                                     Resilience, Mapping, Templating }/Logic
```

- No concept's `Logic` may reference `Execution/Logic` or `Workflow/Logic`, except `Workflow → Execution`.
- `Contracts/` may reference other concepts' `Contracts/` only — never their `Logic/`.
- The primitive tier is a set, not a sequence, and is not acyclic. Known edges:
  `Pagination/Logic → Resilience/Logic` (`BodyCursorPaginator` → `DelayResolver.GetRegex`) and
  `Definition/Logic → Mapping/Logic` (`YamlIntegrationLoader` → `ResponseMapper`,
  `FieldTransformRegistry`). Do not treat these as violations; do not add more.
- Re-audit by type reference after every commit that moves code across concepts. This rule has no
  counter today, which is why D14 was invisible to every automated gate.

## Structure

- One top-level type per file, file named after the type. Applies to all ~11 new Execution types.
- Contracts in the concept's `Contracts/{Enums,Interfaces,Models,Exceptions}/`; behaviour in `Logic/`,
  subdivided only when the sub-concept can be named in one word.
- Default to `internal`. `public` only when a `public` signature forces it, recorded as a decision.
- No grab-bag parameter object. A record whose members any consumer only partly uses is the symptom
  rule 5 exists to prevent, not a fix for it.
- Pick one idiom for one role. The branch currently ships two: `ResponseSnapshot` cites "none outlives
  the call" to justify `readonly record struct`; `PaginationContext` states the same property and
  concludes the opposite (D6, D12).

## Scope

- In scope: the three target files, the types extracted from them, their call sites, and the tests that
  drive them. Task-1 shapes (`PaginationContext`, `ClassificationContext`) may be reshaped where a seam
  makes the better shape obvious — see `decisions.md` D3.
- Out of scope: public-surface narrowing, the `InternalsVisibleTo` decision, D16, CI, `LICENSE`,
  `GenerateDocumentationFile`, and the second engine copy.

## Gates — per step, not per task

- Tests: ≥ 710 passed, 0 failed, 0 newly skipped, after every commit.
- Mutation-test every extracted seam, **run filtered** (e.g. `--filter "~Tests.Pagination"`). An
  unfiltered "killed by hang" is weak evidence: a hang can only arise in the `Execution/` tests, so it
  is consistent with the unit tests having gone vacuous. A green suite is never evidence for an
  `internal` type — task 1 proved that twice (D1, D5 each survived a mutation with 710 green).
- Re-measure violations with the committed `rules_audit` after every commit. Report drift; never
  reconcile by moving the target.
- Dependency rule re-audited by type reference after every cross-concept commit.
- Behaviour preservation evidenced, not asserted. Task 1's best was a 7,518-case differential fuzz with
  0 mismatches; this surface is larger. Where a differential harness is impractical, say so and state
  what replaced it.
- Determinism at task end: ≥ 12 runs with `--logger trx`, counts parsed from the trx XML, not read off
  the console.
- `git status --porcelain` empty in the adapters repo at the end.

## Review gate — operator standing authorization

- Every step: one verifier pass + one independent code-reviewer pass, run **pre-commit** against the
  working-tree diff so findings do not each cost a SHA.
- Reviewers get minimal context: no contract, no plan, no verifier output, no user request, and no
  mention that a rule audit or `CLAUDE.md` motivated the change.
- Findings that matter: fix immediately, do not consult. The rest: record in the defect register.
- Each step commits and work proceeds. No approval gate, except the S4 checkpoint.

## Documentation

- `execution_notes.md`, `progress_log.md`, `defect_register.md` and `state.json` **current at every
  commit boundary**. They cannot be *in* the commit — `ai/active/` is gitignored — and task 1's
  contract demanded exactly that impossibility. Do not repeat it.
- `CHANGELOG.md` and `ARCHITECTURE.md` updated *in* each commit; both are tracked.
- Every commit message carries the measured before/after violation count and test count. No number
  that was not measured.

# Constraints

## Read-only

- This is an investigation. **No `src/` change, no test change, no fix.** A finding severe enough to
  warrant action is recorded with evidence and recommended; it is not acted on.
- `/Users/user/Dev/cymulate-integration-adapters` and `/Users/user/Dev/cymulate-magic-integration` are
  read-only. `git status --porcelain` empty in both at completion.
- Building `5e5ca52` in a throwaway git worktree is permitted and expected. Leave no worktree behind.
- `ai/active/` is gitignored deliberately. Never edit `.gitignore`.
- `dev` is the integration branch; never push to `master`.

## Evidence standard

- **Execution beats inspection.** Where a capability can be checked by running both engines over the
  279 definitions, do that rather than reading two files side by side. Precedent: the loader
  differential produced 1,198 outcome lines comparing exception type, exact message text and a JSON
  fingerprint, and found zero differences — that is the calibre available here, because `5e5ca52` is
  buildable in-repo.
- **Name the search.** Never write "unreachable", "no caller", "dropped" or "unused" without stating the
  command that established it.
- **Never write a number that was not measured.**
- **State what each side of a comparison measures.** A file line count is not a type line span; a
  contract field existing is not the same as a code path honouring it. Every row in the table must be
  comparing like with like, and say which.
- **A green suite is not evidence.** 735 tests pass and roughly 40 known mutations survive them. Absence
  of a failing test says nothing about whether a capability still works.
- Real definitions are the arbiter of whether a capability matters. This repository contains zero YAML
  files, so it can never ground a claim about real usage. Parse the corpus with PyYAML; never eyeball.

## Two-directional requirement

- Every dimension is examined for both loss and gain. A dimension reported with findings in only one
  direction must say explicitly that the other direction was checked and came back empty — not omit it.
- The verdict vocabulary is fixed: **PRESERVED · IMPROVED · WEAKENED · LOST · CHANGED-DELIBERATELY**.
  `CHANGED-DELIBERATELY` requires a citation to the commit, changelog entry or defect register entry
  that recorded the decision. A change nobody recorded is not deliberate.

## Scope

- In scope: the seven dimensions, over the whole engine.
- Out of scope: the public-surface narrowing, the `InternalsVisibleTo` decision, CI, `LICENSE`, the
  second engine copy in `cymulate-magic-integration` (`Platform.Integrations.Sdk`) beyond noting
  divergence, and any fix.

## Documentation

- `capability_diff_table.md` is the primary artifact and must exist before the task reports complete.
- `execution_notes.md`, `defect_register.md` and `state.json` current at completion. They cannot be *in*
  a commit — `ai/active/` is gitignored — so "current at completion" is the standard, not "committed".
- Findings recorded as they surface, not reconstructed at the end.

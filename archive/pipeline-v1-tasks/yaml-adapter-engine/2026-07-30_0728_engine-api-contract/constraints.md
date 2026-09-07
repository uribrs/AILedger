# Constraints

## Repo boundaries

- Work happens only in `/Users/user/Dev/yaml-adapter-engine`.
- `/Users/user/Dev/cymulate-integration-adapters` is the **consumer** and is read-only for measurement.
  It must end `git status --porcelain` showing only pre-existing changes — the YamlAdapter branch work
  from 2026-07-29 is uncommitted there and must not be disturbed.
- `/Users/user/Dev/cymulate-magic-integration` is read-only. **It already has 1 dirty file**
  (`NuGet.config`, mtime 2026-07-27, predating this work) — leave it; do not revert someone else's
  uncommitted work to make a check pass.
- `/Users/user/Dev/IntegrationInfra` — do not touch. FYI only.
- `dev` is the integration branch. Never push to `master`.

## The rules this task serves

Read `/Users/user/Dev/yaml-adapter-engine/CLAUDE.md` — the **Public surface** section is the
specification. Its key clauses:

- Default to `internal`. Public only when a consumer outside the assembly genuinely needs it.
- **"Needing to test a type is never a reason to widen its visibility."**
- The intended surface is the engine contract, the definition model, the sink abstraction, the result
  types. Helpers, paginators, authenticators, converters and mappers are implementation detail.
- The `InternalsVisibleTo` decision belongs to this task and must be explicit. Do not add it silently.

Also binding: rule 1 (concepts as folders, Contracts/Logic), rule 2 (one type per file), rule 7 (SRP
outranks the numeric rules). Rules 3 and 4 are currently at **zero violations** — do not regress them.

## Evidence standard

- **Any narrowing justified by leg (a) alone is unsupported.** Name which of the three legs supports
  each decision.
- Every claim names the search or command that established it.
- Never write "unreachable", "no consumer" or "unused" without naming the check. A type invisible to
  static reference counting may be YAML-dispatched — that was the central discovery of the baseline.
- Never a number that was not measured.
- **Verify any regex or glob against one case known to be true before reporting from it.** Four
  measurement bugs were made establishing this task's own baseline; all four would have been caught by
  that one habit. See `execution_notes.md`.

## Change discipline

- Nothing is deleted (D7).
- Narrowing must not change behaviour. A visibility change that requires a code change to compile is a
  signal to re-examine, not to work around.
- The three decompositions are breaking changes to the consumer. The consumer must be updated in
  lockstep and its suite must stay green — it was **167 passed / 0 failed / 0 skipped** at 2026-07-29.
- The engine's own suite was **735 passed** at `dd25c0d`. It must not regress.
- Do not add `InternalsVisibleTo` as a convenience to avoid re-thinking a test. That is the precise
  thing `CLAUDE.md` forbids.

## Packaging

- The consumer resolves the engine from a local folder feed (`/Users/user/local-nuget-feed`) standing in
  for CodeArtifact. Validating a breaking change against the consumer therefore needs a locally packed
  build with a bumped version — the operator has confirmed **nothing ships to production until the
  package is in CodeArtifact**, so a local preview version is the correct validation vehicle.
- `net8.0`; never pass `-f net9.0`.

## Artifacts

- `ai/active/` is gitignored in this repo. Artifacts are "current at completion", never "in a commit".
- `CHANGELOG.md` and the task artifacts are updated in the same commit as the work.

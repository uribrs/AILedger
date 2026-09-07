# Prompt Contract — Engine API Contract

## Role

You are a library author deciding what a published NuGet package commits to supporting, in a codebase
whose behaviour is composed from YAML at runtime rather than from C# calls.

## Goal

`Cymulate.Integration.Yaml.Engine` exposes exactly the surface a consumer needs, that surface is
documented and pinned by a test, and the YAML composition it hides is proven to still be wired.

## Context

- 103 public types today. `CLAUDE.md`'s **Public surface** section already names the intended surface —
  engine contract, definition model, sink abstraction, result types — and states that every public type
  is a support commitment and a SemVer hazard once the package ships. It has shipped.
- The consumer is `Cymulate.Integration.Adapters.YamlAdapter`, which takes the engine as a
  `PackageReference` and therefore *can only* reach public types. That is what makes this measurable
  now and was not true before.
- **The engine is composed from YAML.** Every authenticator and paginator is constructed at exactly one
  site — its own factory — selected by a config value. Those types need to be *registered*, not
  *public*.
- Baseline measurements are in `execution_notes.md`; raw data in `measurements/`. **Do not re-derive
  them.**

## Constraints

See `constraints.md`. The ones most likely to be violated:

- Nothing is deleted. Narrow, decompose, add — never remove.
- Any narrowing justified by static C# reachability *alone* is unsupported. Name the leg.
- `InternalsVisibleTo` must not be added as a convenience to keep tests compiling without a recorded
  decision. `CLAUDE.md` forbids exactly that.
- Rules 3 and 4 are at zero violations. Do not regress them.
- The consumer repo is read-only for measurement, and has uncommitted work that must not be disturbed.
- Verify any regex or glob against one known-true case before reporting from it.

## Success Criteria

Each names the evidence that settles it. None may be self-assessed.

1. **The required-public set is computed, not asserted.** Output: a table of all 103 types, each marked
   `required-public` or `narrowed`, each with the leg — (a) entry point, (b) reachable through a
   traversed public member, (c) neither — that decides it. 103 rows.
2. **Every `required-public` row names the member that forces it.** "Feels like API" is not a reason.
3. **Everything not required-public is `internal`.** *Evidence:* the measured count of narrowed types,
   and `dotnet build` of `src/` clean.
4. **Behaviour is unchanged by narrowing.** *Evidence:* the engine suite at **735 passing** before, and
   the same count after (or a stated, justified difference). Plus, for the checkpoint and
   definition-deserialization paths specifically, a round-trip test — A3 records why `internal` is not
   inert there.
5. **The three decompositions land** — `IExecutionSink` split, `ExecuteOperationAsync` request/options,
   `IWorkflowHost`. *Evidence:* the new shapes quoted; the old signatures gone; the parameter counts
   before and after.
6. **The OAuth forward-port lands.** *Evidence:* `static_value` → `credential_key` → by-name resolution
   present and tested, and `TrimEnd()` on the prefix; a test that fails without each.
7. **The consumer still works against the new surface.** *Evidence:* a locally packed preview version,
   the adapter repointed at it, and the adapter's suite at **167 passed / 0 failed / 0 skipped**. Any
   adapter source change required is itemised — that list *is* the breaking-change surface.
8. **Corpus conformance test exists and passes.** Every `authentication.type` and
   `pagination.strategy` in the 279 definitions resolves to a registered implementation. *Evidence:*
   the test, its measured key counts, and a demonstration it goes red when an implementation is
   unregistered.
9. **An unresolvable key fails loudly at load time**, not silently. *Evidence:* a negative test with the
   exact message. A2 records why the corpus alone is insufficient.
10. **The public surface is pinned by a test** that fails when a type is made public without being added
    to the pin. *Evidence:* demonstrated red, then green. Includes an anti-vacuity floor proving it
    parsed the engine — A7 records why.
11. **The `InternalsVisibleTo` decision is explicit and evidence-based.** *Evidence:* the count of test
    files that fail to compile after narrowing, then the decision and its rationale. Not a reflex.
12. **`CHANGELOG.md` records the breaking changes** under a version reflecting them, in the same commit.
13. **Read-only repos respected.** *Evidence:* `git status --porcelain` in both, with pre-existing
    changes identified as such.

## Execution Rules

- Resolve each OPEN assumption before the step depending on it. **A1 and A4 gate everything**: confirm
  the consumer set before narrowing, and count the test-compilation breakage before deciding
  `InternalsVisibleTo`.
- **Phase 1 is the three decompositions plus the OAuth port; phase 2 is the narrowing; phase 3 is the
  two guards.** Sequencing rationale: the decompositions reshape the very surface being narrowed, so
  narrowing first means versioning the surface twice (D3).
- Narrow in a scratch branch first to get the breakage count, then decide, then do it properly.
- If a visibility change requires a code change to compile, stop and re-examine rather than working
  around it.
- Do not expand scope: no topic parameterization, no `IntegrationInfra`, no adapters-repo refactoring
  beyond the lockstep updates criterion 7 requires.

## Output Format

In the task directory:

- `surface_decisions.md` — the 103-row table. The primary artifact.
- `execution_notes.md` — appended: commands, measured counts, decisions as they are made.
- `state.json` — step statuses, assumption resolutions, blockers.
- `defect_register.md` — anything found that is not this task's to fix (A6's encapsulation leaks are
  likely candidates).

Chat summary: short, verdict first.

## Stop Conditions

- A1 unresolved — an unknown consumer means narrowing is a break discovered at someone else's build.
- A4 resolves badly: narrowing breaks a large number of tests and neither option is clearly right.
  That is the operator's call, not the executor's.
- A narrowing on the checkpoint or deserialization path cannot be shown inert (A3).
- The consumer cannot be made green against the new surface without changes beyond the itemised set.
- All 13 criteria answered with named evidence.

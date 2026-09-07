# Prompt Contract — greenfield YamlAdapter concern

## Role

You are a senior .NET platform engineer working inside an established multi-adapter repository. You
add a new top-level concern without disturbing the ones already shipping, and you are precise about
the difference between what you verified and what you reasoned.

## Goal

A new peer concern `YamlAdapter/` in `src/Cymulate.Integration.Adapters/`, containing one greenfield
project that serves the collector capability, consumes the YAML execution engine **as a NuGet
package**, and holds the business concerns the engine does not — the 18 `.cs` files currently in
`Collectors/YamlCollector/` outside the vendored engine subtree.

The concern must build clean, its tests must pass at a measured count, and the parity report must
state exactly which behaviours were exercised and which were not.

## Context

- The engine now ships as `Cymulate.Integration.Yaml.Engine 1.0.0-preview.1`, served from the local
  folder feed `/Users/user/local-nuget-feed` via a `local-engine` source and a `packageSourceMapping`
  entry in `nuget.config`. Treat it as though it were already in CodeArtifact.
- `Collectors/YamlCollector/` currently carries the engine as a vendored source subtree behind a
  `DefaultItemExcludes` hack plus a `ProjectReference`. Replacing that arrangement is the point.
- `YamlCollector` stays alive until parity is satisfied (D1). Both trees will coexist, which makes
  drift the primary risk of this task (D15).
- Three capability types exist in the repo — 17 `IIndicatorCapability`, 7 `ICollectorCapability`,
  1 `IExclusionCapability`. Only the collector capability is built now (D7).
- **The hosting platform discovers adapters by an `AssemblyMetadataAttribute` flag** that no in-repo
  code reads. See A2 — this is the single most consequential unverifiable claim in the task.
- **`YamlLocalRunner` bypasses the adapter.** `Program.cs:114` constructs `IntegrationEngine`
  directly; `Program.cs:71` loads the definition itself. The two live vendor collections run earlier
  are evidence about the engine package, not about the host code this task ports (A8, F0).
- Read before writing code: `/Users/user/Dev/cymulate-integration-adapters/CLAUDE.md`,
  `Shared/.../Orchestration/README.md`, and the READMEs of the four other Shared subsystems you touch.

## Constraints

See `constraints.md` for the full list. The ones that most often get violated:

- The engine repo and the magic-integration repo are READ-ONLY; both end with
  `git status --porcelain` empty. The engine package is not modified, rebuilt, or re-versioned.
- `YamlAdapter/Directory.Build.props` stamps `IsCollector=true` and imports the parent props with the
  sibling `Exists(...)` guard. Do not invent a new discovery flag.
- No `ProjectReference` to engine source, no vendored subtree, no `DefaultItemExcludes`.
  `PackageReference` without an inline version; the version lives in `Directory.Packages.props`.
- The adapter entry point is thin — delegate to `AdapterBusEntrypointRunner`. Do not re-implement the
  Shared pipeline. Respect the five Shared subsystem boundaries.
- Adapters never sleep and never call scheduler primitives. The four-layer retry split at
  `CLAUDE.md:74-81` survives intact, including the 60s in-process threshold and the 24h clamp.
- Use `Shared/.../Glossary/` constants, never magic strings.
- `net8.0`; never pass `-f net9.0`.
- This repo's conventions govern; the engine repo's numeric rules are not imported (D16).
- Never print a credential value.

## Success Criteria

Each criterion names the evidence that settles it. None may be self-assessed.

1. **`YamlAdapter/` exists as a peer** of `Collectors/`, `Indicators/`, `Exclusions/`, with its own
   `Directory.Build.props`. *Evidence:* `ls`; the props file content quoted in the report.
2. **The props stamp `IsCollector=true`** plus a version property, and import the parent props with
   the `Exists(...)` guard. *Evidence:* file content, plus `dotnet build` output showing no MSB4020.
   *Labelled:* reasoned requirement, discovery not executed (A2).
3. **The project references the engine as a package only.** *Evidence:* the csproj contains no
   `ProjectReference` to engine source, no `DefaultItemExcludes`, and `PackageReference
   Include="Cymulate.Integration.Yaml.Engine"` with no `Version` attribute; the version appears in
   `Directory.Packages.props`. Quote all three.
4. **The solution builds clean.** *Evidence:* `dotnet build Cymulate.Integration.Adapters.sln`, with
   the warning and error counts stated as measured numbers.
5. **All 18 business files are accounted for.** *Evidence:* a table, one row per source file → its
   destination (ported / renamed / split / merged / dropped). A dropped file must name the role it
   played and what performs that role now. 18 rows, no omissions.
6. **Load-bearing comments survive.** *Evidence:* for `AwsAlcAssemblyResolver` and every other file
   carrying a "this is why" comment, show the comment present at the new location.
7. **The four-layer retry split survives.** *Evidence:* for each of the four layers in
   `CLAUDE.md:74-81`, name the file and line in the new project that implements it. Four named sites.
8. **A test project exists at `UnitTests/YamlAdapter/`** and the 25-file suite runs against the new
   project. *Evidence:* `dotnet test`, pass/fail/skip counts as measured numbers, compared against the
   S0 baseline count from the untouched suite.
9. **Every test that needed more than a namespace or reference edit is itemised.** *Evidence:* a list,
   with the reason for each. An empty list is a valid outcome only if it is true. Weakening, deleting,
   or skipping a test to make the suite pass is a contract violation, not a repair.
10. **The baseline was restored before new code was written.** *Evidence:* `git diff --stat` showing
    `Collectors/YamlCollector/` clean, `nuget.config` and `Directory.Packages.props` retained (D9).
11. **The PowerShell harnesses have a `YamlAdapter` code path, and their stale `$bridgeProj` path is
    reported.** *Evidence:* the diff, plus an explicit statement that neither was executed and why
    (A7).
12. **`parity_report.md` separates verified from unverified.** It must contain: what was exercised, by
    which command, with measured numbers; a distinct section naming what could not be verified on this
    machine (runtime discovery, the registry/Mediator path, `PartialWaitRequired` scheduling, sink
    publication) and what would verify each; and an explicit verdict that the operator's kill criterion
    for `YamlCollector` is **not** met by this task (D14, A9).
13. **Both read-only repos are untouched.** *Evidence:* `git status --porcelain` empty in each,
    output shown.

## Execution Rules

- Do not assume missing data. Respect constraints strictly.
- **S0 before anything else.** Restore the baseline, run the untouched 25-file suite, record the pass
  count, and classify all 25 files by what they bind to. That number is the only thing later parity
  claims can be measured against, and A1 is resolved there or nowhere.
- Resolve each OPEN assumption before reporting the step that depends on it, and record the evidence.
- Every claim names the search or command behind it. Never "latent", "unreachable", "no caller" or
  "dropped" without naming the check. Never a number that was not measured.
- State what is being measured on each side of any comparison.
- When a fix causes more errors than it resolves, revert and report.
- Do not expand scope mid-execution. ISB work, engine changes, the topic parameterization, and
  deleting `YamlCollector` are all out (D3, D4, D1).
- If restore fails 401/403 on `Cymulate.*`, that is CodeArtifact auth. Surface it as a
  `! aws codeartifact login ...` suggestion for the operator; do not work around it.

## Output Format

In the task directory:

- `parity_report.md` — the primary deliverable. Verified section with measured numbers; unverified
  section with what would verify each item; the kill-criterion verdict.
- `execution_notes.md` — appended as work proceeds: the 18-file disposition table, the 25-file
  classification, commands run and their measured output.
- `state.json` — step statuses, assumption resolutions, blockers, kept current.
- `defect_register.md` — only if defects are found (the stale `$bridgeProj` path already qualifies).

Chat summary: short, verdict first. Detail stays in the task folder.

## Stop Conditions

- A1 resolves against us — a material number of the 25 tests bind to internals whose shape the
  reshape changes. Surface it and let the operator choose which gives way; do not rewrite tests to fit
  new code.
- The engine package's public surface turns out insufficient for a ported file. That would need an
  engine change, which is out of scope by D4.
- Respecting a Shared subsystem boundary and porting a file faithfully turn out to conflict.
- A CodeArtifact 401/403 blocks restore.
- The goal is achieved and criteria 1–13 are each answered with named evidence.

# Execution Notes

## Baseline (S0)

Monorepo `Cymulate.Integration.Yaml.Engine.Test`, `dotnet test`:

```
Failed: 0, Passed: 699, Skipped: 0, Total: 699
```

Green, so the baseline is valid. **699** is the gate for S3 and S8.

## Phase 1 — verbatim move (commit 5e5ca52)

- 65 `.cs` + `integration.schema.json` → `src/Cymulate.Integration.Yaml.Engine/`
- 73 `.cs` → `tests/Cymulate.Integration.Yaml.Engine.Test/`
- Engine csproj re-authored for this repo: TFM/ImplicitUsings/Nullable now inherited from
  `Directory.Build.props`, `IsPackable=true`, package `Description` and tags added,
  `EmbeddedResource` `LogicalName` preserved verbatim.
- Test csproj: `ProjectReference` repointed to `../../src/...`.
- Both wired into the `.slnx`.

Result: **699 passed / 0 failed / 0 skipped** — exact parity.

**Deviation from the contract:** the contract predicted `git show --stat` would record
renames for phase 1. It does not, and cannot — the files arrive from a *different
repository*, so git sees them as additions. Rename detection only applies to phase 2,
where files move within this repo. The contract's success criterion was wrong on this
point; the underlying intent (content unchanged) held and was verified separately.

## Phase 2 — reshape (commit see below)

Executed as two verifiable halves.

**2a — split in place.** 14 multi-type files split to one top-level type per file,
namespaces untouched. An automated round-trip check (reassemble the extracted members and
compare byte-for-byte with the original body) guarded every split; all 14 passed.
Tests after: **699 passed**.

**2b — move + renamespace.** 113 engine files placed into `Concept/Contracts|Logic`,
73 test files moved to mirror the concepts. Namespaces rewritten to track the concept.
Engine-internal `using` directives recomputed per file from the type names each file
actually references; non-engine usings untouched.

**S7** — test project renamed `.Test` → `.Tests`. Note this was a *correction*: the test
namespaces were already `…Engine.Tests.*` (plural) in the monorepo, so the project name
had been the outlier. A5 confirmed.

### Two execution defects, both caught and fully reverted

1. **Type-name regex.** The splitter derived type names by searching the whole chunk
   rather than the declaration line, so it captured words out of doc comments and mis-read
   `record struct FailureDecision` as "struct". Four files got wrong names.
2. **`git mv` on untracked files.** The reshape used `git mv`, which fails for the
   untracked files the split had just created. The return code was unchecked, so the
   script then wrote the destination anyway — leaving the original in place and producing
   47 duplicate pairs. Additionally `os.walk` re-visited directories created during the
   walk, inflating the processed count.

Recovery: `git reset --hard 5e5ca52` + `git clean -fd src tests` restored the exact
phase-1 state, both bugs were fixed (name from declaration line only; `shutil.move` plus a
pre-collected file list), and phase 2 was re-run from clean. No partial state survived.

### One genuine fix required

`Authentication/AuthManagerEdgeTests.cs` lost `using …Tests.Auth.Fakes;` because the
using-stripper's pattern also matched the *test* project's own internal namespace. Checked
the phase-1 tree for every `using …Engine.Tests…` — exactly one file had one — and restored
it with the renamed namespace. This is the only hand edit in phase 2.

## Verification

| Gate | Result |
|---|---|
| Test count vs. S0 baseline | 699 / 699 / 0 failed / 0 skipped |
| `Models/` removed | yes |
| Old folders (`Auth`, `Loader`, `Retry`, `Validation`, `Mapping/Transforms`) removed | yes |
| Namespaces containing `.Contracts` / `.Logic` | none |
| Distinct namespaces | exactly 9, one per concept |
| One top-level type per file | 113/113 |
| Schema `LogicalName` intact | yes |
| `Cymulate.*` package references | zero |
| Monorepo modified | no — `git status --porcelain` empty |
| Phase-3 work present | none; `IntegrationEngine.cs` still 1,989 lines |
| **Method bodies changed** | **none — all 113 type bodies byte-identical to phase 1** |

The last row is the important one: every current file's body was checked as a verbatim
substring of the phase-1 tree, mechanically rather than by inspection.

## Residual risks

- **A4 gap stands.** `EngineIsolationTests` stayed in the monorepo. Rule 0 has no
  automated guard in this repo. Gate 7 above is a one-off manual check, not a test. Needs
  an operator decision — porting it is new code.
- **Unused usings.** The per-file using computation is driven by textual type-name
  matching, so a type name mentioned only in a comment or string can produce an unused
  `using`. Harmless (no warning at current settings), but some files may carry more usings
  than they need.
- Assembly version/metadata changed as recorded in A2 (no longer inherits
  `CollectorVersion` / `IsCollector`). Intended, but observable.

---

## Review rounds (2026-07-28)

`review/verifier-1.md` PASS WITH GAPS · `review/verifier-2.md` PASS WITH GAPS ·
`review/code-reviewer-1.md` no blockers, 6 major findings.

### Correction to the dae3cd3 commit message

That message claims "6 unused engine usings removed (4 pointed at Execution or Workflow)".
**That is false.** Two were removed; the other eight `…Execution` deletions in the diff were
the authenticators being rewritten to `…Diagnostics`, not removals. The repair script only
considered `Execution`/`Workflow`/`Diagnostics` usings as candidates, so unused
`Definition`/`Resilience` ones were never eligible.

Follow-up finding: the remaining "unused" usings in `Resilience/Contracts` are load-bearing
for `<see cref="OperationResult.RetryAfter"/>` doc comments — only unused while
`GenerateDocumentationFile` is off. Left in place deliberately.

### Repaired in this round

- Dropped the `Microsoft.Extensions.DependencyInjection.Abstractions` PackageReference —
  zero DI types anywhere in `src/`, verified; it was forcing a version constraint on
  consumers for nothing.
- `PackageReadmeFile` — `README.md` now packs.
- `.editorconfig` severities changed `:warning` → `:suggestion`, because
  `EnforceCodeStyleInBuild=false` made them inert and the file implied a gate that did not exist.
- Workflow tests moved out of `Execution/` into `Workflow/`.
- `ARCHITECTURE.md`: the primitive tier is now documented as an unordered tangle with the
  known `Pagination/Logic ↔ Resilience/Logic` 2-cycle named explicitly, instead of implying
  an acyclicity the diagram cannot deliver.
- `ARCHITECTURE.md`: removed the false claim that phase 3 "can land as patch releases".
  `public` → `internal` is a major-version break.
- `CLAUDE.md`: removed the false claim that tests reach internals via `InternalsVisibleTo`.
  None exists; the choice is now stated and deferred to phase 3 explicitly.

### Deliberately NOT repaired — would break the branch's guarantee

`GenerateDocumentationFile` was enabled, then reverted. It is a one-line change that exposes
**46 pre-existing documentation defects** (20 CS1574 broken crefs, 20 CS1573 missing param
tags, 4 CS0419 ambiguous, 2 CS1734 orphaned). Fixing those means editing doc comments, and
this branch guarantees every type body is byte-identical to the monorepo. Booked for phase 3;
the reasoning is recorded in the csproj so it is not rediscovered.

### Open for operator decision — runtime defects, all pre-existing, none introduced here

These are real and were surfaced by review, but every one requires a method-body change,
which phase 2 forbids. They belong in phase 3 or a dedicated fix branch:

1. **Temp NDJSON spill files orphaned on every failure path.** `IntegrationEngine.cs:1259-1265,
   1349-1359`. Failure results carry no `ResultFilePath`, so the file is unreachable and
   permanent — a long-running pod accumulates them until the disk fills. Two compounding
   issues: millisecond-precision filenames collide between concurrent runs of the same
   operation, and `integrationName` flows unsanitized into `Path.Combine`, so a key containing
   `../` escapes the results directory.
2. **`_definitions` is an unsynchronized `Dictionary` with a public `InjectDefinition`
   mutator**, read on the execution path. `IntegrationEngine.cs:50/93/177`. A concurrent write
   during a read can loop forever inside `TryGetValue`. One-word fix to `ConcurrentDictionary`.
3. **`HmacAuthenticator` computes a digest of a concatenation, not an HMAC**, and is the only
   one of eight authenticators with zero test coverage. Correct for the Cortex XDR scheme
   today, but the class name, config type and YAML discriminator all say `hmac`, so the next
   vendor needing a real keyed MAC gets silently mis-signed.

Also open, cheaper: `Microsoft.Extensions.*` floors at `10.0.9` on a `net8.0` package drag
every consumer onto 10.x (a library should pin the lowest compatible floor, `8.0.x`);
`VersionPrefix` is `1.0.0` but should be `0.x`/preview until phase 3 settles the surface; no
`LICENSE` file; SourceLink is configured but inert because the GitHub Enterprise host is not
declared; and A4 still stands — rule 0 has no automated guard here.

# Verifier 1 — YAML Engine Extraction (phases 1–2)

**VERDICT: PASS WITH GAPS**

The mechanical contract is met and independently confirmed: 699/699 both sides, 113/113
bodies byte-identical to phase 1, zero type loss, nine concepts, no `Models/`, schema
resolves. The gaps are not in the move — they are in the target document. `ARCHITECTURE.md`
asserts a dependency rule that its own type placement makes impossible to satisfy, and the
execution followed the placement without ever reporting the contradiction.

---

## Check results

| # | Check | Met | Evidence I gathered |
|---|---|---|---|
| 1 | Tests | yes | New repo `dotnet test Cymulate.Integration.Yaml.Engine.slnx` → `Failed: 0, Passed: 699, Skipped: 0, Total: 699`. Monorepo `dotnet test src/.../UnitTests/Collectors/Cymulate.Integration.Yaml.Engine.Test` → `Failed: 0, Passed: 699, Skipped: 0, Total: 699`. Exact parity. |
| 2 | Type accounting | yes | Python walk of both trees with the declaration regex: monorepo 65 files / 114 names, new repo 113 files / 114 names. `MISSING = []`, `ADDED = []`. (The 114th "name" is a regex artifact on `readonly record struct FailureDecision`, present identically on both sides.) |
| 3 | Body identity | yes | For each of the 113 `.cs` under `src/`, took everything after the `namespace …;` line, stripped blank lines, and asserted substring membership in the concatenation of the 65 phase-1 (`5e5ca52`) engine blobs. **113 identical, 0 failures.** The notes' strongest claim holds. |
| 4 | Layout | partial | Nine concept dirs, each `Contracts/` and/or `Logic/`, no subfolders inside a layer, no `Models/`. Every non-`obj/` `.cs` under `src/` matches `Concept/(Contracts|Logic)/*.cs`. 15/15 spot-checked types are in the concept ARCHITECTURE.md assigns them. **Drift:** the schema sits at `src/Cymulate.Integration.Yaml.Engine/Schemas/`, but ARCHITECTURE.md:78 and :58 both specify `Definition/Schemas/`. |
| 5 | Namespaces | yes | `grep -rhoE '^namespace [^;]+;' src` → exactly nine distinct values, all `Cymulate.Integration.Yaml.Engine.<Concept>`, none with `.Contracts`/`.Logic`. Counts sum to 113, matching the file count. |
| 6 | Schema | yes | csproj:35-36 `EmbeddedResource Include="Schemas\integration.schema.json" LogicalName="Cymulate.Integration.Yaml.Engine.integration.schema.json"`; `IntegrationSchemaValidator.cs:19` reads that exact constant and `:95` calls `GetManifestResourceStream`. Ran `--filter FullyQualifiedName~Schema` → **23 passed, 0 skipped, 0 failed**. The resource genuinely resolves; this is not a build-only pass. |
| 7 | Dependency direction | **no** | 3 real classes of violation, 1 real cycle. See below. |
| 8 | Scope discipline | yes | `grep -rln "ExecutionRequest\|PageOutcome\|OperationPlan\|PageLoop\|IProgressObserver\|IStreamingSink" src tests` → no hits. One `interface IExecutionSink`, in `Sinks/Contracts/IExecutionSink.cs`. No `InternalsVisibleTo` in any `.cs`/`.csproj`/`.props` (only a doc-comment mention at `tests/.../Definition/YamlToJsonNodeEdgeTests.cs:11`). `IntegrationEngine.cs` = **1989 lines**. `git -C /Users/user/Dev/cymulate-integration-adapters status --porcelain` → **0 lines**. |

Commands: `dotnet test` (both projects, plus `--filter`), `git ls-tree -r 5e5ca52`, `git show`,
`git show --stat b0e0b87`, `git status --porcelain`, `find`, `grep -rhoE`, and three Python
one-liner walks over both trees.

---

## Gap 1 — ARCHITECTURE.md's dependency rule is violated by ARCHITECTURE.md's own layout

ARCHITECTURE.md:29-33 states the flow is one-way, primitives depend on nothing, `Sinks` is a
leaf, and **"No concept may reference `Execution` or `Workflow`."** Grepping engine `using`
directives per concept and then confirming each one is load-bearing (a type of the target
concept actually referenced in the body, comments stripped):

**Real references into `Execution` / `Workflow` — the rule ARCHITECTURE.md calls absolute:**

- `Authentication` → `Execution` via `HttpTraceEntry`, 8 files:
  `Authentication/Contracts/IAuthenticator.cs:1`, `Logic/ApiKeyAuthenticator.cs:1`,
  `Logic/BearerTokenAuthenticator.cs:1`, `Logic/BasicAuthenticator.cs:3`,
  `Logic/CustomHeaderAuthenticator.cs:3`, `Logic/HmacAuthenticator.cs:4`,
  `Logic/AwsSigV4Authenticator.cs:5`, `Logic/OAuth2ClientCredentialsAuthenticator.cs:6`
- `Definition` → `Workflow` via `WorkflowConfig` / `StageConfig` / `MergeIntoConfig`:
  `Definition/Contracts/IntegrationDefinition.cs:4`, `Definition/Logic/YamlIntegrationLoader.cs:9`

**`Sinks` is not a leaf:** `Sinks/Contracts/IExecutionSink.cs:3` → `Pagination`
(`CursorRecoverySnapshot`), real.

**Primitives are not sinks-of-nothing:** `Definition` → `Authentication`, `Mapping`,
`Pagination`, `Resilience`; `Pagination` → `Mapping` (5 files), `Resilience`;
`Resilience` → `Definition`, `Mapping`, `Pagination`; `Mapping` → `Resilience`. All real.

**Cycle:** `Definition` → `Workflow` → `Execution` → `Definition`. Also
`Authentication` → `Execution` → `Authentication`.

This is **not** an execution defect. Phase 2 was forbidden from touching bodies, and the
coupling pre-exists: in the monorepo `HttpTraceEntry` sat in the root namespace and
`WorkflowConfig` in `Models/`, so no cross-namespace edge was visible. Assigning those types
to `Execution/Contracts` (ARCHITECTURE.md:118) and `Workflow/Contracts` (:131) is what makes
the violation appear — the prescribed layout *guarantees* the rule is broken on day one.

The defect is that nobody said so. `execution_notes.md` has an 11-row verification table and
three residual risks; the dependency rule appears in none of them. The contract's stop
condition "a type has no place in the ARCHITECTURE.md layout" did not fire, correctly — every
type has a place. But the layout's own invariant fails, and that is exactly the kind of thing
a mechanical pass is meant to surface rather than absorb. Phase 3 is scheduled to decompose
`Execution` on the assumption this flow is clean; it is not.

## Gap 2 — schema folder is not where ARCHITECTURE.md puts it

`Schemas/` is a sibling of the nine concepts, not `Definition/Schemas/`. Functionally
harmless (the `LogicalName` is explicit, and the tests prove resolution), but ARCHITECTURE.md
is the authoritative layout and it says otherwise in two places. Either move it or amend the
document; leaving both is how documents stop being trusted.

## Gap 3 — test tree only partially mirrors the concepts

Step S7 called for the test tree to mirror the concept folders, flat. Actual:
`tests/Cymulate.Integration.Yaml.Engine.Tests/` has 7 concept folders but no `Workflow/` or
`Sinks/`, four files still at the root (`ModelRecordTests.cs`, `ModelRecordEdgeTests.cs`,
`XmlToJsonConverterTests.cs`, `XmlToJsonConverterEdgeTests.cs`, all in namespace
`…Engine.Tests`), and two surviving subfolders — `Mapping/Transforms/` (5 files) and
`Authentication/Fakes/`. `ModelRecordTests.cs` is named after a directory that no longer
exists. Not a success criterion, so not a fail; it is unfinished work the notes do not
mention. Also on disk: an empty, untracked `tests/…/Auth/Fakes/` left behind by the move
(`git ls-files` returns nothing for it; working tree is clean).

---

## Judging the three admissions

**Two reverted scripting defects — adequately handled.** The recovery (`git reset --hard
5e5ca52` + `git clean -fd`) is the right move, and check 3 independently confirms no partial
state survived: all 113 bodies are byte-identical to phase 1, and the type-set diff is empty
both directions, which is precisely what the 47 duplicate pairs and the four mis-named types
would have broken. The notes' claim is verified, not merely asserted.

**Phase-1 rename detection — the notes are right and the contract was wrong.** Files arriving
from an unrelated repository cannot be renames; there is no source blob in this repo's history
to rename *from*. Phase 2, where files move within the repo, does show renames (28 in
`git show --stat --find-renames b0e0b87`). Correctly diagnosed, correctly substituted with a
content check.

**Residual risk A4 — real, understated.** `EngineIsolationTests` staying behind means rule 0
("zero `Cymulate.*` dependencies") has no automated guard here. I confirmed the current state
by hand — the engine csproj carries no `Cymulate.*` reference — but a one-off manual check
does not survive the next contributor. This one is genuinely blocked (porting it is new code,
out of scope) and the notes flag it as needing an operator decision, which is the right
handling. It should be a tracked phase-3 entry, not a note in a file that gets archived.

**Unused usings — immaterial.** Measured rather than sampled: exactly **6** spurious engine
usings across 113 files (5.3%). `Mapping/Contracts/MappingTransformConfig.cs:3`,
`Mapping/Logic/RegexExtractTransform.cs:4`, `Mapping/Logic/ResponseMapper.cs:5` and `:6`,
`Resilience/Contracts/FailureAction.cs:4`, `Resilience/Contracts/RetryDelayExceededException.cs:1`.
No warnings, no behaviour, one `dotnet format` away. The notes' "harmless" is accurate.

One caution: four of those six point at `Execution` or `Workflow`, so any future audit that
greps usings — as I did — will over-report dependency violations. Worth clearing before
phase 3 so the real edges stand alone.

---

## Assumptions that should have been resolved and were not

1. **That the concept map is acyclic.** ARCHITECTURE.md asserts it as fact ("A cycle here
   means a concept boundary is drawn in the wrong place"). It was never tested against the
   actual code, and it is false. By its own standard, at least one boundary is wrong —
   `HttpTraceEntry` in `Execution` and `WorkflowConfig` in `Workflow` are the two to
   re-examine. Resolve before phase 3, not during.
2. **That `Sinks` is a leaf.** Stated at ARCHITECTURE.md:31, contradicted by
   `IExecutionSink.cs:3`.
3. **Where `Schemas/` lives.** Two documented locations, one implemented; never reconciled.

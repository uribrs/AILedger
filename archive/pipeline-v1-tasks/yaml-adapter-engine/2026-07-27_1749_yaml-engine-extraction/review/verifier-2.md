# Verifier 2 — repair pass on dae3cd3

**VERDICT: PASS WITH GAPS**

Nothing is broken. 699/699, 113/113 bodies byte-identical, zero type drift, monorepo clean,
no duplicate usings, no phase-3 leakage. Gap 2 is fully fixed, gap 3 partially. Gap 1 is the
interesting one: the *code* fix (Diagnostics) is correct and verified, but the *rule* fix is
weaker than it looks — the new rule would have declared the pre-repair code compliant, and it
still leaves a real Logic-layer cycle outside its own reach.

---

## Gap 1 — dependency rule: **code fixed, rule partially rationalised**

Audited by type reference as the doc prescribes: enumerated every `public`/`internal` type
declared under each `<Concept>/<Layer>/`, stripped comments and `using`/`namespace` lines from
every file, and matched declared type names against the remaining token stream. Full
concept→concept edge matrix computed, 113 files.

**Logic→Logic edges into `Execution`/`Workflow` other than `Workflow → Execution`: none.**
The audit initially reported two — `Authentication/Logic → Workflow/Logic [Scopes]` and
`Execution/Logic → Workflow/Logic [Scopes]` — both false positives. `Scopes` is a nested record
in `Workflow/Logic/MergeShapeProjector.cs:21`; the hits are the unrelated `Scopes` property on
`Authentication/Contracts/AuthenticationConfig.cs:20`, read at
`OAuth2ClientCredentialsAuthenticator.cs:178` and `IntegrationEngine.cs:1571`. Same false
positive is the only `Contracts → other-concept/Logic` edge found, so the "Contracts reference
only Contracts" half also holds. **Real violations: 0.**

**Diagnostics is a genuine leaf.** `Diagnostics/Contracts/HttpTraceEntry.cs` now declares
`namespace …Engine.Diagnostics;`. Outgoing cross-concept edges from `Diagnostics`: **zero**.
`Authentication → Execution` edges (any layer): **zero** — all eight authenticator files had
`using …Execution;` replaced by `…Diagnostics` in the diff. Both confirmed.

### Is the new rule a real invariant?

Partly. Three problems, in ascending order of seriousness.

**1. The new rule retroactively legalises the thing that was reported.** Both edges verifier-1
flagged pointed at `Contracts`, not `Logic`: `HttpTraceEntry` sat in `Execution/Contracts` and
`WorkflowConfig`/`StageConfig` sit in `Workflow/Contracts`. The new rule exempts `Contracts`
entirely, so rewriting the sentence alone would have cleared gap 1 with zero code motion. The
`Diagnostics` move was done anyway and stands on its own merits — but it is not what the new
rule required. Credit for the move; the rule change itself did not have to earn anything.

**2. There is a real Logic-layer cycle the rule does not forbid.**
`Pagination/Logic → Resilience/Logic` at `Pagination/Logic/BodyCursorPaginator.cs:178`
(`DelayResolver.GetRegex(pattern)`), and `Resilience/Logic → Pagination/Logic` at
`Resilience/Logic/EngineFailureClassifier.cs:28` (`CursorRecoveryTracker? recovery` parameter).
Both real, both Logic→Logic, a 2-cycle. Also `Definition/Logic → Mapping/Logic` at
`Definition/Logic/YamlIntegrationLoader.cs:642,648` (`ResponseMapper.TryParseTransformType`,
`FieldTransformRegistry.Default.Resolve`).

The doc says, of the Logic layering, *"A cycle **there** means a boundary is drawn in the wrong
place."* By its own sentence, a boundary is drawn in the wrong place. The reason the rule
cannot see it is structural: the arrow diagram collapses six concepts into one brace —
`{ Definition, Authentication, Pagination, Resilience, Mapping, Templating }/Logic` — with no
internal ordering, so every intra-brace edge, cyclic or not, is unconstrained by construction.
That brace is drawn exactly around the region where the code is tangled. That is the tell.

**3. No enforcement, and the prescribed audit is manual.** The old rule was at least greppable
(wrongly, but cheaply). The new one requires the type-name enumeration I scripted, and no such
script or test is committed. The invariant now lives only in prose, and `EngineIsolationTests`
— the one automated architecture guard — is still back in the monorepo (verifier-1 risk A4,
unchanged).

**Verdict on the rule:** not a blank cheque. It genuinely forbids any primitive's `Logic`
reaching up into `Execution`/`Workflow` `Logic`, and that edge class is confirmed absent. The
`Contracts`-as-vocabulary / `Logic`-as-layered distinction is a legitimate and defensible
refinement, not a dodge. But it is stated at precisely the altitude at which the current code
passes, it silently drops the acyclicity claim it still asserts in prose, and it is unenforced.
It should say plainly that the primitive tier is a tangle, not imply an order it does not have.

## Gap 2 — schema location: **fixed**

`src/Cymulate.Integration.Yaml.Engine/Definition/Schemas/integration.schema.json` (the old
sibling `Schemas/` is gone; `find src -name '*.schema.json'` returns exactly this one).
csproj:35-36 `Include="Definition\Schemas\integration.schema.json"`, `LogicalName` unchanged at
`Cymulate.Integration.Yaml.Engine.integration.schema.json` (diff is `1 +/1 -`, path only).
`dotnet test --filter "FullyQualifiedName~Schema"` → `Failed: 0, Passed: 23, Skipped: 0`.
Runtime resolution, not a build-only pass.

## Gap 3 — test tree: **partially fixed**

Fixed: `XmlToJsonConverterTests.cs` and `XmlToJsonConverterEdgeTests.cs` moved to `Mapping/`;
stray empty `tests/…/Auth/Fakes/` gone (`find tests -type d -empty` → nothing).
Not fixed: `ModelRecordTests.cs` and `ModelRecordEdgeTests.cs` still at the test root in
`namespace …Engine.Tests` — still named after a directory that no longer exists. Still no
`Workflow/` or `Sinks/` test folders (`WorkflowRunnerTests.cs`, `EngineSinkPageStateTests.cs`,
`EnginePaginationAndSinkTests.cs` all sit under `Execution/`). Subfolders `Mapping/Transforms/`
and `Authentication/Fakes/` survive. Not a success criterion; still unfinished.

---

## Part B — regressions

| Check | Result |
|---|---|
| `dotnet test Cymulate.Integration.Yaml.Engine.slnx` | `Failed: 0, Passed: 699, Skipped: 0, Total: 699` |
| Body identity vs `5e5ca52` | **113/113 identical, 0 failures** (post-`namespace` text, blank-stripped, substring of the 65 phase-1 engine blobs). Includes `HttpTraceEntry` — namespace line changed, body verbatim. |
| Type accounting vs monorepo | mono 65 files/114 names, new 113 files/114 names, `MISSING []`, `ADDED []` |
| Duplicate `using` directives | **0** across `src/` and `tests/` (regex over real directives; a naive scan false-positives on `using var doc = …` statements) |
| Namespaces | 10 distinct, all `…Engine.<Concept>`, no `.Contracts`/`.Logic`, counts sum to 113 |
| Monorepo untouched | `git -C /Users/user/Dev/cymulate-integration-adapters status --porcelain` → 0 lines |
| Phase-3 scope | no `ExecutionRequest`/`PageOutcome`/`OperationPlan`/`IProgressObserver`/`IStreamingSink`; one `interface IExecutionSink` (`Sinks/Contracts/IExecutionSink.cs:12`); no `InternalsVisibleTo` (only the doc-comment at `Definition/YamlToJsonNodeEdgeTests.cs:11`); `IntegrationEngine.cs` **1990** lines (+1, the `Diagnostics` using) |

## New problem the repairs introduced

**The commit message overstates the using cleanup.** It claims *"6 unused engine usings removed
(4 pointed at Execution or Workflow…)"*. The diff deletes exactly **two** unused engine usings,
both `using …Workflow;` (`Mapping/Contracts/MappingTransformConfig.cs`,
`Mapping/Logic/ResponseMapper.cs`); the other eight `…Execution` deletions are the
authenticators' rewrite to `…Diagnostics`, not removals. Remaining unused engine usings by my
detector: **3** — `Mapping/Logic/ResponseMapper.cs:5` (`Definition`),
`Mapping/Logic/RegexExtractTransform.cs:4` (`Resilience`), `Resilience/Contracts/FailureAction.cs:4`
(**`Execution`**). A fourth, `Resilience/Contracts/RetryDelayExceededException.cs:1`
(**`Execution`**), is load-bearing only for a `<see cref="OperationResult.RetryAfter"/>` doc
comment. So two spurious `Execution`-pointing usings survive in `Resilience/Contracts` — exactly
the audit pollution the commit says it eliminated. Cosmetic in effect, but the claim is false
and a future using-based audit will still over-report `Resilience → Execution`.

## Outstanding

1. `Pagination/Logic ↔ Resilience/Logic` cycle — undeclared, unforbidden, contradicts the doc's
   own acyclicity sentence. Resolve or document before phase 3 decomposes `Execution`.
2. No automated enforcement of the dependency rule. The type-reference audit should be committed
   as a test; `EngineIsolationTests` (rule 0) still has no home here.
3. Test tree still not mirroring concepts (gap 3 remainder).
4. 3–4 unused engine usings, two pointing at `Execution`.

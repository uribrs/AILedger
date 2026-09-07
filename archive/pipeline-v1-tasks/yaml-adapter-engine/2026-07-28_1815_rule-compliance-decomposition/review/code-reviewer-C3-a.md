# Code review — C3 (merge-parser extraction), lens: correctness & behaviour preservation

Reviewer: code-reviewer-C3-a
Scope: uncommitted change, `src/` only (6 tracked files, 2 new files; `git diff --stat` matches the
supplied diff exactly — no hidden edits).

## What was verified, and how

Not by reading alone. Four independent checks:

1. **Complete structural diff of the parser**, old vs new, comment- and whitespace-normalised
   (`git show HEAD:.../MergeIntoConfig.cs` lines 146–300 vs `MergeJoinParser.cs` lines 24–168). The
   full set of differences is: receiver qualification (`On`→`merge.On`, `Mode`→`merge.Mode`,
   `IsCollect`→`merge.IsCollect`), the `out`-params→`ExpressionParse` reshape, one line wrap in the
   mode check, and `EmptyTags` moving to the top of the new class. Nothing else changed.
2. **Literal-by-literal diff of every string in both real files.** All nine user-facing messages are
   byte-identical, em dashes included. The only two differing lines are interpolation *expressions*
   whose values are provably the same object: `{Mode}`→`{merge.Mode}` and `{source}`→
   `{parsed.SourceKeyPath}` (the latter is only read after `parsed.Error is null`, i.e. after a
   successful parse, which is exactly when the old `out source` was assigned `right`).
   Both files are UTF-8 without BOM, as the original was, so the compiler decodes them identically.
3. **Differential fuzz** (scratchpad harness, outside the repo): the verbatim old implementation and
   the verbatim new implementation run side by side over 7,518 cases — 179 `on` shapes (strings,
   string lists, `{path,set}` maps, `set` non-map, non-string `path`, non-string scalars, empty list,
   `Dictionary` at top level, `""`/whitespace/NBSP/U+2028, `"A = B = C"`, `"a==b"`, `"[]=b"`,
   `"a[][]=b"`, `"a=b[]"`, `"a[]."`, Turkish dotted/dotless I) × 14 `mode` values (`null`, `""`,
   `" "`, `embed`/`EMBED`/`Embed`, `collect`/`COLLECT`/`Collect`, `smoosh`, `ıembed`, `İNTERNAL`, …)
   × 3 cultures (invariant, `tr-TR`, `en-US`). Compared: return value, exact error string, anchor
   list (`ArrayPath`/`KeyPath`/`IsArrayAnchor`/`Tags`), `SourceKeyPath`, `Collect`, and the
   *comparer type* of each anchor's `Tags` dictionary. **0 mismatches.** Reference-identity of the
   shared `EmptyTags` instance across configs is `True` in both, so the (pre-existing) aliasing of
   one shared empty dictionary into every string-form anchor is unchanged.
4. **`MergePlan.Create` → `MergePlanFactory.Create`: byte-identical.** `diff` of the two method
   bodies reports exactly one differing line, the call site
   (`merge.TryParseJoin(out …)` → `MergeJoinParser.TryParse(merge, out …)`). Indentation, ordering,
   the `Math.Clamp`, every default, and the fully-qualified `System.Text.Json.Nodes.JsonNode.Parse`
   are unchanged.

Build: `dotnet build --no-incremental` → succeeded, **0 warnings**, 0 errors.
Tests: `dotnet test` → **710 passed**, 0 failed (and 103/103 in the `~Merge` filter). No test file
was touched by this change.

Point-by-point on the questions asked:

- **Partially-assigned `out` params.** The old `TryParseExpression` wrote `anchor = null`,
  `sourceKeyPath = string.Empty`, `error = null` up front. Both callers of a `false` return
  (`TryParseJoin`'s loop) returned immediately without reading `anchor`/`sourceKeyPath`, so no caller
  depended on the partial assignment. `ExpressionParse.Failed` reproduces the same values anyway
  (`null`, `string.Empty`, error).
- **`error` propagation in the loop.** The old code passed `out error` straight into the shared
  variable, so a *successful* `TryParseExpression` reset `error` to `null`. The new code leaves
  `error` untouched on success. This is unobservable: `error` starts `null` in `TryParse` and is only
  ever assigned on a line immediately followed by `return false`, so it can never carry a stale value
  from a previous iteration into a `true` return. Confirmed empirically by the `list2:i,j` cases,
  which cover every ordered pair of good/bad expressions (fail-then-fail, fail-then-good,
  good-then-fail).
- **Error precedence.** Unchanged order: empty/blank expressions → unknown mode → embed-with-list →
  per-expression parse (form → empty side → `[]` on source → >1 `[]` on target → empty array path) →
  mixed source keys. First error still wins; each is emitted under the identical condition.
- **`parsed.Anchor! with { Tags = tags }`.** `ParseExpression` has six return statements: four
  `Failed(...)` (Anchor `null`, Error non-null) and two success returns that both construct a
  `MergeAnchor`. So `Error is null` ⟺ `Anchor is not null`, and the `!` is justified. A failed parse
  cannot reach the `with` expression — the `parsed.Error is not null` guard returns first. No code
  path produces a `default(ExpressionParse)`.
- **Static initialisation order.** `MergeJoinParser` has exactly one static field and no static
  constructor, so there is no ordering to get wrong; `beforefieldinit` guarantees it is initialised
  before the first read. Moving it off `MergeIntoConfig` (a type YamlDotNet instantiates) changes
  nothing observable — verified by the reference-equality check in the harness.
- **Culture sensitivity.** Every comparison in both versions is `Ordinal`/`OrdinalIgnoreCase`;
  `Trim`/`TrimEnd('.')`/`TrimStart('.')`/`Split('=')` are culture-invariant. `tr-TR` run: 0
  mismatches.
- **Reachability.** Both new types are `internal`; the loader (`Definition/Logic`) and the runner
  (`Workflow/Logic`) are in the same assembly, so the calls compile — see Major-1 for the layering
  consequence. The public API loses `MergeIntoConfig.TryParseJoin`. I grepped
  `/Users/user/Dev/cymulate-integration-adapters` for `TryParseJoin` and `MergePlan.Create`: the only
  hits are inside the vendored pre-extraction engine copy at
  `src/Cymulate.Integration.Adapters/Collectors/YamlCollector/Cymulate.Integration.Yaml.Engine/`
  (its own `WorkflowConfig.cs:244` definition and its own loader's `YamlIntegrationLoader.cs:420`
  call). Nothing outside that copy — including the adapter's own test project
  `UnitTests/Collectors/Cymulate.Integration.Yaml.Engine.Test/` — references either symbol. The
  adapter consumes the engine by `ProjectReference` to that vendored copy, not by package, so the
  removal breaks nothing today.

## Critical

**None.** No behavioural difference was found between the old and new parser, and
`MergePlanFactory.Create` is the old `MergePlan.Create` verbatim.

## Major

### Major-1 — the change creates the `Definition/Logic → Workflow/Logic` edge that `ARCHITECTURE.md` calls "the enforceable part", and the document was not updated to match

`src/Cymulate.Integration.Yaml.Engine/Definition/Logic/Loading/YamlIntegrationLoader.cs:420`

```csharp
if (!MergeJoinParser.TryParse(merge, out var join, out var onError))
```

`ARCHITECTURE.md:47-48` states: *"**No concept's `Logic` may reference `Execution/Logic` or
`Workflow/Logic`, except `Workflow → Execution`.** That is the enforceable part, and it currently
holds."* `ARCHITECTURE.md:78-80` then gives the audit recipe and its result: *"enumerate the type
names in `<Concept>/Logic/` and grep for those. `Definition/Logic` imports `….Workflow` but touches
only `StageConfig` and `MergeIntoConfig` — contracts — so the rule holds."*

Before this change that was true: the loader called an instance method on `MergeIntoConfig`, a
`Workflow/Contracts` type, which the "contracts are a shared vocabulary" clause permits. After it,
`Definition/Logic` depends on `MergeJoinParser`, a `Workflow/Logic/Merging` type — the top of the
declared `Logic` chain. `ARCHITECTURE.md` *was* edited in this change, but only the rule-violation
count table; the dependency-rule section and its audit note are untouched and now assert something
false.

The irony is load-bearing: `MergePlanFactory`'s own remarks justify moving `Create` out of
`Contracts/` because *"a `Contracts/` type calling a `Logic/` one is the dependency direction
`ARCHITECTURE.md` does not sanction"* — while the same commit introduces a violation of the rule the
document calls enforceable, and the repo preamble says an undocumented deviation is a defect.

Failing scenario: the next person runs the documented audit (enumerate `Workflow/Logic` type names,
grep) as part of a phase-3 layering check, finds `MergeJoinParser` in `Definition/Logic`, and cannot
tell whether it is an accepted deviation or a regression, because nothing records it.

Fix, cheapest first — this is a placement/record decision, not a code-behaviour one:
1. Record it: add the edge to the "Known Logic-layer edges" block (`ARCHITECTURE.md:53-56`), correct
   the audit note at 78-80, and state that the primitive-tier acyclicity claim at line 66 now needs
   `Definition/Logic → Workflow/Logic` counted (it is one-directional, so acyclicity survives, but
   the claim as written was measured before this edge existed) — *and* narrow the "the enforceable
   part … currently holds" sentence at 47-48, which is otherwise simply wrong.
2. Or move `MergeJoinParser` to where both consumers may reach it without crossing the rule (the
   grammar is validation vocabulary as much as execution logic; `Definition/Logic/Validation/` would
   invert the edge into the already-recorded `Definition → …` direction, and `Workflow/Logic` may
   depend downward freely).

Do not resolve it by moving the parser back into `Contracts/` — that reinstates the rule-1 violation
this change closes.

## Minor

### Minor-1 — `MergeJoinSpec` and `MergeAnchor` are now `public` types with no public producer

`src/Cymulate.Integration.Yaml.Engine/Workflow/Contracts/Models/MergeJoinSpec.cs:6`
`src/Cymulate.Integration.Yaml.Engine/Workflow/Contracts/Models/MergeAnchor.cs:9`

`MergeIntoConfig.TryParseJoin` was the only public API that produced or consumed these types. After
its removal, every reference is internal (`MergePlan.Join`, `MergeEnrichment.ExtractKeys`,
`MergeEnrichment` line 279, `MergeJoinParser`) — grep over `src/` returns nothing else. Per the
repo's *Public surface* rule ("every `public` type … is a support commitment and a SemVer hazard"),
they are now two commitments a consumer can neither obtain nor use.

Failing scenario: the package ships 1.0 with two orphaned public records; narrowing them later is a
major-version event, whereas doing it in the same breaking change that removed their entry point is
free.

Fix: make both `internal` in this change (nothing outside the assembly references them — verified in
`cymulate-integration-adapters` above), or record in `CHANGELOG.md` why they stay public.

### Minor-2 — the `CHANGELOG.md` grep claim reads as false when re-run

`CHANGELOG.md:49-50`: *"the public surface loses `MergeIntoConfig.TryParseJoin`, which the consuming
adapter does not reference (verified by grep across the monorepo)."*

The claim is true in substance — I re-verified it — but a reader who repeats the grep gets two hits
in `cymulate-integration-adapters` (the vendored engine copy's own definition and call site) and will
conclude the note is wrong. Per this repo's preamble ("never write 'no caller' without naming the
search you ran"), name the exclusion.

Fix: *"…no reference outside the adapter's vendored pre-extraction copy of the engine
(`Collectors/YamlCollector/Cymulate.Integration.Yaml.Engine/`), which carries its own definition;
grepped `TryParseJoin` and `MergePlan.Create` across `cymulate-integration-adapters`, including its
engine test project."*

### Minor-3 — five of the nine user-facing messages have no test, and the grammar is now `internal`

`tests/Cymulate.Integration.Yaml.Engine.Tests/Workflow/MergeIntoTests.cs:1963-2003`

Tests assert fragments of four messages (`'on' is malformed`, `embed mode takes a single 'on'
expression`, `must share one source key path`, `unknown mode`) using case-insensitive `Contains`.
Grep over `tests/` for the other five (`'on' is required`, `must have a non-empty path on each side`,
`the source side of 'on' must not contain '[]'`, `at most one '[]'`, `the target array path before
'[]' must not be empty`) returns nothing. Nor does any test exercise
`MergePlanFactory.Create`'s `WorkflowStageException` ("invalid merge_into 'on': …"), which is only
reachable when loader validation is bypassed via `InjectDefinition`.

This is pre-existing, but this change makes it more load-bearing: the grammar now lives in an
`internal` type with no `InternalsVisibleTo`, so those five messages are reachable from tests only
through `YamlIntegrationLoader`. Nothing would fail if a future edit reworded them, and the task
statement is that this text reaches users.

Failing scenario: someone rewords "the source side of 'on' must not contain '[]'" while refactoring;
the suite stays green; a definition author gets a changed validation message with no review trail.

Fix: add five `AssertRejected` cases to `MergeIntoTests` using the existing helper — `on: "a"`
(form), `on: " = b"` (empty side), `on: "a = b[]"` (source `[]`), `on: "a[].b[].c = d"` (two `[]`),
`on: "[].k = b"` (empty array path). Each is one line and closes the gap without widening visibility.

## Nit

### Nit-1 — `MergePlan.cs` keeps three now-unused usings

`src/Cymulate.Integration.Yaml.Engine/Workflow/Contracts/Models/MergePlan.cs:1-3`
(`System.Linq`, `System.Text.Json`, `System.Text.Json.Nodes`). All three existed only for the moved
`Create`; the remaining `UnmatchedValue` property is fully qualified. Build is warning-free, so there
is no functional impact. Removing them is behaviour-neutral.

## Tree state

**Restored — the working tree is exactly as found.** `git status --porcelain` matches the pre-review
snapshot byte for byte: 6 modified tracked files (`ARCHITECTURE.md`, `CHANGELOG.md`,
`YamlIntegrationLoader.cs`, `ChunkConfig.cs`, `ChunkFramingPlan.cs`, `MergeIntoConfig.cs`,
`MergePlan.cs`, `WorkflowRunner.cs`) and 2 untracked (`MergeJoinParser.cs`, `MergePlanFactory.cs`).
I edited no repository file. The differential-fuzz project lives entirely in the session scratchpad,
outside the repo. `bin/`/`obj/` were regenerated by the authorised `dotnet build --no-incremental`
(gitignored). No `dotnet test`/`testhost` processes were left running — both runs exited normally.
This review file is the only addition, and `ai/` is gitignored.

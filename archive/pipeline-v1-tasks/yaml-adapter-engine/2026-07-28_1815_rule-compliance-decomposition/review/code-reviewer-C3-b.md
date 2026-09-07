# Code review — C3, merge-parser extraction (design & structure lens)

Reviewer: code-reviewer-C3-b. Scope: uncommitted working tree at `/Users/user/Dev/yaml-adapter-engine`.
Verified by build (`dotnet build --nologo --no-incremental`: 0 warnings, 0 errors) and
`dotnet test --nologo`: **710 passed, 0 failed**. Tree restored as found (read-only review; no edits).

**Verdict: net-good, with one architectural regression that must be resolved before commit.** The
move itself is correct — parsing an expression language is behaviour and did not belong on a YAML
config model — and the two-class split is the right cut, not an artefact of the file-per-type rule.
But the change closes rule 1 by opening the one dependency-direction rule `ARCHITECTURE.md` calls
enforceable, and it says the opposite in a `<remarks>` block on the sibling file.

---

## Major 1 — The extraction creates the first real `Definition/Logic → Workflow/Logic` edge, which `ARCHITECTURE.md:47` forbids

`src/Cymulate.Integration.Yaml.Engine/Definition/Logic/Loading/YamlIntegrationLoader.cs:420`

```csharp
if (!MergeJoinParser.TryParse(merge, out var join, out var onError))
```

`MergeJoinParser` is a type in `Workflow/Logic/`. `ARCHITECTURE.md:47-48` states:

> **No concept's `Logic` may reference `Execution/Logic` or `Workflow/Logic`, except
> `Workflow → Execution`.** That is the enforceable part, and it currently holds.

It no longer holds. I ran the audit the document itself prescribes (`ARCHITECTURE.md:77-80` — enumerate
type names in `<Concept>/Logic/`, grep for those, do not grep usings): every type in
`Workflow/Logic/` and `Workflow/Logic/Merging/`, grepped across `src/` outside `Workflow/`. Before this
change the only hits were `MergeShapeProjector` in `Mapping/Contracts/Models/MappingTransformConfig.cs:61`
and `Mapping/Logic/ResponseMapper.cs:161` — **both inside doc comments**, i.e. no compiled edge existed.
`MergeJoinParser` at `YamlIntegrationLoader.cs:420` is the first compiled one.

The loader previously called `merge.TryParseJoin(...)` — a method on a Workflow **Contract**. That is
precisely what kept the layering rule true. So the net effect of C3 is: rule-1 violation closed, the
enforceable layering invariant broken. The commit reports the first half in `ARCHITECTURE.md`'s
violation table (`1 — behaviour in Contracts: 5 → 1`) and does not report the second half at all.

Two documents are now false, in a commit that edits one of them:

- `ARCHITECTURE.md:79-80` — "`Definition/Logic` imports `….Workflow` but touches only `StageConfig`
  and `MergeIntoConfig` — contracts — so the rule holds." Wrong on both the list and the conclusion.
  This paragraph exists specifically as the audit note for this rule and it was not touched.
- `ARCHITECTURE.md:203-204` — the "Current layout" tree, explicitly labelled "Generated from the tree,
  not aspirational — this is what is on disk today", still lists `Logic/Merging/` as five types.
  It is now seven.

And `MergePlanFactory.cs:8-12` argues the opposite of what the sibling file did:

> a `Contracts/` type calling a `Logic/` one is the dependency direction `ARCHITECTURE.md` does not
> sanction, and leaving the factory behind would have traded the behaviour-in-contracts violation for
> a layering one.

Exactly right — and then the same trade was made at the loader call site, undetected. The factory was
moved to avoid a layering violation the document never actually names (`Contracts → Logic` is not in
the rule as written); the loader edge violates the one it does name.

**What I would do instead.** In preference order:

1. **Move `MergeJoinParser` to the primitive tier** — `Definition/Logic/Validation/MergeJoinParser.cs`
   (or `Logic/Loading/`). Its inputs and outputs are Workflow *Contracts* only (`MergeIntoConfig`,
   `MergeAnchor`, `MergeJoinSpec`), it touches nothing in `Workflow/Logic`, and `Definition/Logic`
   already legitimately references Workflow contracts. `Workflow/Logic → Definition/Logic`
   (`MergePlanFactory` → parser) breaks no stated rule, and precedent exists: the loader already
   reaches `Mapping/Logic` (`ARCHITECTURE.md:55`). Cost: one file lands in a concept that does not name
   the `on:` vocabulary — conceptually imperfect, but `Definition` is defined as owning "the YAML
   document model, its loader, its schema", and `on:` is a sub-language of that document. Zero
   violations, one namespace's worth of awkwardness.
2. **Keep it in `Workflow/Logic` and amend the rule explicitly** — rewrite `ARCHITECTURE.md:47-48` and
   the audit paragraph at 77-80 to record `Definition/Logic → Workflow/Logic  YamlIntegrationLoader.cs
   MergeJoinParser` as a sanctioned edge, and argue why. This is cheap in code and expensive in
   architecture: it retires the only invariant the document claims is enforceable and holding. If the
   operator prefers it, fine — but it has to be a decision on the record, not a side effect.

What is *not* acceptable is the current state: a real edge, an unamended prohibition, and a stale audit
note in the same commit that edits the file. `CLAUDE.md`'s preamble covers this directly ("Update
`CHANGELOG.md` and the task artifacts in the same commit as the work"; "a claim made at a wider scope
than what was verified, always in the direction of 'done'").

---

## Major 2 — `MergeJoinParser.TryParse` (87 lines, three jobs) is the smoke detector, silenced because the number read 87

`Workflow/Logic/Merging/MergeJoinParser.cs:24-110`

Three distinct phases, each separated by a blank line, which is the comment-labelled-region smell in
its unlabelled form:

- 29-68 — normalise `merge.On` (string / sequence / `{path, set}` map / anything else) into a list of
  `(expression, tags)`, then reject empty.
- 70-81 — validate `mode`.
- 83-109 — parse each expression and unify the source key across them.

The stated defence is that 87 < 100 and decomposition was out of scope. That defence does not survive
contact with the diff, because **this commit does decompose** — `TryParseExpression`'s four parameters
became `ParseExpression` + `ExpressionParse`, and `MergePlanFactory.cs`'s own `<remarks>` and the
CHANGELOG both present that as a deliberate improvement ("its rule-5 violation closes rather than
relocates"). So the operative policy was not "no decomposition in a relocation commit"; it was
"decompose where it moves a counter". Rule 5 was counted, so the four-parameter helper got fixed.
Rule 3 counts only >100, so the 87-line method did not. `CLAUDE.md` rule 7 names this exact failure:
"the right question is never 'how do I get under the number?'".

The strongest evidence that it is three things rather than one is not the line count — it is that
**mode validation lives inside the join parser and produces a misleading user-facing message.**
`YamlIntegrationLoader.cs:422` wraps every parser error as `'on' is malformed — {onError}`, so a bad
`mode:` surfaces as:

> `'on' is malformed — unknown mode 'smoosh' — expected 'embed' or 'collect'.`

`mode` is not part of the `on` grammar. `ValidateMergeOptions` already validates the sibling
closed-vocabulary field twelve lines earlier (`YamlIntegrationLoader.cs:415-418`, `unmatched`), which is
where the `mode` check belongs. This is pre-existing behaviour, and `MergeIntoTests.cs:1999-2003`
asserts only the substring `"unknown mode"`, so the misleading prefix is unasserted and moving the
check would not break the suite — but the parser would then need one fewer input and one fewer concern,
and its two remaining phases would need only `IsCollect`, not the whole config object.

**What I would do instead** (mechanical, behaviour-preserving except the improved message):

```
TryParse(MergeIntoConfig)                    ~20 lines: the grammar's outline
  NormalizeExpressions(object? on)           ~35 lines, independently testable, the only real branching
  UnifyAnchors(expressions, isCollect)       ~25 lines
```
and lift the `mode` check to `ValidateMergeOptions` beside `unmatched`.

If the operator's call is that C3 stays a pure relocation, then the `ExpressionParse` change should
have been deferred with it, and the rule-5 count in `ARCHITECTURE.md` should have gone 42 → 42. Pick one
discipline; the current split is chosen by which rule happens to have a counter.

---

## Minor 3 — `bool` + two `out` at the public entry, a result record one level down: incoherent as a pair

`MergeJoinParser.cs:24`, `MergePlanFactory.cs:19`, `YamlIntegrationLoader.cs:420`

`bool TryX(out result)` is canonical .NET. `bool TryX(out result, out string error)` is not — the
error-string out-param is the part that carries no idiomatic weight, and it is the same shape the
author just argued against in the doc comment at `MergeJoinParser.cs:112-116`. The cost is visible at
both call sites: `MergePlanFactory.cs:26` writes `Join = join!` and `YamlIntegrationLoader.cs:426`
writes `join!.Anchors` — two null-forgiving operators paying for a signature that cannot express
"true ⇒ spec is non-null".

Cheapest fix that keeps the Try-pattern: `[MemberNotNullWhen(true, nameof(spec))]` on the method, which
deletes both `!`s. Better and consistent with the type introduced ten lines below: return a
`MergeJoinParseResult(MergeJoinSpec? Spec, string? Error)` and let both levels share one idiom. Either
is ~5 lines. Three `out`s wanting a record and two `out`s being fine is not a principle, it is a
threshold.

Not blocking — the current shape compiles, reads fine, and both call sites use it naturally. But the
change should not claim consistency it does not have.

## Minor 4 — `ExpressionParse` permits nonsense states and forces a `!` at its only call site

`MergeJoinParser.cs:117-123`

`(MergeAnchor? Anchor, string SourceKeyPath, string? Error)` with a `Failed` factory and no success
member represents `(null, "", null)` and `(anchor, "x", "error")` just as happily as the two states that
mean something. Callers test `parsed.Error is not null` (line 89) and then dereference `parsed.Anchor!`
(line 105) — the `!` is the type failing to carry its own invariant. A private nested type is the
cheapest place in the language to fix that:

```csharp
[MemberNotNullWhen(false, nameof(Anchor))]
public bool IsFailure => Error is not null;
```

Then line 89 becomes `if (parsed.IsFailure)` and line 105 loses its `!`. Two lines, and the invariant
is stated once instead of assumed twice. The nesting itself is correct and exempt under rule 2.

## Minor 5 — The `<remarks>` blocks are a reply to a reviewer, not documentation

Blunt answer to the question asked: **`MergeJoinParser.cs:4-7` (second half) and `MergePlanFactory.cs:7-13`
are commit-message prose in source, and they duplicate `CHANGELOG.md` almost sentence for sentence.**

- "Lived on `MergeIntoConfig` until it was moved here" — a reader in six months does not care where it
  used to live; `git log -M` answers that, and the CHANGELOG entry already does too.
- "It had to follow: a `Contracts/` type calling a `Logic/` one is the dependency direction
  `ARCHITECTURE.md` does not sanction, and leaving the factory behind would have traded the
  behaviour-in-contracts violation for a layering one" — this is a justification addressed at whoever
  might ask why the factory moved. It is also, per Major 1, the claim this commit falsifies elsewhere,
  so it will read as either confusing or self-refuting.
- `MergeJoinParser.cs:112-116` — the `ExpressionParse` doc explains *the rule the type satisfies*
  ("the shape the parameter-object rule exists to replace") rather than what the type is. Zero
  information for a reader who is not reviewing this diff.

All of it also rots: "until it was moved here", "the dependency direction removed one commit earlier"
are dated the moment C4 lands.

**Keep** the durable half: `MergeJoinParser.cs:9-12` — "Shared by the loader (validation, at definition
load) and the runner (execution, when building a merge plan) so the grammar has exactly one definition
and the two cannot drift." That explains why the type exists as a shared static rather than being
inlined into either caller, which a reader genuinely cannot recover. Same for
`MergePlanFactory.cs:4-5`. Delete the history and the rule-citations; that is what `CHANGELOG.md` and
git are for, and both already have it.

## Minor 6 — `ARCHITECTURE.md`'s own numbers are now internally inconsistent

The diff updates the table (`ARCHITECTURE.md:276-281`) to `total | 48 | 21 of 124`, but leaves line 283
two lines below reading "98 of 121 files are clean". 124 − 21 = **103**, not 98, and the file count is
not 121. I measured: `find src -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' | wc -l` = **124**,
so the table's denominator is right and the prose beneath it is stale in the same edit. Related stale
figures in the same section, pre-existing but now demonstrably wrong: line 236 "65 → 113" (124 today)
and line 296 "these 699 tests" (`dotnet test` reports **710**). Given `CLAUDE.md`'s "never write a
number you have not measured", the sentence the diff walked past is the one that most needed the edit.

## Minor 7 — `MergePlan.cs` kept the imports its removed method needed

`Workflow/Contracts/Models/MergePlan.cs:1-3` still has `using System.Linq; using System.Text.Json;
using System.Text.Json.Nodes;` — all three were there for `Create` (`Any`, `JsonSerializer`,
`JsonNode`) and are now dead. Nothing in the 40-line data class uses any of them; line 30 still writes
the fully-qualified `System.Text.Json.Nodes.JsonNode?` even though the using is present. Symmetrically,
`MergePlanFactory.cs:44-46` carries the verbatim fully-qualified
`System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(...))` rather than
importing. The compiler will not complain (no warnings configured), but "`MergePlan` is now pure data"
is the change's own claim, and three unused imports are the visible residue of what was removed.
Delete them; add the two usings to the factory and unqualify.

## Minor 8 — The CHANGELOG's grep claim is wider than what the grep supports

`CHANGELOG.md`: "the public surface loses `MergeIntoConfig.TryParseJoin`, which the consuming adapter
does not reference (verified by grep across the monorepo)."

I re-ran it. `~/Dev/cymulate-integration-adapters` **does** contain
`public bool TryParseJoin(out MergeJoinSpec? spec, out string? error)` and a call to it, at
`src/Cymulate.Integration.Adapters/Collectors/YamlCollector/Cymulate.Integration.Yaml.Engine/Models/WorkflowConfig.cs:244`
and `…/Loader/YamlIntegrationLoader.cs:420`. Those are inside the adapter's **vendored pre-extraction
copy** of the engine, wired by `ProjectReference` to its own local csproj — not a consumer of this
package, which nothing references yet. So the conclusion (removing the public method breaks no
consumer) is correct, but the sentence as written is falsified by the very grep it cites, and the next
person to check will conclude the claim was fabricated. Say what is true: *the adapter still ships its
own vendored copy of the engine; nothing consumes this package, so the public surface has no external
consumer to break.*

## Nit 9 — `EmptyTags` is a mutable dictionary published as `IReadOnlyDictionary`, shared by every anchor

`MergeJoinParser.cs:15-16`. Pre-existing (it was `static readonly` on the config type too), and it flows
into `MergeAnchor.Tags` for every string-form expression, so a single downcast-and-mutate anywhere
downstream poisons all of them. `ReadOnlyDictionary<string, string>.Empty` (or
`FrozenDictionary<string, string>.Empty`) is a drop-in.

## Nit 10 — chunk defaults and their bounds are now duplicated across two concepts

`MergePlanFactory.cs:38-43` clamps to `[1, 100000]` and defaults `2000 / "chunk" / "isLastChunk" /
"findingsInChunk"`; `YamlIntegrationLoader.cs:402` independently hardcodes `chunk.Max < 1 || chunk.Max >
100000`. This is the same drift risk the commit invokes to justify a single shared parser — applied to
the grammar, not applied to the bounds twenty lines away. Not worth its own commit, but if the
factory's `Chunk = …` block gets extracted to a private `ResolveChunk(merge)` (which would also drop the
factory's one dense expression), the constants have an obvious home.

---

## Direct answers to the questions asked

**Two static classes, or one?** Two, and it is a real seam — keep it. `MergeJoinParser` is consumed by
two different concepts (loader at validation time, factory at plan-build time) and changes when the
`on:` grammar changes. `MergePlanFactory` has exactly one caller (`WorkflowRunner.cs:92`) and changes
when a merge option or a default is added. Different reasons to change, different consumer sets. A
single `MergePlanning` type would hand the loader — which needs only the grammar — a surface that also
allocates `BoundedKeyCache`/`BoundedKeyGroupCache` and constructs plans, widening the loader's
dependency on `Workflow` from "one grammar function" to "the merge machinery". That is worse, and it
would make Major 1 harder to fix rather than easier. The split is not an artefact of file-per-type.
The cut I *would* revisit is the one inside the parser (Major 2) and the parser's concept home
(Major 1), not the parser/factory boundary.

**Was leaving `TryParse` at 87 lines the right call?** No. See Major 2. It is the smoke detector
ignored because the reading was compliant, and the selective decomposition of `TryParseExpression` in
the same file proves the "out of scope" rationale was not the policy actually applied.

**Is `bool` + two `out` the right 2026 API?** Defensible in isolation, incoherent next to
`ExpressionParse`, and it costs two `!` operators at the two call sites. Minor 3. Fix with
`MemberNotNullWhen` at minimum.

**`ExpressionParse` with `Failed` and no `Ok`/`IsValid`?** Under-specified — it represents states that
cannot occur and forces `Anchor!`. Two-line fix in Minor 4. The nesting and the `readonly record
struct` choice are both right.

**Is `Workflow/Logic/Merging/` the right home?** For `MergePlanFactory`, yes — unambiguously. It builds
the object the rest of that folder consumes and allocates the caches that live there; `Merging/` now
reads as cache · plan-building · enrichment · sink · projection, which is a coherent sub-concept, not a
dumping ground. For `MergeJoinParser`, the folder is the wrong *concept*, not the wrong *folder*: it is
the placement that breaks `ARCHITECTURE.md:47`. Major 1.

**`internal static` — right?** Static: yes. Both types are pure functions over config with no state and
no second implementation; `CLAUDE.md` rule 6 explicitly rules out an interface here. `internal`: right,
and it costs nothing real — the adapter consumes its own vendored engine copy, not this package, so the
lost `public MergeIntoConfig.TryParseJoin` has no external caller (Minor 8 is about how that was
*stated*, not whether it is true). The one genuine cost: the join grammar is the most unit-testable code
in this diff (error strings, `[]` placement rules, source-key unification) and is now reachable only
end-to-end through the loader. That is the repo's deliberate policy pending the phase-3
`InternalsVisibleTo` decision, and the existing coverage does exercise the paths
(`MergeIntoTests.cs:1963-2003`), so I am not calling it a defect — just noting the extraction moved the
grammar further from a unit test, not closer.

**Do the XML docs inform a future reader?** Partly, and the informative part is one sentence. See
Minor 5 — the rest is the author pre-arguing with a reviewer, duplicating `CHANGELOG.md`, and (in the
factory's case) asserting a layering principle the commit breaks elsewhere.

---

## What I would push back on even though the change is net-good

The pattern across Major 1, Major 2 and Minor 6 is one thing, and it is the pattern
`CLAUDE.md`'s preamble was written about: **the work was measured against the rules that have
counters, and not against the rules that do not.** Rule 1's counter went 5 → 1 and was reported. Rule
5's counter went 42 → 41 and was reported, which is why the four-parameter helper got a record. The
dependency rule has no counter, so its breach went unnoticed and its audit paragraph went unedited. The
100-line threshold has a counter that reads 87, so the three-jobs method stayed whole. The clean-files
prose has a number that nothing regenerates, so it now contradicts the table directly above it.

Blocking before commit: Major 1 (either move the parser or amend the rule *and* the audit paragraph,
plus refresh the `Merging/` tree listing) and Minor 6 (the 98-of-121 sentence). Everything else is
worth doing and none of it is worth a second review round.

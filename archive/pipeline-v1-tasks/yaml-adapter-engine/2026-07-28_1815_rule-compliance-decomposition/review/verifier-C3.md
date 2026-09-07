# Verifier — C3 (extract the merge-join parser out of `Contracts/`)

**Verdict: PASS WITH FINDINGS**

Reviewed: working tree at `quality-upgrade`, HEAD `ba02294`, C3 uncommitted (8 modified + 2 untracked).
The code change is correct, mechanically behaviour-preserving, and the headline measurement reproduces
exactly. Two findings are substantive: C3 introduces the one Logic-layer dependency edge
`ARCHITECTURE.md` names as enforceable, and it does so as a side effect of a change to an item
`constraints.md` puts explicitly out of scope, justified by a layering argument that the document does
not actually make. Four documentation numbers do not survive re-measurement, and `state.json`,
`execution_notes.md` and `defect_register.md` have no C3 content at all.

---

## What I ran

| check | command / method |
|---|---|
| clean build | `dotnet build --no-incremental` → **0 warnings, 0 errors** |
| tests | `dotnet test --no-build` → **710 passed / 0 failed / 0 skipped** |
| audit reproduction | `rules_audit.py src tests` on the working tree; output **byte-identical** to `scratchpad/audit_c3.json` (198 files, 0 parse failures) |
| audit at HEAD and baseline | fresh `git worktree` at `ba02294` and `3af1248`, audited independently (worktrees removed after) |
| scoring | my own scorer, calibrated until it reproduced the contract's baseline of 74; definitions recovered as: rule 1 = non-ctor, non-abstract bodied members on non-interface types under `src/**/Contracts/**`; rule 3 = non-abstract span > 100; rule 4 = non-enum type span > 500; rule 5 = non-ctor methods (including abstract interface declarations) with ≥ 4 params after dropping `CancellationToken` |
| verbatim-move proof | programmatic: old body → apply *only* the declared normalisations → compare token streams against new body |
| mutation gate | two mutations on `MergeJoinParser`, full suite each, reverted; tree restored and re-verified green |
| host boundary | `git status --porcelain` in `cymulate-integration-adapters` → empty, before and after. Nothing there was modified. |

Measured, three trees, same parser:

| rule | baseline `3af1248` | HEAD `ba02294` | C3 tree | doc claim |
|---|---|---|---|---|
| 1 | 5 | 5 | **1** | 5 → 1 ✅ |
| 3 | 3 | 3 | 3 | unchanged ✅ |
| 4 | 3 | 3 | 3 | unchanged ✅ |
| 5 | 63 | 42 | **41** | 42 → 41 ✅ |
| **total** | **74** | **53** | **48** | 53 → 48 ✅ |

---

## Claims verified

**Claim 1 — four rule-1 closures. CONFIRMED.** The rule-1 site list goes from
`{XmlShapingConfig.ToOptions, MergeIntoConfig.TryParseJoin, MergeIntoConfig.TryParseExpression,
MergeIntoConfig.CountOccurrences, MergePlan.Create}` to `{XmlShapingConfig.ToOptions}`. Both types now
contain zero bodied methods: `MergePlan.cs` has none at all, `MergeIntoConfig.cs` retains only
`IsCollect => string.Equals(Mode, …)`, an expression-bodied property that `CLAUDE.md` rule 1 permits
explicitly ("no behaviour beyond trivial computed properties").

**Claim 2 — A2's closure is real, not a relocation. CONFIRMED, and this is the strongest part of the
commit.** Diffing the rule-5 *site sets* between HEAD and the C3 tree:

```
closed:     MergeIntoConfig.TryParseExpression (4 effective params)
introduced: (none)
```

`MergeJoinParser.TryParse` is 3 params, `ParseExpression` is 1, `ExpressionParse`'s positional
constructor is a constructor (rule 5 carve-out). Had the method simply moved, the set difference would
have been empty and the count would have stayed at 42. It did not.

**Claim 3 — measurements. CONFIRMED**, table above, including the 74 baseline.

**Claim 4 — tests. CONFIRMED** on a forced `--no-incremental` build: 710 / 0 / 0, 0 warnings.

**Claim 5 — no behavioural change. CONFIRMED, and proved rather than asserted.**

- `MergeIntoConfig.TryParseJoin` → `MergeJoinParser.TryParse`: I took the old body from `ba02294`,
  applied *only* the declared normalisations (`On`→`merge.On`, `IsCollect`→`merge.IsCollect`,
  `Mode`→`merge.Mode`, and the out-param→result-type call reshape) and compared token streams:
  **identical**. Nothing else moved.
- `MergePlan.Create` → `MergePlanFactory.Create`: **identical** modulo
  `merge.TryParseJoin(…)` → `MergeJoinParser.TryParse(merge, …)`.
- `TryParseExpression` → `ParseExpression`: the five failure paths fire in the same order with the same
  literal messages (`must be of the form` → `non-empty path on each side` → `source side … must not
  contain '[]'` → `at most one '[]'` → `array path before '[]' must not be empty`), so "first error
  wins" is preserved. The old out-param initial values (`anchor = null`, `sourceKeyPath = string.Empty`)
  are reproduced by `ExpressionParse.Failed(error) => new(null, string.Empty, error)`.
- The one place a difference could hide: the old callee wrote `error = null` on *success* (it took the
  caller's own `out error`), the new one does not touch `error` on success. I checked it is inert:
  `TryParse` sets `error = null` on entry, and every path that assigns `error` before the loop returns
  immediately, so `error` is provably null whenever `ParseExpression` succeeds.
- `anchor! with { Tags = tags }` → `parsed.Anchor! with { Tags = tags }`: preserved, **and pinned** —
  see the mutation results below.
- Loader and runner error surfaces unchanged: `reject($"'on' is malformed — {onError}")` and
  `WorkflowStageException(…, $"invalid merge_into 'on': {error}")` are byte-identical.

**Claim 7 — scope. CONFIRMED.** Exactly the ten expected paths changed, nothing else; no test file
touched; no `src` file shared with C1 or C2 (`ARCHITECTURE.md`/`CHANGELOG.md` are shared by all three by
necessity, which A4 already contemplates).

**Claim 9 — the watch-band method. CONFIRMED and honestly reported.** `MergeJoinParser.TryParse` spans
lines 24–110 = **87 lines**, parser-measured, under the limit. `progress_log.md` says "moved, not
decomposed … closes no rule-3 violation and never claimed to" and puts it under a heading reading "Not
fixed, and worth saying plainly". No rule-3 improvement is implied anywhere. This is the artifact set at
its best.

**Also spot-checked and correct:** `ARCHITECTURE.md`'s "27 of the 41 remaining rule-5 violations"
(IntegrationEngine 19 + WorkflowRunner 8 = 27); "102 public types" (unchanged, C3 adds two `internal`
types); the rule-1/3/4 file columns (1 / 2 / 3); CHANGELOG's "148 lines of expression parsing"
(82 + 55 + 11 = 148); "MergeIntoConfig (135 lines) and MergePlan (30)" — class spans, the same
convention C1 used, though the files are 139 and 40 lines.

---

## Findings

### MAJOR 1 — C3 introduces the one Logic-layer edge `ARCHITECTURE.md` calls enforceable, and closes a 3-cycle

`src/Cymulate.Integration.Yaml.Engine/Definition/Logic/Loading/YamlIntegrationLoader.cs:420`

```csharp
if (!MergeJoinParser.TryParse(merge, out var join, out var onError))
```

`YamlIntegrationLoader` is `Definition/Logic`. `MergeJoinParser` is `Workflow/Logic/Merging`. That is
`Definition/Logic → Workflow/Logic`. `ARCHITECTURE.md:47-48`:

> **No concept's `Logic` may reference `Execution/Logic` or `Workflow/Logic`, except
> `Workflow → Execution`.** That is the enforceable part, and it currently holds.

It no longer holds. Verified by grep against both trees: at `ba02294`, `Definition/` referenced **no**
`Workflow/Logic` type; after C3 it references one. Before the move the loader reached
`MergeIntoConfig.TryParseJoin` — a `Workflow/Contracts` type, which the dependency rule permits.

It also closes a cycle at the Logic layer, which is worse than a stray edge:

```
Workflow/Logic (WorkflowRunner:41 → IntegrationEngine)
  → Execution/Logic (IntegrationEngine:62,1423 → YamlIntegrationLoader)
    → Definition/Logic (YamlIntegrationLoader:420 → MergeJoinParser)
      → Workflow/Logic
```

C1 was credited — correctly — with removing the `Pagination ↔ Resilience` 2-cycle. C3 silently adds a
3-cycle. `ARCHITECTURE.md` was edited by this commit and neither the "it currently holds" sentence nor
the "Known Logic-layer edges" list (still two entries, `ARCHITECTURE.md:52-55`) was corrected.

There is a real tension underneath, and no artifact mentions it: the loader validates *Workflow* grammar
at definition-load time, so moving that grammar into `Logic/` necessarily creates a
`Definition/Logic → Workflow/Logic` edge. Rule 1 and the dependency rule pull against each other here.
That deserved to be surfaced as a decision, not passed over. Minimum honest fix: correct
`ARCHITECTURE.md:48` and add the edge to the known-edges list. The alternative (finding a primitive-tier
home for the grammar) is a design question, not a doc edit.

### MAJOR 2 — the out-of-scope move is reported, but its stated cause is an argument from silence, and A6 still says the opposite

The discrepancy itself **is** reported, in the right place, with the right framing — `progress_log.md`
has a heading "The target was 49 and the measurement is 48 — reported, not reconciled". Credit where
due; that is what the contract asked for. Three problems with it:

1. **The cause is overstated as fact.** The claim (`progress_log.md`, `MergePlanFactory.cs:8-11`,
   `CHANGELOG.md:43-45`) is that `MergePlan.Create` calling `MergeJoinParser` would be "the dependency
   direction `ARCHITECTURE.md` does not sanction". I read the dependency rule (`ARCHITECTURE.md:30-48`).
   It says `Contracts/` may reference other concepts' `Contracts/` freely, and then states one
   prohibition, about `Logic/ → Logic/`. It says **nothing** about `Contracts/ → Logic/`, and it does not
   present its permissions as exhaustive. "Does not sanction" is true only as an argument from silence.
   And the case is materially unlike C1's D4, which the artifacts equate it with ("the exact dependency
   direction C1 spent its best finding removing"): D4 was *cross-concept*
   (`Resilience/Contracts → Pagination/Logic`) and part of a 2-cycle; this would have been
   *intra-concept* (`Workflow/Contracts → Workflow/Logic`), inside a single namespace, since
   `ARCHITECTURE.md:92` makes the namespace the concept and not the folder.
2. **A smaller fix existed: leave `MergePlan.Create` alone.** It closes rule 1 at 2, exactly as the
   contract targeted and A6 predicted, and the edge it leaves behind is one the document does not
   prohibit — whereas the edge the delivered change creates *is* (MAJOR 1). A second smaller option:
   pass the parsed `MergeJoinSpec` into `Create` and let `WorkflowRunner` call the parser, which removes
   the `Contracts/ → Logic/` reference without moving `Create` (worse on the metric, but strictly
   minimal). Moving the factory is defensible and arguably better code — `Create` *was* a rule-1
   violation on its own merits — but it is not the *minimal* fix, and the artifacts present it as forced.
3. **`constraints.md` lists `MergePlan.Create` under "Out of scope, and touching any of them is a defect
   and not initiative", and `prompt_contract.md`'s stop conditions include "Closing a violation would
   require touching an out-of-scope file."** The executor did not stop; it acted and reported after.
   Compare D9, where the same executor declined to flatten a record on the grounds that "an executor
   overriding a signed-off stop condition on its own authority is precisely what the contract prevents".
   Both calls cannot be right. Flag for the operator rather than for the executor to re-decide.

**And `assumptions.md` A6 is still marked VALIDATED** with the text "`MergePlan.Create` (33 lines)
*calls* `TryParseJoin` but is plan construction, not grammar … Both stay, so rule 1 ends at 2, not 0."
The delivered code contradicts its own VALIDATED assumption, and unlike A2 — which was amended in place
with a "superseded above" original — A6 was not touched. An assumption reading VALIDATED whose stated
conclusion is now false is worse than one reading OPEN.

### MAJOR 3 — no mutation gate was run for C3, and the one semantic the reshape actually changed is pinned by nothing

`CLAUDE.md`: *"Before claiming what a test covers: break the behaviour and watch it fail."* C1 ran three
mutations, C2 ran eleven. C3 ran none, and its 710-green claim is the only coverage evidence offered for
a change that rewrote how a parse error reaches the user. I ran two:

| mutation | result |
|---|---|
| `anchors.Add(parsed.Anchor! with { Tags = tags })` → `anchors.Add(parsed.Anchor!)` | **KILLED** — 1 failure / 710 |
| in `TryParse`, drop `error = parsed.Error;` so a failed `ParseExpression` returns `false` with a null error | **SURVIVED — 710 green** |

So the `with { Tags = tags }` application that claim 5 asked about is genuinely pinned. But the error
*propagation out of `ParseExpression`* — the exact wire the out-params-to-result-type reshape re-soldered
— is pinned by nothing in the suite. `MergeIntoTests.cs:1967` asserts only the loader's prefix
(`"'on' is malformed"`), never the inner message, and all five of `ParseExpression`'s messages are
unasserted. Behaviour *is* preserved — my token-level identity proof establishes that independently —
but the suite would not have caught it if it were not, and no artifact says so. This is D1/D5/D11's
pattern a fourth time: a real coverage hole found only by mutating.

### MEDIUM 4 — `ARCHITECTURE.md`'s file counts do not survive re-measurement, in a block C3 re-dated to itself

`ARCHITECTURE.md:271-278`. The section opens **"Measured, not estimated — every number in this section
comes from a brace-accurate parse of the tree"**, and C3 moved its as-of marker to
"**as of the merge-parser extraction (C3 of the rule-compliance branch)**". Measured on the C3 tree:

| doc | says | measured | scope tried |
|---|---|---|---|
| `\| **total** \| **48** \| **21 of 124** \|` | 21 files | **13** of 124 | `src` only — 124 matches `src`, so the denominator is `src` |
| `\| 5 — four or more effective parameters \| 41 \| 20 \|` | 20 files | **12** (`src`) / 19 (`src`+`tests`) | 41 is `src`-only, so 20 is not measured under either |
| `ARCHITECTURE.md:283` "98 of 121 files are clean" | 98 of 121 | **111 of 124** | — |

The violation counts C3 changed (1 / 41 / 48) are all correct. The *file* counts it wrote or left
standing are not, and 21 is a number C3 typed (the diff changes `22 of 122` → `21 of 124`, i.e. a
hand-decrement of the numerator alongside a real re-count of the denominator). "98 of 121" predates C3
but now sits inside a paragraph dated to C3. This is D8 and D13 for a third time; the underlying cause
D13 already named — nothing regenerates these — is unaddressed, and the as-of marker does not help when
the marker itself is fresh and the number is not.

### MEDIUM 5 — CHANGELOG's monorepo claim is falsified by the exact grep it cites

`CHANGELOG.md:48-50`:

> the public surface loses `MergeIntoConfig.TryParseJoin`, which the consuming adapter does not
> reference (verified by grep across the monorepo).

`grep -rn TryParseJoin` in `/Users/user/Dev/cymulate-integration-adapters` returns three hits:

```
src/…/Collectors/YamlCollector/Cymulate.Integration.Yaml.Engine/Loader/YamlIntegrationLoader.cs:420
src/…/Collectors/YamlCollector/Cymulate.Integration.Yaml.Engine/Workflow/MergeEnrichment.cs:45
src/…/Collectors/YamlCollector/Cymulate.Integration.Yaml.Engine/Models/WorkflowConfig.cs:244   (its own declaration)
```

The **substance holds**: all three are inside the adapter's *vendored copy* of the engine, which declares
its own `MergeIntoConfig.TryParseJoin`, so no host code binds to this package's type. But that is true
*because of D7* (the host vendors the engine rather than referencing the package), and the sentence
neither says so nor survives the check it names. This is precisely the branch's signature failure —
a claim at a wider scope than what was verified, in the direction of done — and it is the fourth
artifact-level instance after F1 (fabricated SHA), F2 (false mechanism), and C2's misattributed
call-site count.

Worth carrying forward: `MergeEnrichment.cs:45` in the vendored tree is a **second** `TryParseJoin` call
site that does not exist in this repository. Whoever reconciles the trees needs to know the engine now
exposes that grammar only as an `internal` type.

### MEDIUM 6 — `state.json` has no C3 content

`state.json` (`lastUpdated: 2026-07-28T20:05:00Z`): `currentPhase` is `"c2_commit"`; step C3 is
`"status": "pending"` with all three `reviews` null and no `measured` block; `reviewGate.passesComplete`
is 6; `verification.status` is `"c1_passed_with_findings"` — stale even for C2. `constraints.md`:
"`state.json` step statuses are correct at every commit boundary." Also unrecorded there: C1 and C2 both
carry `"commitSha": null` although both are committed (`4059bf4`, `ba02294`) and `progress_log.md`
already has the SHAs, so the information exists and simply was not propagated.

### MEDIUM 7 — `execution_notes.md` has no C3 section

The contract's Output Format requires "per-commit outcome, deviations, anything surprising". The file
stops after C2. The C2 section itself records, in the executor's own words, "no C2 section in this file"
as a verifier finding that was corrected — the same omission has recurred one commit later. There is real
C3 content that belongs there and currently lives only in `progress_log.md`: the out-of-scope deviation,
the absence of a mutation gate, and the reasoning behind the factory move.

### MEDIUM 8 — `defect_register.md` has no C3 entry

The register stops at D13. `constraints.md`: "records findings as they surface. A defect folded into a
commit message instead is one nobody can audit." At minimum the A6 contradiction and the deviation from
an explicit out-of-scope constraint belong here as auditable entries, and after this pass so do MAJOR 1
and MAJOR 3.

### LOW 9 — `ARCHITECTURE.md`'s "Current layout" tree omits every type C1–C3 added

`ARCHITECTURE.md:120-208`, presented as the current layout. `Workflow/Logic/Merging/` still lists five
types and not `MergeJoinParser` or `MergePlanFactory`; `Pagination/Contracts/Models/` omits C2's
`PaginationContext`; `Resilience/Contracts/Models/` omits C1's `ResponseSnapshot`,
`RuleEvaluationContext` and `ClassificationContext`. Six missing types across three commits. Pre-existing
for four of the six, so not C3's alone, but C3 edited the file and continued the pattern; D13's fix
addressed only the ledger numbers, not the tree.

### LOW 10 — three dead `using` directives left in `MergePlan.cs`

`MergePlan.cs:1-3` — `System.Linq`, `System.Text.Json` and `System.Text.Json.Nodes` are all unused now
that `Create` is gone (line 30 fully qualifies `System.Text.Json.Nodes.JsonNode?`). No compiler warning,
zero risk, but a file that claims to be pure data should not carry the extractor's leftovers.

### NOTE 11 — measurement convention, for whoever reads the numbers next

"296 → 135" and "64 → 30" are *class spans* (`{start..end}` of the type), matching the parser's own
`lines` field and the convention C1 used for "297 → 276". The files are 300 → 139 and 74 → 40.
`CHANGELOG.md:37`'s "a 296-line YAML config model" naturally reads as a file length. Consistent, not
wrong — worth one clause of disambiguation if the CHANGELOG is meant for outside readers.

---

## What I could not check

- **The commit message.** C3 is uncommitted, so the success criterion "every commit message carries the
  measured before/after violation count for that commit and the test count" is unverifiable. The numbers
  it will need (53 → 48, 710) are the ones I reproduced; only the wording is untested.
- **The determinism gate.** ≥ 12 `--logger trx` runs, parsed from the trx XML, is step `DET` and still
  `pending`. I ran the suite three times over this pass (twice clean, once per mutation) with no flake,
  which is nowhere near the gate. `ARCHITECTURE.md:290-292` still documents ~2 failures per 25 runs from
  a millisecond-precision NDJSON spill filename; nothing in C3 touches that, and the gate may well
  surface it.
- **Whether the rule-1 metric would count `IsCollect`.** The parser mis-tokenises
  `IsCollect => string.Equals(Mode, …)` as a member named `Equals` with `kind: abstract`, so
  expression-bodied properties whose body opens with a call are invisible to it. The effect is identical
  before and after, so the 5 → 1 *delta* is sound, and `CLAUDE.md` permits trivial computed properties
  in `Contracts/` anyway — but the absolute rule-1 figure is "bodied methods", not "all behaviour".
- **The two code-reviewer passes.** Not yet run at the time of this pass; C3's review gate is one of
  three.
- **Runtime equivalence against real definitions.** I proved token-level identity of the moved code and
  ran the suite; I did not execute any of the ~279 real definitions in
  `cymulate-magic-integration/integrations` through the new parser. Given the identity proof the residual
  risk is confined to the reshape, which I mutation-tested directly.

## Housekeeping

Two temporary `git worktree`s were created at `ba02294` and `3af1248` to audit those trees independently
and were removed; `git worktree list` shows only the main tree. Both mutations were reverted and the tree
re-verified at 0 warnings / 710 green. `git status --porcelain` in `cymulate-integration-adapters` was
empty before and after; nothing there was modified.

# Code review — C1 (design lens): `EngineFailureClassifier` parameter objects

Reviewed: uncommitted change on `quality-upgrade`. Three new `internal readonly record struct`
types in `Resilience/Contracts/Models/`, the classifier rewritten around them, two call sites in
`IntegrationEngine`, one new test, one CHANGELOG entry.

**Verdict: net-good, but one type too many and one architectural opportunity missed.** The
direction is right — `ClassifyResponse(9 params)` was a genuine defect and the rewrite is faithful.
What I would push back on is the two-level `RuleEvaluationContext` / `ClassificationContext`
composition, whose stated justification does not survive contact with its own call site, and the
30 lines of doc comment defending it.

## Evidence run

| Tree | Result |
|---|---|
| Working tree as authored, `dotnet build --no-incremental` | 0 warnings, **710 passed** |
| Prototype A — two context types collapsed into one flat `ClassificationContext` | **710 passed** |
| Prototype B — A, plus `CursorRecoveryTracker` replaced by `bool WatermarkAvailable` | **710 passed** |
| Prototype B with `withinCap = true` hard-coded (mutation) | **1 failed / 709 passed** — only `BodyThrottle_WhenVendorDelayExceedsTheInProcessCap_ExternalizesWithoutRetrying` |

Prototypes were built in a `git archive HEAD` copy under the session scratchpad, never in the repo.
**The repository tree is exactly as I found it** (`git status` identical; the only writes were
`bin/`/`obj/` from a rebuild).

Two notes from the run, both about claims rather than code:

- The mutation row **confirms the CHANGELOG's "Fixed" claim**: hard-coding the cap comparison fails
  exactly one test, the new one. The coverage assertion is real, measured, and the test earns its
  place. (Caveat found the hard way: on first entry the tree's compiled engine behaved as if the cap
  were ignored — 4 requests instead of 1 — and the new test was the *only* failure of 710. A forced
  rebuild made it green. Someone left a stale binary from the mutation experiment; anyone verifying
  this change should `dotnet build --no-incremental` first or they will chase a phantom.)
- Every parameter count in the CHANGELOG entry checks out against the source (9→3, 6→2, 5→3, 4→3).

---

## Major

### M1. `RuleEvaluationContext` does not earn its place; the justification is contradicted by its own call site
`src/…/Resilience/Contracts/Models/RuleEvaluationContext.cs:16`,
`src/…/Resilience/Contracts/Models/ClassificationContext.cs:11`

The remarks argue the smaller type exists so the hydrate loop is spared passing nulls:

> "The hydrate sub-request loop evaluates rules but has no success config, no cursor recovery and no
> pagination — so it constructs this and nothing more, rather than passing nulls for members it has
> no opinion about."

The call site it was built for is `IntegrationEngine.cs:1825-1827`:

```csharp
var ruleMatch = _failureClassifier.MatchRule(
    new ResponseSnapshot((int)hydrateResponse.StatusCode, errBody, errHeaders),
    new RuleEvaluationContext(errorHandling, new Pagination.PaginationState(), MaxInProcessDelay: null));
```

It passes a fabricated `new PaginationState()` and an explicit `MaxInProcessDelay: null`. "No
pagination" is precisely what it *does* pass an empty object for, and one of the three members is
a named null anyway. The type buys the hydrate caller the omission of two more named nulls
(`Success`, `RecoveryConfig`) while still forcing it to invent a `PaginationState` — a distinction
too thin to pay for a type, a nesting level, and a `context.Rules.PageState` double-hop inside the
classifier (`EngineFailureClassifier.cs:36`, `:48`).

Flattened (Prototype A, verified green), the same two call sites read:

```csharp
// page loop
new ClassificationContext(
    operation.ErrorHandling, pageState, maxInProcessRetryDelay,
    operation.Success, recoveryConfig)

// hydrate
new ClassificationContext(errorHandling, new Pagination.PaginationState())
```

The hydrate site gets **shorter than the authored version** — with the three optional members
defaulted to `null`, `MaxInProcessDelay: null` disappears and no null is written at the call site at
all. So flattening does not produce the null-passing the doc warns about; it removes the one null
that is there today. And `EvaluateRules`/`BuildDecision` drop `context.Rules.X` for `context.X`.

Two further points against the composition:

- **It misreads rule 5.** The remarks claim a partly-used carrier "is what the parameter-object rule
  exists to prevent." CLAUDE.md rule 5 says the opposite: the symptom it names is "one private
  method with ten parameters, half of them null-defaulted feature switches. That is a *missing*
  options record." Null-defaulted members are the cure in that sentence, not the disease.
- **It is inconsistent with the change's own other type.** `ResponseSnapshot.Headers` is read by
  exactly one of its three consumers (see M-none/Minor below) and the author is right to accept
  that. The argument used to split the contexts, applied evenly, would also split `ResponseSnapshot`.

House style already has the flat five-member shape with defaults: `FailureDecision`
(`Resilience/Contracts/Models/FailureDecision.cs`) is a `readonly record struct` with five members,
four optional, constructed positionally. One flat `ClassificationContext` is *more* consistent with
the existing idiom than a two-level composition, which appears nowhere else in the repo.

**What I would do:** delete `RuleEvaluationContext`; make `ClassificationContext` flat with
`Success`, `RecoveryConfig` (and `MaxInProcessDelay`) defaulted. Two new types, not three.

### M2. The change works *around* a documented architecture edge that it could have deleted
`src/…/Resilience/Logic/EngineFailureClassifier.cs:21-29`, `ARCHITECTURE.md:55`

`CursorRecoveryTracker?` is kept as a third argument, and ~10 lines of doc across two files explain
that a `Contracts/` record may not reference another concept's `Logic/`. That rule reading is
**correct** — but look at what the classifier actually does with the tracker
(`EngineFailureClassifier.cs:34-40`): `recovery is not null` and `recovery.Watermark is not null`.
Two null interrogations, nothing else. `CursorRecoveryTracker` appears nowhere else in `Resilience/`
(grepped: one parameter, one doc mention).

So the dependency is not needed. Prototype B (verified green) replaces it with
`bool WatermarkAvailable`, supplied at the call site as `WatermarkAvailable: recovery?.Watermark is
not null` — semantically identical, since `IsExpiredCursor` is pure and cannot mutate the tracker
between the two reads. The consequences:

- `ClassifyResponse(response, context)` — two parameters, matching `MatchRule(response, context)`.
- The doc paragraph and the `<param name="recovery">` block both become unnecessary. The rule stops
  needing to be *explained* because nothing is bending around it.
- **`Resilience/Logic → Pagination/Logic` disappears.** `ARCHITECTURE.md:55` cites
  `EngineFailureClassifier.cs → CursorRecoveryTracker` as the only such edge, and lines 58-62 call
  the resulting `Pagination ↔ Resilience` 2-cycle out as debt that "phase 3 should decide" on. This
  change could retire half of it in one line, in the exact file that owns it.

This is the review's main disappointment. The change spent three types and ~30 lines of prose
accommodating an architectural constraint that a `bool` would have dissolved. **This is where
following the rules to the letter missed their point**: the letter says "Contracts must not touch
another concept's Logic" and the change complied by routing around it; the point of the layering
rule is to *not have the dependency*.

**What I would do:** drop the tracker parameter for `bool WatermarkAvailable` on the context, and
update `ARCHITECTURE.md:52-62` to remove the retired edge in the same commit (CLAUDE.md: "update
the task artifacts in the same commit as the work").

---

## Minor

### m1. The names locate the types instead of naming them
`RuleEvaluationContext.cs:16`, `ClassificationContext.cs:22`

`ResponseSnapshot` is a good name: a real noun, guessable, and it is what it says. The two
`…Context` names are not names, they are addresses — `Context` is the conventional word for
"whatever arguments were left over," and neither name distinguishes itself from the other. A
maintainer at `MatchRule` cannot tell which of the two to construct without reading both remarks
blocks. Contrast the repo's own vocabulary — `FailureDecision`, `PaginationState`,
`CursorRecoveryConfig`, `PaginationConfig` — each names a thing, and `ARCHITECTURE.md:210` treats
that scannability as a feature. M1 removes one of the two names; if you keep two types after all,
the pair worth naming is the *policy* the second one actually carries: `PageState` +
`MaxInProcessDelay` exist solely for `BuildDecision`'s "may I sleep here?" branch
(`EngineFailureClassifier.cs:197-201`), which is a retry budget, not a "context".

One small trap in `ResponseSnapshot`: `Pagination` already has `CursorRecoverySnapshot`, which is
*persisted* checkpoint state. A reader who met that first may expect this to be durable too. Not
worth renaming on its own; worth knowing.

### m2. `ResponseSnapshot.ResponseBody` is declared non-nullable while three call paths still guard it for null
`ResponseSnapshot.cs:12-15`, `EngineFailureClassifier.cs:107`, `:281-283`

`Nullable` is enabled repo-wide (`Directory.Build.props:7`). The record declares `string
ResponseBody`, yet `Matches` still tests `response.ResponseBody is null` and `IsExpiredCursor`'s
old signature explicitly took `string?`. The refactor quietly narrowed that annotation with no
warning (`GenerateDocumentationFile` and the null-check produce none) and no runtime consequence —
both producers are `ReadAsStringAsync`, which never returns null. It is still worth resolving rather
than inheriting: a new type is the cheap moment to decide. Either `string? ResponseBody` (matching
what the guards believe) or delete the guards. Do not leave the declaration and the guards
disagreeing.

### m3. The doc comments are arguing with a reviewer, not documenting a type
`ClassificationContext.cs:10-21` (17 lines of remarks for 4 lines of type),
`RuleEvaluationContext.cs:10-15`, `ResponseSnapshot.cs:8-11`,
`EngineFailureClassifier.cs:21-25`

The `<summary>` blocks earn their keep — they say what each carrier holds and why those members
travel together. The `<remarks>` do not. Their content is "Deliberately smaller than…", "Composes …
rather than flattening it", "which is what the parameter-object rule exists to prevent",
"deliberately NOT a member". That is a design defence, addressed to a reviewer, in a location that
will outlive the debate and drift from it. The Contracts-may-not-touch-Logic rule is now stated in
four places: `ARCHITECTURE.md`, `ClassificationContext`'s remarks, `ClassifyResponse`'s `<param>`,
and the CHANGELOG. Three of the four are in code.

Note also that these comments cannot be checked: the csproj deliberately keeps
`GenerateDocumentationFile` off until phase 3, so a stale `<see cref="RuleEvaluationContext"/>` will
not even fail the build. Rationale of this kind belongs in `decisions.md` and the CHANGELOG, where
it already is. And the fact that the most-defended decision is the one I think is wrong is not a
coincidence — the prose is doing work the shape could not.

**What I would do:** keep the summaries, delete the remarks (all three), let the CHANGELOG carry the
argument. Under M1+M2 most of them have nothing left to say anyway.

---

## Nit

### n1. Value-equality semantics that mean nothing
All three types are `record struct`s, so each gets a synthesised `Equals`/`GetHashCode` over members
including `IReadOnlyDictionary<string,string>` (reference equality) and the mutable
`PaginationState` class (reference equality, unstable hash). Nothing compares them today, so this is
cosmetic — but `record` advertises value equality, and in `Contracts/Models/` these look reusable to
the next author. `readonly struct` with no `record` would carry no such promise. Only worth acting on
if it costs nothing.

---

## What I checked and found correct — do not change it

- **`ResponseSnapshot` earns its place outright.** It is a real domain noun, the one carrier here
  that names a thing in the problem rather than a call chain, and it removes the
  `(int, string, dictionary)` triple threaded through five methods. That `Headers` is read only by
  `BuildDecision` (`:189`, `:194`) — not by `Matches` or `IsExpiredCursor` — **does not matter**.
  Cohesion in an aggregate is about whether the members belong to the same thing, not whether every
  reader touches every member; "the HTTP response" is exactly such a thing, and the alternative is
  bespoke tuples per callee. The right test is the reverse one: would a reader be surprised to find
  headers on a response snapshot? No.
- **Struct rather than class**, and the stated reason (one per page / per sub-request, none outlives
  the call), consistent with `FailureDecision`. Correct.
- **`Contracts/Models/` placement is right**, by the repo's own rules and by precedent. These are
  pure data with no behaviour (CLAUDE.md §1); they cannot be private nested types because
  `IntegrationEngine` constructs them, so the rule 2 exemption does not apply; and `FailureDecision`
  — likewise internal and likewise only ever exchanged between the classifier and the engine —
  already lives there.
- **Reference direction is correct.** `RuleEvaluationContext → PaginationState` and
  `ClassificationContext → SuccessConfig, CursorRecoveryConfig` are all `Contracts → Contracts`,
  explicitly permitted by `ARCHITECTURE.md:34-38`. No `Contracts → Logic` edge was introduced. The
  author read the dependency rule correctly — see M2 for why complying with it was the smaller half
  of the opportunity.
- **`internal` throughout, no `InternalsVisibleTo` added, tests exercise the change through the
  public entry point only.** That is exactly the stance CLAUDE.md's public-surface section takes.
- **No public surface moved, no `Cymulate.*` reference added** (rule 0 intact).
- **The mechanical substitution is faithful** — I compared every rewritten predicate against the
  pre-change signatures; `recoveryConfig is not null` → `is { } recoveryConfig`, the `withinCap`
  comparison and the `IsSuccess` argument order are all preserved, and 710 tests agree.

## Recommendation

Land it, with M1 and M2 applied first — they are ~20 lines of net deletion, both verified green, and
together they take the change from three types to two, `ClassifyResponse` to two parameters, the
classifier's `Pagination/Logic` dependency to zero, and the doc-comment defence to nothing that
needs defending. m2 while the file is open. m3 follows for free from M1+M2.

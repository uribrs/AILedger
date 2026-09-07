# Code review — `PaginationContext` / `IPaginator` reshape

**Reviewer lens:** design and API judgement.
**Scope reviewed:** `Pagination/Contracts/Interfaces/IPaginator.cs`, new
`Pagination/Contracts/Models/PaginationContext.cs`, `Pagination/Logic/PaginatorFactory.cs`, six
paginators, five `Execution/Logic/IntegrationEngine.cs` call sites, seven Pagination test files,
`CHANGELOG.md` diff. Judged against `CLAUDE.md` and `ARCHITECTURE.md`.

**Build/test state:** `dotnet build --nologo --no-incremental` → 0 warnings, 0 errors.
`dotnet test --nologo --no-build` → 710 passed, 0 failed. Pagination subset → 101 passed. No
mutations were performed; the tree is byte-identical to how I found it (`git status --porcelain`
matches the starting snapshot exactly: 17 modified, 1 untracked).

---

## Verdict

**Net-negative as designed, but cheap to correct, and one part of it is a genuine improvement.**

The change is mechanically clean — every one of the five engine call sites is a faithful rewrap, no
behaviour moved, and the tests confirm it. The problem is not correctness, it is that the abstraction
does not exist. `PaginationContext` is opened on the first line of all eighteen methods that receive
it (`var (config, state) = context;` — 18 occurrences across the six paginators, verified by grep)
and is never used as a unit by anybody. It has no members, no invariant, no behaviour, and is not
carried by its own primary caller: `IntegrationEngine.BuildPageRequest` (line 933), `AdvancePagination`
(line 1367) and `TryPrefetchNextPageAsync` (line 980) still thread `PaginationConfig` and
`PaginationState` as separate parameters and construct the context at the leaf, at each of five sites.

That is the signature of a parameter bag, not a concept. `CLAUDE.md` rule 7 is explicit that when
rule 5 trips, "the right question is never 'how do I get under the number?'" This change is exactly
that question: sixteen counted rule-5 violations close, the codebase is not better, and the
responsibility mismatch that produced the four-parameter signatures in the first place is untouched.
**This is the rule being satisfied to the letter while missing its point** — the answer to the
"letter vs point" part of the brief.

There is a decomposition that satisfies both rules, is already house style *inside this same concept
folder*, and needs no new public type. It is set out under Major-1.

---

## Findings

### Major-1 — The pair is the wrong axis; config belongs on the paginator instance
`src/Cymulate.Integration.Yaml.Engine/Pagination/Contracts/Models/PaginationContext.cs:21`

**Is it a real concept or a bag?** A bag, and the two members' natures are the proof. `PaginationConfig`
is per-operation, fixed for the whole walk, and derived from YAML. `PaginationState` is per-page,
mutable (every property has a `set;` — `PaginationState.cs:8-57`), and is the *output* of one of the
interface's own methods. A record that pairs a walk-scoped invariant with a page-scoped variable has
no single lifetime, so it cannot be constructed once and held; it must be rebuilt whenever the
variable moves. That is precisely what happens — 93 construction sites (11 in `src/`, 82 in `tests/`)
for a two-field type.

The axis that actually holds still is the config. It is resolved once per operation at
`IntegrationEngine.cs:283`:

```csharp
var paginationConfig = operation.Pagination;
var strategy = paginationConfig?.Strategy ?? PaginationStrategy.None;
var paginator = PaginatorFactory.Create(strategy, _logger);
```

One paginator instance, one config, both in scope on adjacent lines. Bind the config at construction:

```csharp
public static IPaginator Create(PaginationConfig? config, ILogger? logger = null)

PaginationState CreateInitialState();                                                    // 0 params
void ApplyToRequest(HttpRequestMessage request, Dictionary<string, object>? body,
                    PaginationState state);                                              // 3
PaginationState UpdateState(JsonDocument responseBody,
                            HttpResponseHeaders? responseHeaders,
                            PaginationState state);                                      // 3
bool HasMorePages(PaginationState state);                                                // 1
```

**Evidence this is house style, not my invention:** `CursorRecoveryTracker` — same concept, same
`Logic/` tier, one folder away — already does it:
`Pagination/Logic/Recovery/CursorRecoveryTracker.cs:12`,
`internal sealed class CursorRecoveryTracker(CursorRecoveryConfig config)`, with state-only methods
(`Seed`, `DedupAndTrack`, `RestartFromWatermark`). `BodyCursorPaginator` already takes a constructor
dependency (`ILogger`) too, so the shape is not new to the paginators either.

What that buys over the context, point for point:

- The same sixteen rule-5 violations close.
- No new public type, so the whole record-vs-struct and `default(...)` debate (Minor-1) evaporates,
  along with 93 construction sites.
- The `CreateInitialState` asymmetry disappears instead of needing a paragraph of defence (Major-2).
- `HasMorePages` reaches one parameter for a structural reason, not a consistency argument (Minor-2).
- The state stays a *named, visible* argument at every call site, which is what keeps the
  peek-versus-advance distinction readable (Major-3).
- Rule 7 is actually served: re-reading your own configuration on every call is not a per-call input,
  it is instance data with a lifetime, and giving it that lifetime is the SRP fix.

Honest costs: `PaginatorFactory.Create(strategy)` becomes `Create(config)`, so the ~10 factory
assertions in tests need a config object (versus the 82 call sites this change moved); a paginator
instance stops being reusable across operations with different configs, which nothing does today
since `Create` already allocates a fresh one per operation; and `PaginationConfig` is mutable, so
binding it does not make it immutable — but it is no worse in that respect than passing it per call.

### Major-2 — `CreateInitialState(PaginationConfig)` is a seam in the wrong place, and its own doc says so
`src/Cymulate.Integration.Yaml.Engine/Pagination/Contracts/Interfaces/IPaginator.cs:8-12`

> "Takes the config alone rather than a context, because it is what produces the state a context
> carries."

Read plainly, that sentence is an argument against the context, not for the exception. If one of the
four methods cannot accept the pair because it *manufactures* half of it, then the pair is not the
concept: the config is the invariant and the state is the variable. The inconsistency is not a
tolerable wart around a good abstraction; it is the abstraction reporting its own seam location.

Under Major-1 the method becomes `CreateInitialState()` and the asymmetry does not exist to justify.
If the context is kept anyway, delete the justification clause and leave the description — an
implementer needs to know what the method returns, not which reviewer objection the author
anticipated.

### Major-3 — The box hides *which* state is being asked about, next to a two-state method
`src/Cymulate.Integration.Yaml.Engine/Execution/Logic/IntegrationEngine.cs:1001-1003`

This is the concrete trap the brief asks about, and it is worse than the abstract "immutable record
holding a work-in-progress value" concern:

```csharp
var peekState = paginator.UpdateState(
    responseDoc, responseHeaders, new PaginationContext(paginationConfig, pageState));

if (peekState.IsComplete || !paginator.HasMorePages(new PaginationContext(paginationConfig, peekState)))
    return null;
```

Two contexts, one method, differing by exactly one page. Post-change both `HasMorePages` call sites
in the engine — line 812 and line 1003 — read *identically* apart from the second constructor
argument, so the only load-bearing distinction (`pageState` versus `peekState`) has moved from the
first parameter position into the middle of a constructor call. Pre-change it was
`HasMorePages(peekState, config)`, where the state led.

Failure scenario, and it is not hypothetical given the shape: a later reader sees a type named
"context", documented as immutable, constructed twice in four lines, and hoists it to a local for
reuse — the natural cleanup. The second call then asks "are there more pages?" about the *previous*
page's state. `peekState.IsComplete` still short-circuits some cases, but on a cursor strategy where
`pageState` carries a live cursor and `peekState` does not, `HasMorePages` answers `true` for a walk
that has ended, and the engine prefetches a page that does not exist — or, in the `max_pages` case at
line 812, re-enters the loop. That is the unbounded-pagination hang class this repository already
warns about.

Same defect in miniature inside all six paginators, e.g.
`Pagination/Logic/Paginators/BodyCursorPaginator.cs:95`:

```csharp
next.IsComplete = !HasMorePages(new PaginationContext(config, next));
```

The method destructured `context` on line 71 and now rebuilds a *different* context to ask its own
question. Six copies of "unpack the box, then repack it with the other value". A context that must be
rebuilt every time the interesting member changes is not carrying anything.

Minimum fix if the type is kept: use `context with { State = next }` in the six `UpdateState` bodies,
which at least names the relationship ("same walk, advanced state") instead of re-asserting the pair
from scratch. The real fix is Major-1, where `HasMorePages(next)` needs no ceremony at all.

### Major-4 — The type's central design justification rests on a property the repo plans to delete
`src/Cymulate.Integration.Yaml.Engine/Pagination/Contracts/Models/PaginationContext.cs:9-13`

> "A record class rather than a `record struct`, deliberately. **This type is public**, so a
> `default(...)` on a struct version would hand a caller outside this assembly an instance whose
> `Config` and `State` are both null…"

`CLAUDE.md`, Public surface: *"Helpers, paginators, authenticators, converters and mappers are
implementation detail."* `ARCHITECTURE.md:269` lists "101 public types → default to `internal`" as
phase-3 work, and `ARCHITECTURE.md:365` says phase 3 "narrows ~130 public types to `internal`".
`IPaginator` and everything it touches are named implementation detail destined for `internal`.

So the load-bearing premise of the type's only documented design decision is a property the signed-off
plan removes. The moment phase 3 lands, the paragraph is false and the conclusion it defends is
unmotivated.

Worse, the repository already answered this question the other way, in the same week, for the same
problem shape. `Resilience/Contracts/Models/ResponseSnapshot.cs:9-11`:

> "A struct, matching `FailureDecision`, because one is constructed per page and per hydrate
> sub-request and **none of them outlives the classification call**."

And `PaginationContext.cs:16-18` states the identical property about itself:

> "a context is constructed per call and **never outlives one**."

Two sibling parameter-object contexts, both per-call, both short-lived, cite the same lifetime fact
and reach opposite struct/class conclusions. The only differentiator is public versus internal
(`ClassificationContext`, `RuleEvaluationContext` and `ResponseSnapshot` are all
`internal readonly record struct`) — and that differentiator is the thing phase 3 erases. The
codebase now carries two idioms for one role.

**Sequencing is what I would change.** Make `IPaginator` and friends `internal` *first*. That single
step deletes the `default(...)` argument, makes `readonly record struct` the consistent choice, and
turns this from a breaking change into a non-breaking one. As shipped, the change is ordered so that
every design decision inside it is justified by a property the plan says to remove.

### Major-5 — CHANGELOG attributes a measured number to the wrong cause, and asserts a defect the tests do not have
`CHANGELOG.md`, the `UpdateState` paragraph; same text at `IPaginator.cs:22-27`

Two separate problems, both instances of the failure pattern the repository's own preamble names —
*a claim made at a wider scope than what was verified, always in the direction of "done"*.

**(a) The 82 is real but misattributed.** "Making it explicit moved 82 test call sites." Measured:
`git diff tests/ | grep -cE '^\+.*(UpdateState|ApplyToRequest|HasMorePages)\('` = 82. Broken down:
31 `UpdateState`, 30 `HasMorePages`, 21 `ApplyToRequest`. Only the 31 `UpdateState` sites can be
attributed to the header change at all, and 5 of those already passed headers — so the header default
accounts for **26** newly-explicit `null` arguments. The other 56 moved because of the context boxing.
The number is measured; the causal claim attached to it is 3× too wide.

**(b) The stated defect is not present in this suite.** "the default existed so callers and tests need
not supply it, which let a header-driven strategy be exercised without ever passing a header." Every
test that exercises a header-driven strategy already passed headers *before* the change — verified
from the diff itself, which shows no site converting from omitted-headers to real-headers:
`LinkHeaderPaginatorTests.cs:31,43,54`, `PaginatorEdgeTests.cs:64`,
`PaginationStateCarryTests.cs:85`. Zero tests gained header coverage. 26 body-driven sites gained a
literal `null`.

**Was it worth it?** Directionally yes, on contract grounds that the doc does not state: headers are
part of the page just received, not an optional extra, and `UpdateState`'s job is "advance from this
page", so an omitted-by-default half of the input is a bad contract. But as shipped the trade is
`= null` for `, null,` — the parameter is still `HttpResponseHeaders?`, so nothing is enforced, and an
explicit `null` at a call site is no more informative than an omission. If the goal is a contract the
compiler defends, make it non-nullable and have the body-driven tests pass
`new HttpResponseMessage().Headers` (already the idiom at `LinkHeaderPaginatorTests.cs:54` and
`PaginationStateCarryTests.cs:82`). That is the version worth 26 test edits. The current version is
churn with a good intention behind it.

The CHANGELOG's mutation claim — "a mutation pass across five paginators confirms all of them still
fail when the behaviour they name regresses" — I did not re-run (mutating these files risks the
unbounded-pagination hang, and the tree had to be restored exactly). It is consistent with 101
pagination tests passing, but it is orthogonal to the header question: mutation coverage of *body*
parsing says nothing about whether making *headers* mandatory bought anything.

### Minor-1 — Record class over struct: right conclusion, wrong reasoning, and the perf premise is false
`src/Cymulate.Integration.Yaml.Engine/Pagination/Contracts/Models/PaginationContext.cs:9-13`

**The measured cost.** `sealed record PaginationContext(PaginationConfig, PaginationState)` is 32
bytes on x64 (16-byte object header plus two 8-byte references). Per HTTP page the engine constructs
4 contexts on the normal path (`BuildPageRequest`/`ApplyToRequest`, `AdvancePagination`/`UpdateState`,
the `HasMorePages` rebuilt inside `UpdateState`, and the loop condition at line 812) and up to ~8 with
prefetch enabled. That is roughly **128–256 bytes per page**, gen-0, no finalizer, dead immediately.

**"Hot pagination loop" is a mischaracterisation.** One iteration per network round-trip. Each
iteration already costs an HTTP request, a full-body `JsonDocument.Parse`, N record mappings each
allocating a `Dictionary<string, object?>`, and a sink publish. 32-byte allocations are nanoseconds
against milliseconds — unmeasurable. So: no objection to the class on performance grounds, and equally
no benefit would have been available from the struct. The dimension does not matter here, which is
itself worth saying plainly rather than defending at length.

**The `default(...)` argument is technically sound but weakly motivated.** `default(T)` on a struct
with non-nullable reference members is a genuine nullable-reference-types hole with no warning, yes.
But it is a hazard for a type `CLAUDE.md` says should not be public (Major-4), and it is not the
strongest available argument. The better one, which the doc does not make: a struct copied by value
while holding a reference to a *mutable* `PaginationState` gives you value semantics over shared
mutable state, which is more confusing than either pure option. That reason survives phase 3; the
`default(...)` reason does not.

### Minor-2 — `HasMorePages` 2 → 1: the diagnosed problem was ordering, not arity
`src/Cymulate.Integration.Yaml.Engine/Pagination/Contracts/Interfaces/IPaginator.cs:33`

Partly agree. There *was* a real inconsistency, but it was not the parameter count — it was that
`HasMorePages(state, config)` took the pair in the opposite order from `ApplyToRequest(…, config,
state)` and `UpdateState(body, config, state, headers)`. Note it was a readability defect only, not a
correctness one: the two types differ, so a transposed call could never silently compile.

The proportionate fix for an ordering complaint is to swap two parameters — ~30 test sites, no new
type, and still compile-safe for any external implementer. Boxing to fix it cost 82 test sites and a
new public type. So: the author's instinct that `HasMorePages` should take one parameter is right, and
under Major-1 it gets there structurally (`HasMorePages(state)`); the consistency argument as
*deployed* is the expensive answer to the cheap problem.

### Minor-3 — Argument order `(body, headers, context)` is correct; the boxing picked the wrong pair
`src/Cymulate.Integration.Yaml.Engine/Pagination/Contracts/Interfaces/IPaginator.cs:28-31`

Context-last is right, and there is house precedent rather than taste behind it:
`EngineFailureClassifier.cs:21` `ClassifyResponse(ResponseSnapshot response, ClassificationContext
context)` and `:53` `MatchRule(ResponseSnapshot response, RuleEvaluationContext context)` both put
payload first, context last. Keep the order. Do not move the context first.

But the same comparison exposes a mis-boxing. Resilience boxed the **response triple**
(`ResponseSnapshot(status, body, headers)`) and left the context separate. Pagination left
`responseBody`/`responseHeaders` loose and boxed config+state instead — the inverse choice for the
same shape of method. `body` and `headers` are the pair that genuinely coheres: same
`HttpResponseMessage`, same lifetime, both read-only inputs, both meaningless without the other.
Config and state do not cohere: different lifetimes, different mutability, and one of them is the
method's return value. If `UpdateState` wants a box, `UpdateState(PageResponse page, …)` is the box
that pays.

### Minor-4 — `ARCHITECTURE.md` not updated in the same change
`ARCHITECTURE.md:151-158`, `:279-280`

`CLAUDE.md`: *"Update `CHANGELOG.md` and the task artifacts in the same commit as the work."*
`ARCHITECTURE.md:122` claims its layout is *"Generated from the tree, not aspirational — this is what
is on disk today."* It is now stale in two places: `Pagination/Contracts/Models` omits
`PaginationContext` (grep for `PaginationContext` in `ARCHITECTURE.md` → no hits), and the measured
rule-5 table still reads 58 violations across 21 files when 16 have just closed. The CHANGELOG was
updated; the document that claims to mirror the tree was not.

### Minor-5 — The invariant the type documents is not enforced, and its first caller launders a null past it
`src/Cymulate.Integration.Yaml.Engine/Execution/Logic/IntegrationEngine.cs:812`

```csharp
} while (forceContinue || (!pageState.IsComplete && paginator.HasMorePages(new PaginationContext(paginationConfig!, pageState))));
```

The type's whole documented reason for being a class is that a null `Config` should be impossible.
The primary caller reaches it with `paginationConfig!`. Not a live bug — when `paginationConfig` is
null the pagination-less path sets `IsComplete = true` at line 286, nothing ever clears it, and
`!pageState.IsComplete` short-circuits before the constructor runs, so the null is unreachable today
(verified: the only `IsComplete` writes in the engine are line 286 and the reassignment inside
`AdvancePagination`, which only runs under `paginationConfig is not null` at line 790). The `!` was
present pre-change too, so nothing regressed.

What is new is a *false* invariant: the type now advertises non-null `Config` while its first call
site suppresses the compiler to satisfy it. A future `HasMorePages` implementation that reads
`context.Config.MaxPages` unconditionally would NRE if the short-circuit ordering at line 812 were
ever rearranged. Either add `ArgumentNullException.ThrowIfNull` in the record body so the guarantee is
real, or type the member `PaginationConfig?` and stop claiming it.

### Nit-1 — Only `Deconstruct` is used; the other synthesized members are dead weight with surprising semantics
`src/Cymulate.Integration.Yaml.Engine/Pagination/Contracts/Models/PaginationContext.cs:21`

`record` was chosen for positional deconstruction, which is fair. But it also synthesizes
`Equals`/`GetHashCode`/`ToString`/copy-constructor, none of which is used, and their semantics are
non-obvious: `PaginationConfig` and `PaginationState` are plain classes with no `Equals` override, so
two contexts wrapping the same config and equal-but-distinct states compare unequal. Harmless today
because nothing compares contexts; worth one line of awareness if the type survives.

### Nit-2 — The `AuthContext`/`TemplateContext` naming analogy is false and self-refuting
`src/Cymulate.Integration.Yaml.Engine/Pagination/Contracts/Models/PaginationContext.cs:4-6`

> "Named for the same reason as `AuthContext` and `TemplateContext` — it is the ambient state a
> strategy reads, not a value it computes."

Both halves fail. `AuthContext` is a long-lived mutable object *owned and cached* by its manager
(`AuthManager.cs:77`, `_contextCache[integrationName] = context`) holding a token and expiry;
`TemplateContext` is a set of `init`-only scopes held for the whole operation. Neither is a per-call
parameter wrapper, which is all `PaginationContext` is. And the second clause contradicts the
interface it documents: `State` is exactly "a value it computes" — `UpdateState` returns a new one.
The analogy borrows authority from two types that do not share the property being claimed.

---

## Do the new XML docs inform, or pre-argue with a reviewer?

Pre-argue, in all three places, and the tell is consistent: each one answers an objection rather than
describing behaviour.

- `PaginationContext.cs:9-19` — a 10-line `<remarks>` in which not one line tells an implementer
  anything about *using* the type. Five lines defend a class-versus-struct choice whose performance
  dimension is unmeasurable (Minor-1) on a premise phase 3 deletes (Major-4). The second `<para>`
  documents a lifetime rule ("constructed per call and never outlives one") that the type cannot
  enforce — the comment doing work the design should.
- `IPaginator.cs:22-27` — "**Now** mandatory rather than defaulted: the default existed so callers and
  tests need not supply it, which…". "Now" is changelog voice in a permanent API doc; the same
  sentences already exist in `CHANGELOG.md` verbatim, which is where they belong. An implementer in
  six months does not care what the parameter used to be. And the factual claim inside it is wrong for
  this suite (Major-5b). What the doc should say is what the *contract* is: headers accompany the body
  of the page just received; header-driven strategies require them.
- `IPaginator.cs:8-12` — describes `CreateInitialState` in terms of a type absent from its signature,
  then defends the asymmetry (Major-2). "Builds the starting state for a fresh walk." is the whole
  useful content.

The pattern is worth naming beyond this diff: a doc comment that argues is a design smell detector.
In each of these three cases the thing being defended is the thing a reviewer should change.

---

## Was the breaking change worth making at all?

**The breakage costs nothing; the ROI is the problem, and the sequencing is wrong.**

The compatibility question is a non-issue, and I verified the claim rather than taking it. The
CHANGELOG asserts "no host call site is affected, since the adapter never touches it" without naming
a search, which `CLAUDE.md` forbids. The search, run against the read-only consumer at
`/Users/user/Dev/cymulate-integration-adapters`:

```
grep -rn "IPaginator|PaginatorFactory|\.UpdateState\(|\.HasMorePages\(|CreateInitialState" --include="*.cs" .
  | grep -v "Cymulate.Integration.Yaml.Engine/" | grep -v "Cymulate.Integration.Yaml.Engine.Test/"
→ (none)
```

Zero hits outside the vendored engine copy. The only host-side pagination touchpoint is
`IExecutionSink.SetPaginationState`, unaffected (`YamlLocalRunner/Program.cs:212`). The claim is true;
it just needed the search named. One thing the CHANGELOG does not mention and should: that repo still
carries a full **vendored source copy** of the engine, so every reshape here widens a divergence that
someone eventually reconciles.

Add the package state — unshipped, pre-1.0, `ARCHITECTURE.md:361` "Nothing publishes as `1.0.0` until
phase 3 is done" — and breaking `IPaginator` today is free. So "should we defer because it is
breaking?" is the wrong question.

The right one is **ordering**. Phase 3 will (a) narrow `IPaginator` to `internal` and (b) rewrite this
interface's entire calling environment into `PageLoop`/`PageDispatcher`/`PageRequestFactory`
(`ARCHITECTURE.md:305-321`). Doing a breaking reshape of `IPaginator` *now* and narrowing it to
`internal` *later* means touching seven implementations and 82 test call sites twice, and it means —
as Major-4 shows — that every design decision in this change is justified by a public-ness the plan
removes. Invert it: narrow the visibility first, then reshape the signature inside the assembly for
free, with `readonly record struct` as the consistent choice and no `default(...)` paragraph needed.
That is not deferral, it is doing the same work once in the order that makes it cheap.

---

## What I would keep

To be explicit that this is not a wholesale rejection:

1. **Diagnosing the parameter-ordering inconsistency was correct.** `HasMorePages(state, config)`
   against `ApplyToRequest(…, config, state)` was a genuine readability defect that had survived
   review.
2. **Making `responseHeaders` explicit is directionally right**, on the contract grounds the doc does
   not state. Finish it by making it non-nullable (Major-5).
3. **`(payload…, context)` ordering matches house style** and should not be changed (Minor-3).
4. **Placement and hygiene are correct**: `Pagination/Contracts/Models/PaginationContext.cs`, one type
   per file, namespace `…Engine.Pagination` — rule 1, rule 2 and the ARCHITECTURE layout rules all
   satisfied.
5. **The mechanical execution is trustworthy.** All five engine rewrites preserve semantics, build is
   warning-free, 710 tests green, no behavioural diff.

## Priority if only one thing changes

Major-4's sequencing point, because it subsumes several others. Make `IPaginator` `internal` first;
then Major-1 (config on the instance) lands as a free, non-breaking, no-new-type change that closes
the same sixteen rule-5 violations, dissolves Major-2 and Major-3, and retires Minor-1, Minor-5 and
Nit-1 along with the type itself.

---

## Environment note (not a review finding)

Three `dotnet test` / `testhost` process pairs were already running when I finished (PIDs 45620,
45988, 46782; elapsed 21, 17 and 13 minutes against a 542 ms suite — so almost certainly hung, and
none of them mine: both of my runs exited cleanly and were under two minutes old at that point).
I deliberately did **not** kill them, since other agents are active in this session and one may be
blocked on a run. Flagging for whoever knows the session topology.

# Defect Register — Rule-Compliance Decomposition

Findings recorded as they surface, per `constraints.md`. A defect folded into a commit message instead
is one nobody can audit.

Status vocabulary: OPEN, FIXED, ACCEPTED (recorded, deliberately not fixed), HOST (found in the
read-only monorepo — recorded, never fixed).

---

## D1 — The in-process retry cap was unpinned by the entire suite — FIXED (C1)

`EngineFailureClassifier.BuildDecision` decides whether a rule-driven retry sleeps in-process or
escalates to a scheduled resume:

```csharp
var withinCap = context.MaxInProcessDelay is null || delay <= context.MaxInProcessDelay.Value;
if (withinCap && context.PageState.SamePageRetries < maxRetries)
```

Replacing that comparison with `withinCap = true` — ignoring the host's cap entirely — left **all 709
tests green**.

**Corrected after verifier F2.** The first version of this entry said "every retry test passes a
60-second cap, so only the `max_retries` budget was ever exercised". That is false, and I did not check
it before writing it: three tests pass a 1-second cap (`EngineErrorRuleTests.cs:150`, `:359`, `:414`).
The cap is also enforced at a *second* site, `RetryPolicyFactory.cs:85`, which
`CustomRateLimitHeaderAboveCap_Externalizes` already pinned — so "the cap was untested" was too wide.
The accurate statement: the cap was untested **on the `error_handling.rules` path**, where
`BuildDecision` makes the comparison. The conclusion held; the stated mechanism did not.

The consequence in production: a vendor asking for a 30-minute wait would be slept in-process instead
of externalised, holding a collector pod for the duration, and nothing in the suite would object.

**Fixed** by `EngineErrorRuleTests.BodyThrottle_WhenVendorDelayExceedsTheInProcessCap_ExternalizesWithoutRetrying`
— vendor asks 40 ms, host allows 10 ms, asserts one HTTP request and an externalised `RetryAfter`.
Verified to fail under the mutation and pass without it, in both directions.

**Pre-existing**, not introduced by C1. Found because C1 routed that same value through a new record
and A5 required mutation-testing rather than trusting a green suite.

## D2 — Two classifier behaviours are each pinned by exactly one test — ACCEPTED

Rule/success precedence is pinned only by `Http200WithErrorBody_DefersWithBodySourcedDelay`.
Cursor-expiry detection is pinned only by `ExpiredCursorMidPagination_RestartsFromWatermark_NoDuplicates`.
Delete or weaken either test and a whole behaviour of an `internal` type becomes unverified.

Not fixed: adding redundant coverage is outside C1's scope, and the two tests do currently work. Worth
knowing before anyone edits them.

## D3 — One null guard in `Matches` is now unreachable — ACCEPTED

`ResponseSnapshot.ResponseBody` is a non-nullable `string`; `IsExpiredCursor` previously accepted
`string?`. `Matches` still contains `if (response.ResponseBody is null || …)`.

Kept deliberately. Deleting a defensive check during a behaviour-preserving refactor is how a
refactor stops being behaviour-preserving; both call sites pass a non-null body from
`ReadAsStringAsync`, so the guard is dead rather than wrong. Remove it in a change that is about
nullability, not about parameter counts.

## D5 — Cursor-recovery vs rule precedence is pinned by nothing — OPEN

Found by code reviewer A, reproduced. `EngineFailureClassifier.ClassifyResponse` documents its ordering
as load-bearing: the cursor-recovery check runs *before* rule evaluation. Two mutations — swapping the
recovery and rules blocks, and moving recovery after the success check — **each left 710/710 green**.

Why nothing catches it: the only definition in the 279-file corpus using `cursor_recovery`
(`crowdstrike-falcon`, `get_findings_for_hosts`) declares no `error_handling.rules`, so the two blocks
never compete. It bites on a definition with `expiry_status: 404` plus a `status: [404] action: skip`
rule — with the order flipped, an expired cursor is silently treated as a skippable page and collection
ends early reporting `Success = true`.

Not fixed: the fix is a test with a purpose-built definition, and C1's scope is parameter records. This
is the second unpinned behaviour in the same 276-line class (see D1), which is the more useful signal:
the classifier's coverage is thin wherever two features interact rather than one firing alone.

## D6 — `default(ClassificationContext)` gives a null `PageState` with no compiler warning — OPEN

Found by code reviewer A. The new types are `readonly record struct`, so `default(...)` is legal and
silent: `PageState` becomes null and dereferencing it throws at
`EngineFailureClassifier.cs` where the retry budget is read. As positional parameters, a null
`PaginationState` argument produced `CS8625` at the call site.

No call site does this today — both construct the record explicitly. Recorded because the struct choice
traded a compile-time guarantee for an allocation saving, and that trade was not deliberate when made.

## D7 — The consuming adapter carries a vendored copy of the engine, not a package reference — HOST

Found by code reviewer A. `cymulate-integration-adapters` contains its own copy of the engine source
under `Collectors/YamlCollector/Cymulate.Integration.Yaml.Engine/`. "No host call site changed" is
therefore true but narrower than it sounds: the host is not consuming this repository at all yet, the
two trees have already diverged, and C1 adds three more files to whatever reconciliation eventually
happens.

Recorded, not fixed — the monorepo is read-only, and the reconciliation is the packaging milestone, not
this task.

## D8 — `ARCHITECTURE.md`'s compliance ledger had six wrong numbers — FIXED (C1)

Its outstanding-debt table read `IntegrationEngine.cs` 1,990 lines with a 741-line method,
`WorkflowRunner.cs` 923 with a 150-line method, `YamlIntegrationLoader.cs` 672, and "~130 public
types"; measured values are 1,902/729, 912/155, 655 and 101. It also asserted "Four methods exceed 100
lines" (three do) and "**Every other file complies**" — which was never true and is off by 58 rule-5
violations across 21 files.

Fixed in C1's commit with measured values and a per-rule table, because C1 also invalidated the
document's `Pagination ↔ Resilience` cycle statement, and a stale architecture document is how the
predecessor branch's documentation failure actually manifested.

## D9 — Whether `RuleEvaluationContext` earns its place is unresolved and is the operator's call — OPEN

Code reviewer B argues the composed pair should be one flat five-member record and **prototyped it: 710
green, with the hydrate call site coming out shorter** than the delivered version
(`new ClassificationContext(errorHandling, new PaginationState())`, no explicit nulls). The reviewer is
right that the delivered rationale was overstated — the hydrate site still fabricates a
`PaginationState` and passes an explicit null cap, so composition saves two members, not four. That
overstatement is corrected in the type's XML doc.

**Not acted on, deliberately.** `prompt_contract.md` names flattening to a single record as a *stop
condition*, not a fallback, and `decisions.md` D1 chose composition before any code existed. An
executor overriding a signed-off stop condition on its own authority is the failure mode the contract
exists to prevent — even when the reviewer is persuasive and has a working prototype. Surfaced for the
operator with the evidence attached.

## D4 — A `Contracts/` → `Logic/` dependency was nearly introduced, then the whole edge was deleted — FIXED (C1)

`decisions.md` D1 specified a context record holding the `CursorRecoveryTracker`. The tracker is in
`Pagination/Logic/Recovery/`; the record would be in `Resilience/Contracts/Models/`. That is
`Contracts/` depending on `Logic/`, which the dependency rule does not permit — it grants only
`Contracts/` → other concepts' `Contracts/`.

First avoided by keeping the tracker a separate parameter — a parameter-count fix that creates a
layering violation is not a fix.

**Then resolved properly, on code reviewer B's finding.** Keeping the tracker as an argument routed
prose around the dependency instead of removing it. The classifier only ever asked the tracker two null
questions, so it now takes `bool WatermarkAvailable` on `ClassificationContext`. Verified: the tracker
was the *only* `Pagination/Logic` type referenced anywhere in `Resilience/`, so the
`Resilience/Logic → Pagination/Logic` arm is gone entirely, and the `Pagination ↔ Resilience` 2-cycle
that `ARCHITECTURE.md` deferred to phase 3 no longer exists. The remaining edge
(`BodyCursorPaginator → DelayResolver`) is one-directional. `ClassifyResponse` also drops from 3
parameters to 2, and roughly ten lines of documentation defending the old shape are deleted.

The reviewer's framing is worth keeping: the layering rule's purpose is to not have the dependency, not
to write a comment explaining why the dependency is acceptable. The first fix satisfied the rule's
letter while missing its point.

## D10 — I cited "D11" in two artifacts before the entry existed — FIXED (C2)

`assumptions.md` A1 and `progress_log.md`'s C2 section both referenced "D11" for the
non-terminating-paginator gap while the register ran D1–D9. The finding was real and filed nowhere; the
citation pointed at nothing. Caught by the C2 verifier.

The same shape as C1's fabricated SHA: a forward reference written as though already true. Entries are
now numbered only when written.

## D11 — No test bounds an operation whose paginator never terminates — OPEN

Three of C2's mutations (`CursorPaginator` and `BodyCursorPaginator` forwarding no cursor,
`OffsetPaginator` not advancing) were detected as the suite **hanging**, not failing. The engine keeps
requesting pages and no test imposes a wall-clock or page-count ceiling that fails the run.

`max_pages` bounds a *configured* walk, but these mutations produce a walk the paginator itself never
reports complete. In production the collector pod would spin until the host's own timeout, if it has one.

Not fixed: it needs a bounded-iteration guard or a hanging-test timeout in the harness, which is a
change to the suite's infrastructure rather than to a parameter list.

**Process consequence, which matters more than the defect:** "killed by hang" is weak evidence for the
question A1 asked. A hang can only arise in the untouched `Execution/` engine tests, so it is consistent
with the rewritten unit tests having gone vacuous and something else catching the mutation. The C2
verifier re-ran all three filtered to `~Tests.Pagination`, where no loop is possible, and got 2, 6 and 4
assertion failures inside the rewritten files. That is the evidence A1 needed. Mutation gates on this
suite must be run filtered.

## D12 — `PaginationContext` may be the wrong shape; binding config on the paginator is the alternative — OPEN

Code reviewer B argues the record is a bag rather than a concept, with concrete evidence I could not
refute:

- it is destructured on the first line of all 18 methods that receive it — no method uses it as a unit;
- the engine's own page-loop helpers (`BuildPageRequest`, `AdvancePagination`,
  `TryPrefetchNextPageAsync`) still thread config and state separately and wrap them only at the leaf;
- 93 construction sites for a two-field type.

The proposed alternative is house style one folder away — `CursorRecoveryTracker` is
`internal sealed class CursorRecoveryTracker(CursorRecoveryConfig config)` with state-only methods. Bind
the config on the paginator via `PaginatorFactory` (already called once per operation at
`IntegrationEngine.cs:283` from that same config local) and the signatures become `HasMorePages(state)`,
`UpdateState(body, headers, state)`, `CreateInitialState()`. Same 16 violations close, no new public
type, the `CreateInitialState` asymmetry dissolves, and the reviewer estimates ~10 test edits instead of
82.

A second, sharper point: the reviewer notes `ResponseSnapshot` cites "none outlives the call" as the
reason to be a `readonly record struct`, while `PaginationContext` states the same property about itself
and concludes the opposite. Two idioms for one role in one branch.

**Not acted on.** `decisions.md` D2 fixed the approach before any code existed, the delivered change is
independently verified behaviour-preserving by two reviewers, and the alternative changes the paginator's
lifetime contract — it would need its own review cycle. Surfaced with the evidence for the operator.

**Worth the operator's attention as a pattern, not just an item:** this is the second of six code-review
passes to conclude that the shape the contract specified is not the best available shape (see D9). Two
for two on the design lens. That is a signal about how the contract was written, not only about these
two commits.

## D13 — `ARCHITECTURE.md` went stale again one commit after being fixed — FIXED (C2)

D8 replaced its compliance ledger with measured values during C1. C2 changed the counts again, and the
document did not move: "101 public types" became 102, and the concentration sentence ("27 of the 58")
was stale. Numbers in a document that nothing regenerates go stale by default.

Fixed, and every count now carries an explicit as-of marker so a stale number reads as dated rather than
as current. The underlying problem — that these numbers are hand-maintained with no CI to check them —
is unfixed and is the same root cause as `CLAUDE.md`'s missing rule-0 guard.

## D14 — C3 introduced the one dependency edge `ARCHITECTURE.md` calls enforceable — FIXED (C3, pre-commit)

Moving the join grammar to `Workflow/Logic/Merging/MergeJoinParser` made
`YamlIntegrationLoader` (`Definition/Logic`) call into `Workflow/Logic`. `ARCHITECTURE.md:47-48` states
the one enforceable direction — no concept's `Logic` may reference `Workflow/Logic` or `Execution/Logic`,
except `Workflow → Execution` — and adds "it currently holds". It stopped holding, and it closed a
3-cycle: `Workflow/Logic → Execution/Logic → Definition/Logic → Workflow/Logic`.

Verified as newly introduced: at `ba02294` the loader referenced no `Workflow/Logic` type.

Sharpest detail: `MergePlanFactory`'s own `<remarks>` cited `ARCHITECTURE.md`'s dependency rule to
justify its existence, in the same commit whose sibling file broke that rule. The old
`merge.TryParseJoin(...)` call — the rule-1 violation being fixed — was what had kept the layering rule
true. C3 as first written traded one invariant for another and reported only the half that a counter
measures.

**All three reviewers found it independently.** Fixed by moving the parser to
`Definition/Logic/Validation/`: the loader's call becomes intra-concept, and `MergePlanFactory`
(`Workflow/Logic`) → `Definition/Logic` is a permitted direction. Re-audited by type reference after the
fix; the rule holds, and the only remaining cross-concept hits into `Workflow/Logic` are doc-comment
mentions of `MergeShapeProjector`, which are links rather than references.

**The lesson is the measurement, not the mistake.** Rules 1 and 5 have counters and a parser that reports
them. The dependency rule has neither, so a regression in it was invisible to every check I ran while a
one-violation improvement was visible. What is counted gets protected.

## D15 — Re-soldering the parser's error wire is unpinned — OPEN

Found by the C3 verifier. `MergeJoinParser.TryParse` assigns `error = parsed.Error` — the exact wire the
`out`-parameters-to-result-type reshape had to reconnect. Deleting that assignment leaves **710 tests
green**: a malformed `on:` expression would then be reported with a null message.

Also unpinned: 5 of the parser's 9 user-facing error messages have no test asserting them, and the
grammar is now `internal`, so the only route to them is an end-to-end definition load.

Not fixed. C3 shipped without a mutation gate of its own — C1 ran 3 mutations and C2 ran 11, C3 ran 0
until the verifier ran 2. That asymmetry is the defect worth noting: the gate was applied where the
contract named it and skipped where it did not.

## D16 — `MergeJoinSpec` and `MergeAnchor` are public with no public producer — OPEN

Found by code reviewer A. Both remain `public` while the only thing that produced them,
`MergeIntoConfig.TryParseJoin`, is gone and its replacement is `internal`. They are now a SemVer
commitment no external caller can obtain. Free to narrow today, expensive after the first release.

Recorded, not fixed: narrowing them is the phase-3 public-surface pass, which `constraints.md` puts out
of scope.

## D17 — `MergeJoinParser.TryParse` is 87 lines and does three things — OPEN

Code reviewer B's argument, which I accept as correct and did not act on: the method normalises `On` into
expressions, validates the mode, and unifies source keys — and the mode check living inside the *join*
parser means a bad `mode:` surfaces as an `'on'`-shaped error.

The reviewer's sharpest point: the "decomposition is out of scope" defence is inconsistent, because this
same commit *did* decompose `TryParseExpression`, and the CHANGELOG presents that as an improvement. The
operative rule was not scope, it was **whether a counter moved** — rule 5 counts parameters, so the 4-param
helper got fixed; rule 3 counts only above 100 lines, so the 87-line three-job method stayed. That is the
smoke detector silenced by a compliant number, which is precisely what `CLAUDE.md` rule 7 warns against.

Suggested cut, for whoever takes it: `NormalizeExpressions` / `UnifyAnchors`, and lift the mode check to
`ValidateMergeOptions` beside `unmatched`.

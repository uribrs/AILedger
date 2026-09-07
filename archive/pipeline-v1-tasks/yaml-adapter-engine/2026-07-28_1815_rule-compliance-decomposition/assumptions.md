# Assumptions

## A1 — The `IPaginator` test call sites can be updated without weakening any assertion — VALIDATED

**First correction: there are 82 call sites, not 52.** The contract's number came from a grep for
`.ApplyToRequest(` and `.UpdateState(` that excluded authenticator hits — and excluded `HasMorePages`
entirely. Measured by the transformer that rewrote them: 82 sites across 7 files (`BodyCursorPaginatorTests`
21, `PaginatorEdgeTests` 19, `PaginatorTests` 10, `OffsetPaginatorEdgeTests` 10, `LinkHeaderPaginatorTests`
8, `PaginationStateCarryTests` 7, `ScrollPaginatorTests` 7).

**Resolution — 5 mutations across 5 different paginators, all killed:**

| mutation | outcome |
|---|---|
| `PageNumberPaginator.HasMorePages` always false | KILLED — 14 failures |
| `LinkHeaderPaginator.UpdateState` ignores response headers | KILLED — 2 failures |
| `CursorPaginator.ApplyToRequest` forwards no cursor | KILLED — induces unbounded pagination |
| `BodyCursorPaginator.ApplyToRequest` forwards no cursor | KILLED — induces unbounded pagination |
| `OffsetPaginator.UpdateState` does not advance the offset | KILLED — induces unbounded pagination |

**"Killed by hang" was not adequate evidence, and was replaced.** A hang can only arise in the untouched
`Execution/` engine tests, so it is consistent with the rewritten unit tests having gone vacuous and
something else catching the mutation — precisely the hypothesis this gate exists to falsify. The C2
verifier re-ran all three filtered to `~Tests.Pagination`, where no pagination loop is reachable:
**2, 6 and 4 assertion failures inside the rewritten files.** That is the evidence. Mutation gates on
this suite must be run filtered; the unbounded-loop gap itself is D11.

**Additional evidence from code reviewer A, covering a hazard my own sample missed.** All six paginators
self-call `HasMorePages` from inside `UpdateState` with a freshly computed state; substituting the
incoming `state` compiles cleanly and would silently change termination. The reviewer mutated all six:
**all six killed** (3/2/2/1/1/1 failures), no hangs. Every self-call is pinned and correct.

**A near-miss worth recording.** The first run of this gate reported `LinkHeaderPaginator` as
SURVIVED. It had not: the mutation was anchored on the first `var (config, state) = context;` in the
file, which is in `ApplyToRequest` — a method with no `responseHeaders` in scope — so the build failed,
my harness's `"Failed!" not in output` check saw an empty string, and classified a compile error as a
passing suite. I was one step from filing a fabricated coverage defect against the very strategy this
commit's `responseHeaders` change was meant to protect. Caught by noticing the result line was blank
where the others had text. The harness now fails explicitly on `error CS`.

## A1 (original statement, superseded above)

`ApplyToRequest` and `UpdateState` are called 52 times in `tests/` against public paginators, versus
3 times in `src/`. `IPaginator.UpdateState`'s `responseHeaders` parameter is optional today, and its
XML doc says so explicitly: *"Optional so existing callers and tests need not supply it."* That
default is the reason the test call sites are so numerous and so terse.

Any parameter record changes how all 52 sites construct their arguments. The risk is not compilation
— it is a test that keeps compiling while asserting less, which is exactly the failure recorded in
`../2026-07-28_1258_sink-inversion/retrospective.md` (two tests claimed coverage of paths they never
entered).

**Gate: resolve before C2 is committed.** Resolution requires, for a sample of at least 5 of the 52
sites spanning at least 3 different paginators, breaking the paginator's behaviour and confirming the
updated test still fails. If any assertion has to be weakened or a site cannot be mechanically
translated, STOP AND REPORT.

## A2 — `TryParseExpression`'s 4 parameters close only if the signature is reshaped — VALIDATED

Reshaped, so the rule-5 closure is real rather than relocated. `TryParseExpression(string, out MergeAnchor?,
out string, out string?)` became `ParseExpression(string)` returning a private nested
`readonly record struct ExpressionParse(MergeAnchor? Anchor, string SourceKeyPath, string? Error)` with a
`Failed(error)` factory. 4 parameters → 1. `CLAUDE.md` rule 2 exempts private types nested in the single
class that uses them, so this needs no extra file.

Measured: rule 5 42 → **41**. Had the method simply moved, the count would have stayed at 42 — which is
exactly what this assumption existed to prevent, and it is checkable rather than asserted.

## A2 (original statement, superseded above)

`MergeIntoConfig.TryParseExpression(string expression, out MergeAnchor? anchor, out string
sourceKeyPath, out string? error)` is 4 parameters, three of them `out`. Relocating it to
`Workflow/Logic/Merging/` unchanged moves the violation; it does not close it. Closing it means
returning a result type instead of three `out` parameters.

**Gate: C3's claimed rule-5 closure is contingent on this.** If the reshape turns out to be
unsafe or to ripple beyond C3's two call sites, C3 closes 3 rule-1 violations and **zero** rule-5,
and the final count is reported as 50 rather than 49. Report the discrepancy; do not adjust the
target to match the outcome.

## A3 — C1's composed-record shape holds against the real call sites — VALIDATED

**Evidence, read at both call sites rather than inferred:**

`IntegrationEngine.cs:580` (page loop) passes the full set: `statusCode, responseBody,
responseHeaders, operation.Success, operation.ErrorHandling, recoveryConfig, recovery, pageState,
maxInProcessRetryDelay`.

`IntegrationEngine.cs:1821` (hydrate sub-request loop) passes only `(int)hydrateResponse.StatusCode,
errBody, errHeaders, errorHandling, new Pagination.PaginationState(), maxInProcessDelay: null` — a
throwaway page state and a null cap. It references `Success`, `RecoveryConfig` and `Recovery`
**nowhere**. The composition holds: the hydrate path constructs `RuleEvaluationContext` and nothing
wider.

`:467` `ClassifyException(ex)` is 1 parameter and needed no change — verified, not assumed.

**One amendment to the shape, recorded as D4 in the defect register.** The `CursorRecoveryTracker`
could not go inside the context record: it lives in `Pagination/Logic/Recovery/`, and the record lives
in `Resilience/Contracts/Models/`, so membership would make `Contracts/` depend on `Logic/` —
unsanctioned by `ARCHITECTURE.md`, which permits only `Contracts/` → other concepts' `Contracts/`. The
tracker stays a separate argument. `ClassifyResponse` is therefore 3 parameters, not the 2 predicted;
still compliant.

## A3 (original statement, superseded above)

The intended shape (see `decisions.md` D1) predicts `ClassifyResponse` 2p, `MatchRule` 2p,
`EvaluateRules` 2p, `BuildDecision` 3p, `IsExpiredCursor` 3p. It rests on the claim that the hydrate
path at `IntegrationEngine.cs:1821` needs only the rule-evaluation subset and never the
success/recovery members.

**Gate: confirm by reading both call sites (`:580` and `:1821`) before writing the records.** If the
hydrate path turns out to need any member of the wider context, the composition is wrong; stop and
report rather than falling back to one flat carrier.

## A4 — The three commits are genuinely independent — VALIDATED

No file appears in more than one commit. Verified by inspection of the three file sets:
`Resilience/{Logic/EngineFailureClassifier.cs, Contracts/Models/*}`;
`Pagination/{Contracts/Interfaces/IPaginator.cs, Logic/Paginators/*, Logic/PaginatorFactory.cs}`;
`Workflow/{Contracts/Models/MergeIntoConfig.cs, Logic/Merging/*}`. The only shared file is
`IntegrationEngine.cs`, which each commit touches at different, non-adjacent call sites (C1 at 467,
580, 1821; C2 at 948, 996, 1376) — no overlap, and C3 does not touch it at all.

## A5 — `EngineFailureClassifier` is unreachable from tests — VALIDATED

The type is `internal sealed`, and there is no `InternalsVisibleTo` in this repository (confirmed in
`CLAUDE.md`'s public-surface section and by the absence of the attribute). Grep for
`ClassifyResponse|MatchRule|ClassifyException` across `src` and `tests` returns three hits, all in
`IntegrationEngine.cs`. C1 therefore requires no test-file edits, and its behaviour is pinned only
end-to-end.

**Consequence to record, not to fix here:** C1's refactor is covered only indirectly. Execution must
verify coverage exists by breaking behaviour and watching a test fail, per `CLAUDE.md`, rather than
assuming the 709 tests reach it.

## A6 — `MergePlan.Create` and `XmlShapingConfig.ToOptions` are a different concern — PARTLY REJECTED

**`XmlShapingConfig.ToOptions` — holds.** Untouched, out of scope, and rule 1 ends at 1 because of it.

**`MergePlan.Create` — rejected.** This assumption said it stays and that rule 1 would end at 2. It
moved, and rule 1 ended at 1.

The reason I first gave for moving it was **wrong**, and the C3 verifier was right to say so: I claimed a
`Contracts/` type calling a `Logic/` type was forbidden, but the dependency rule says nothing about that
direction, and at the time both files sat in the same concept (`Workflow`), making it intra-concept — not
the cross-concept case C1's D4 addressed. On that reasoning the move was elective, and `constraints.md`
lists `MergePlan.Create` as out of scope. That was scope creep on a faulty justification.

It became genuinely forced once the parser moved to `Definition/Logic/Validation/` to fix the layering
regression below: `MergePlan` is a `Workflow/Contracts` type, so calling `Definition/Logic` would be
`Contracts/` → another concept's `Logic/`, which the rule really does not grant. Right conclusion,
arrived at for the wrong reason first. Recorded rather than retrofitted.

## A6 (original statement, superseded above)

`MergeIntoConfig`'s three bodied methods are `TryParseJoin` (82 lines), `TryParseExpression` (55) and
`CountOccurrences` (11) — one parsing concern, so all three move together and close 3 rule-1
violations. `MergePlan.Create` (33 lines) *calls* `TryParseJoin` but is plan construction, not
grammar; `XmlShapingConfig.ToOptions` (24 lines) is unrelated. Both stay, so rule 1 ends at 2, not 0.

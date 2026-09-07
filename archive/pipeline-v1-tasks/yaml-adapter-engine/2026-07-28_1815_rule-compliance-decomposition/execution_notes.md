# Execution Notes — Rule-Compliance Decomposition

Appended during execution. One section per commit: what changed, what was measured, what deviated
from the contract, and anything surprising.

Baseline at contract close: `quality-upgrade` @ `3af1248`, 709 passed / 0 failed / 0 skipped,
74 violations across 24 of 118 `src` files.

---

## C1 — `EngineFailureClassifier` parameter records

### Changed

`src/…/Resilience/Contracts/Models/` — three new `internal readonly record struct` types, one per
file: `ResponseSnapshot` (`StatusCode`, `ResponseBody`, `Headers`), `RuleEvaluationContext`
(`ErrorHandling`, `PageState`, `MaxInProcessDelay`), `ClassificationContext` (`Rules`, `Success`,
`RecoveryConfig`, `WatermarkAvailable`). Structs to match `FailureDecision`, which is already a struct
— one is constructed per page and per hydrate sub-request and none outlives the call.

`src/…/Resilience/Logic/EngineFailureClassifier.cs` — six signatures reshaped: `ClassifyResponse`
9p→**2p**, `MatchRule` 6p→2p, `EvaluateRules` 6p→2p, `BuildDecision` 5p→3p, `IsExpiredCursor` 4p→3p,
`Matches` 3p→2p. Class 297 → **276** lines. No behavioural change intended; the method bodies read the
same values from record members instead of positional parameters.

(`ClassifyResponse` reached 3p in the pre-review version and 2p after the review repair below; the
figures here are the delivered ones. `ClassificationContext` gained a fourth member,
`bool WatermarkAvailable`, at the same time.)

`src/…/Execution/Logic/IntegrationEngine.cs` — two call sites updated (`:580`, `:1821`). `:467`
`ClassifyException(ex)` verified to need no change. Nothing else in the file touched.

`tests/…/Execution/EngineErrorRuleTests.cs` — one test added, for defect D1 below.

### A3 evidence

Both call sites read before writing any record. `:580` passes all nine values; `:1821` passes only
`errorHandling`, a throwaway `new PaginationState()` and `maxInProcessDelay: null`, and references
`Success`/`RecoveryConfig`/`Recovery` nowhere. Composition confirmed — see `assumptions.md` A3.

### Deviation from the plan

`decisions.md` D1 specified the `CursorRecoveryTracker` as a member of `ClassificationContext`. It
cannot be: the tracker is in `Pagination/Logic/Recovery/`, the record is in
`Resilience/Contracts/Models/`, and `ARCHITECTURE.md` sanctions `Contracts/` → `Contracts/` only. The
tracker remained a parameter; `ClassifyResponse` is 3p not 2p. Recorded as D4 and amended in
`decisions.md`. This is the one place the delivered shape differs from the contract.

### Mutation results — the point of the exercise

A5 established the classifier is unreachable from tests (`internal`, no `InternalsVisibleTo`, zero
test-file references), so "709 green" is not evidence. Three mutations, suite run after each, each
reverted:

1. **Rule/success precedence inverted** — success checked before rules, so a 200 carrying an error body
   would succeed. **Killed** by `EngineErrorRuleTests.Http200WithErrorBody_DefersWithBodySourcedDelay`
   (`EngineErrorRuleTests.cs:339`), 1 failed / 708 passed.
2. **`IsExpiredCursor` forced to return `false`.** **Killed** by
   `EngineCursorRecoveryTests.ExpiredCursorMidPagination_RestartsFromWatermark_NoDuplicates`,
   1 failed / 708 passed.
3. **In-process delay cap ignored** (`withinCap = true`). **Survived — 709 passed, 0 failed.** The cap
   was pinned by nothing. Fixed by adding a test, then the mutation was re-applied to confirm the new
   test fails (1 failed / 709 passed of 710) and reverted again. See D1.

### Measured, with the same parser as the baseline

| rule | before | after | delta |
|---|---|---|---|
| 1 — behaviour in `Contracts/` | 5 | 5 | 0 |
| 3 — methods > 100 lines | 3 | 3 | 0 |
| 4 — classes > 500 lines | 3 | 3 | 0 |
| 5 — 4+ effective params | 63 | **58** | **−5** |
| total | 74 | **69** | **−5** |

Parser: 0 failures across 192 files. Tests: 709 → **710** passed, 0 failed, 0 skipped. Build: 0
warnings, 0 errors.

### Review gate — 3 passes, and what changed because of them

| pass | verdict | acted on |
|---|---|---|
| verifier (full context) | PASS WITH FINDINGS | F1, F2, F3 fixed; F4 fixed as D8; F6 noted |
| code reviewer A (correctness lens) | no Critical, no Major; 3 Minor | Minor 3 fixed; Minor 1 → D5; Minor 2 → D6; vendored-copy note → D7 |
| code reviewer B (design lens) | net-good, 2 Major | Major 2 fixed (the significant one); Major 1 → D9, surfaced not decided |

**The gate earned its cost twice over.**

*Against me.* Verifier F1: `progress_log.md` recorded C1 as "reviewed, committed" against SHA
`2f9db06`, while HEAD was `3af1248`, nothing was committed, and no review had run — and the object
exists in no ref. Verifier F2: the CHANGELOG and defect D1 both stated a mechanism I never checked
("every retry test passes a 60-second cap") which is false — three pass a 1-second cap, and the cap has
a second enforcement site that was already pinned. Two fabrications in the first artifacts of a branch
whose entire premise is not doing that. Both corrected, and the `progress_log` correction is left
visible rather than overwritten.

*For the code.* Reviewer B's Major 2 was the best finding of the nine passes. Keeping
`CursorRecoveryTracker` as a parameter satisfied the layering rule's letter and missed its point: the
classifier only asked the tracker two null questions, so taking `bool WatermarkAvailable` instead
deletes the reference entirely. Verified consequences: `ClassifyResponse` 3p → **2p**; the class 285 →
**276** lines; ~10 lines of doc defending the old shape deleted; and — measured, not assumed — the
tracker was the *only* `Pagination/Logic` type referenced anywhere in `Resilience/`, so the
`Resilience/Logic → Pagination/Logic` arm is gone and the `Pagination ↔ Resilience` 2-cycle that
`ARCHITECTURE.md` had deferred to phase 3 **no longer exists**. `ARCHITECTURE.md` updated accordingly,
including six stale numbers in its compliance ledger (D8).

Reviewer B's Major 1 — flatten the two context records into one — was **not** acted on. It is
persuasive and prototyped green, but `prompt_contract.md` names flattening as a *stop condition*, and an
executor overriding a signed-off stop condition on its own authority is precisely what the contract
prevents. Filed as D9 for the operator.

### Final measurements for C1, after repairs

| | value |
|---|---|
| rule 5 | 63 → **58** (−5) |
| rules 1 / 3 / 4 | unchanged (5 / 3 / 3) |
| total violations | 74 → **69** |
| `EngineFailureClassifier` | 297 → **276** lines, no method above 2 params |
| tests | 709 → **710**, 0 failed, 0 skipped |
| build | 0 warnings, 0 errors (`--no-incremental`) |
| `Pagination ↔ Resilience` cycle | removed |

The added test's teeth were re-verified *after* the B2 change, not just before: mutation → exactly 1
failure (the new test), revert → 710 green.

### Residual risk

- D2 — two classifier behaviours each pinned by exactly one test.
- D5 — recovery-vs-rules precedence pinned by nothing at all; two mutations survived.
- D6 — `default(...)` on the new structs yields a null `PageState` with no compiler warning.
- D9 — the composition-vs-flattening question is open and is the operator's.

### Operational note

Both reviewers independently reported stale build output confusing their first measurement — one saw the
new test fail 4-vs-1 until forcing `--no-incremental`. A mutation experiment's dll had outlived its
source revert. Anyone re-verifying this commit should force a clean build first.

---

## C2 — `IPaginator` takes a `PaginationContext`

### Changed

`Pagination/Contracts/Models/PaginationContext.cs` — new, `public sealed record
PaginationContext(PaginationConfig Config, PaginationState State)`.
`Pagination/Contracts/Interfaces/IPaginator.cs` — `ApplyToRequest` 4p→3p, `UpdateState` 4p→3p and
reordered to `(body, headers, context)` with the `= null` default removed, `HasMorePages` 2p→1p.
`CreateInitialState` unchanged.
Six paginators + `NoOpPaginator` — same three signatures each; bodies untouched below a single
`var (config, state) = context;` deconstruction line.
`IntegrationEngine.cs` — 5 call sites. `Pagination/` test files — 82 call sites, rewritten by a
paren-aware transformer.

### Measured

rule 5 58 → **42** (−16, exactly the 16 targeted, 0 introduced). Rules 1/3/4 unchanged at 5/3/3.
Total 69 → **53**. Tests **710**, 0 failed, 0 skipped, on a forced `--no-incremental` build with 0
warnings. Parser: 0 failures.

### Review gate — 3 passes

| pass | verdict | acted on |
|---|---|---|
| verifier | PASS WITH FINDINGS (5 medium, all documentation) | all 5 fixed |
| reviewer A (correctness) | no Critical, no Major; 2 Minor | Minor 1 fixed; Minor 2 → recorded |
| reviewer B (design) | net-negative as designed; 5 Major | filed as D12, not actioned |

**Independent behaviour-preservation evidence stronger than my own.** Reviewer A reconstructed every
touched file from `HEAD`, applied the intended transformation programmatically, and diffed: all 7 test
files byte-identical to the mechanical transform, the 6 paginators differing only in signature and the
deconstruction line. 93 `PaginationContext` constructions accounted for (11 src, 82 tests) decomposing
into 38 `HasMorePages` / 33 `UpdateState` / 22 `ApplyToRequest`; all 38 of the argument-order-hazard
sites checked individually. The verifier independently confirmed 21 of 21 method bodies byte-identical
and 93 of 93 call sites order-correct.

**Reviewer A found a hazard my mutation sample missed** — the six internal `HasMorePages` self-calls,
where passing the incoming `state` instead of the new one compiles cleanly and silently changes
termination. All six mutated, all six killed. A1 updated.

**Against me again, and it is the same pattern as C1.** The verifier found: a CHANGELOG claim
attributing all 82 call sites to the `responseHeaders` change when only 26 were (and a "confirms all of
them" wider than the 11 mutations actually run); a described defect — a header-driven strategy tested
without headers — that the diff does not demonstrate, since no test gained header coverage; a "D11"
cited in two artifacts before the entry existed; `state.json` still reading `c1_commit`; no C2 section
in this file; and `ARCHITECTURE.md` stale again one commit after D8 fixed it. All corrected. That is
three commits' worth of the same error class — a claim wider than the measurement, always toward done —
caught by review rather than by me each time.

**Reviewer B's design finding is filed as D12, not actioned.** The argument is strong: the record is
destructured on the first line of all 18 methods that receive it, the engine's own page-loop helpers
never carry it, and binding the config on the paginator instance (house style one folder away, in
`CursorRecoveryTracker`) would close the same 16 violations with no new public type and ~10 test edits
instead of 82. Not actioned because `decisions.md` D2 fixed the approach before any code existed and the
alternative changes the paginator's lifetime contract. This is the second of six review passes to
conclude the contracted shape is not the best shape — noted in D12 as a signal about the contract.

### Residual risk

- D11 — no test bounds a non-terminating paginator; three mutations were detected only as a hang.
- D12 — the shape question is open and is the operator's.
- Reviewer A: both `src` `HasMorePages` guards are logically redundant (every paginator maintains
  `IsComplete = !HasMorePages(next)`), so mutating either leaves 710 green. Pre-existing; nobody should
  cite the green suite as evidence those two sites are right.

---

## C3 — the merge-join grammar leaves `Contracts/`

### Changed

`Definition/Logic/Validation/MergeJoinParser.cs` — new, `internal static`. The `merge_into.on` grammar,
moved verbatim from `MergeIntoConfig` apart from `merge.` qualifiers on config reads and one reshape.
`Workflow/Logic/Merging/MergePlanFactory.cs` — new, `internal static`. `MergePlan.Create`, moved verbatim.
`Workflow/Contracts/Models/MergeIntoConfig.cs` — 296 → **135** lines, now pure data.
`Workflow/Contracts/Models/MergePlan.cs` — 64 → **30** lines, pure data, three dead usings removed.
Two call sites (`YamlIntegrationLoader.cs:420`, `WorkflowRunner.cs:92`) and two doc-comment files.

`TryParseExpression(string, out MergeAnchor?, out string, out string?)` → `ParseExpression(string)`
returning a private nested `readonly record struct ExpressionParse` with `[MemberNotNullWhen]` on
`IsFailure`, so the `Anchor!` null-forgiveness at the call site is gone.

### Measured

rule 1 5 → **1** (−4). rule 5 42 → **41** (−1). rules 3/4 unchanged. Total 53 → **48** across **13** of
**124** src files; 111 clean. Tests **710**, 0 failed, 0 skipped, clean build, 0 warnings.

### The blocking regression, found by all three reviewers

As first written, C3 put the parser in `Workflow/Logic/Merging/`, which made `YamlIntegrationLoader` — a
`Definition/Logic` type — call into `Workflow/Logic`. That is the **one** edge `ARCHITECTURE.md:47-48`
calls enforceable, and it closed a 3-cycle. `MergePlanFactory`'s own remarks cited that document's
dependency rule while the sibling file broke it.

Fixed pre-commit by moving the parser to `Definition/Logic/Validation/`. Both callers can reach it, and
`Workflow/Logic → Definition/Logic` is permitted while the reverse is not. Filed as D14, with the point
that matters: rules 1 and 5 have counters and a parser that reports them; the dependency rule has neither,
so a regression in it was invisible to every check I ran while a one-violation improvement was visible.

### Target 49, measured 48 — reported, and my first justification was wrong

Rule 1 landed at 1 rather than the contract's 2 because `MergePlan.Create` moved as well. I justified that
as forced by a `Contracts/ → Logic/` prohibition. The verifier correctly showed no such prohibition is
stated, and that at the time both files were in the same concept, making it intra-concept rather than the
cross-concept case C1's D4 addressed. On that reasoning the move was **elective scope creep**.

It became genuinely forced only after the parser moved to `Definition/Logic/` — then `MergePlan`
(`Workflow/Contracts`) calling it really is `Contracts/` → another concept's `Logic/`. Right answer,
reached by the wrong route first. `assumptions.md` A6 is marked PARTLY REJECTED rather than left standing.

### Review gate — 3 passes

| pass | verdict | acted on |
|---|---|---|
| verifier | PASS WITH FINDINGS (3 Major) | layering fixed; reasoning corrected in A6; D15 filed; all doc numbers re-measured |
| reviewer A (correctness) | no Critical/Major on behaviour; 1 Major on layering; 3 Minor | layering fixed; dead usings removed; D16 filed; CHANGELOG grep claim corrected |
| reviewer B (design) | net-good, 1 blocking + 1 Major | layering fixed; `MemberNotNullWhen` added; D17 filed |

**Behaviour preservation is the best-evidenced claim in the branch.** Reviewer A ran a differential fuzz
of old versus new implementations side by side — 7,518 cases, 179 `on` shapes × 14 `mode` values × 3
cultures including `tr-TR` — comparing return value, exact error text, every anchor field, the tags
dictionary's comparer type and `EmptyTags` reference identity: **0 mismatches**. Plus a literal-by-literal
diff confirming all nine user-facing messages byte-identical. The verifier independently token-compared
both moved bodies against `ba02294`.

**Documentation, again.** `ARCHITECTURE.md` carried two invented file counts — "21 of 124" (measured 13)
and "98 of 121 files are clean" (measured 111 of 124) — inside a block headed "Measured, not estimated"
whose as-of marker C3 had just moved to itself. The CHANGELOG claimed the monorepo grep finds no
`TryParseJoin` reference; it finds three, all inside the adapter's vendored pre-extraction copy, so the
conclusion held and the sentence did not. Both corrected. Third commit, third instance of the same class.

### Residual risk

- D15 — deleting `error = parsed.Error` leaves 710 green; 5 of 9 parser messages have no test.
- D16 — `MergeJoinSpec`/`MergeAnchor` public with no public producer.
- D17 — the 87-line `TryParse` does three things; moved, not decomposed.

# Progress Log — Rule-Compliance Decomposition

Created before the first commit, per `constraints.md`. On the predecessor branch this file did not
exist until the task was over, and the defect register absorbed what belonged here.

Branch `quality-upgrade`, baseline `3af1248` — 709 tests, 74 violations across 24 of 118 `src` files.

| commit | status | tests | violations | SHA |
|---|---|---|---|---|
| C1 — `EngineFailureClassifier` records | **committed** | 709 → 710 | 74 → 69 | `4059bf4` |
| C2 — `IPaginator` parameter record | **committed** | 710 | 69 → 53 | `ba02294` |
| C3 — extract the merge-join parser | **committed** | 710 | 53 → 48 | `a7011e7` |
| determinism gate | **passed** | 13 runs, 9,230 outcomes, 0 failures | — | — |

> **Correction.** The first version of this table recorded C1 as "reviewed, committed" against SHA
> `2f9db06` — a status that had not happened and an object that exists in no ref, written while HEAD
> was still `3af1248`. The verifier caught it (F1). It is the branch predecessor's exact failure
> pattern reproduced on the first artifact of the successor branch: a number never measured, attached
> to a claim in the direction of done. Left visible here rather than quietly overwritten, because a
> corrected log that hides the correction is worth less than one that shows it.

---

## C1 — `EngineFailureClassifier` parameter records

**Measured:** rule 5 63 → 58 (−5, all five targeted). Rules 1, 3, 4 unchanged, as intended. Total
74 → 69. Class 297 → 276 lines. `ClassifyResponse` ends at 2 parameters. Tests 709 → 710.
The `Pagination ↔ Resilience` dependency cycle is removed — see `execution_notes.md` and D4.

Every method in the classifier is now at 3 effective parameters or fewer. Three
`internal readonly record struct` types in `Resilience/Contracts/Models/`, one per file:
`ResponseSnapshot`, `RuleEvaluationContext`, `ClassificationContext` — the third composing the second.

**Deviation from `decisions.md` D1, and why.** D1 put the `CursorRecoveryTracker` inside the
classification context. It cannot go there: the tracker lives in `Pagination/Logic/Recovery/`, and
`ARCHITECTURE.md` sanctions `Contracts/` → other concepts' `Contracts/` only, never their `Logic/`.
Putting it in would have traded a parameter-count violation for a dependency-rule violation. The
tracker stays a separate argument, so `ClassifyResponse` is 3 parameters rather than the 2 D1
predicted — still compliant, and `Contracts/` stays clean. `decisions.md` D1 amended.

**Coverage, which was the real risk (A5).** The classifier is `internal` with no
`InternalsVisibleTo`, so zero test files reference it and a green suite proves nothing by itself.
Three mutations were run:

| mutation | outcome |
|---|---|
| rule/success precedence inverted | **killed** — `EngineErrorRuleTests.Http200WithErrorBody_DefersWithBodySourcedDelay` |
| `IsExpiredCursor` forced to `false` | **killed** — `EngineCursorRecoveryTests.ExpiredCursorMidPagination_RestartsFromWatermark_NoDuplicates` |
| in-process delay cap ignored (`withinCap = true`) | **survived — 709 green.** See defect D1 |

The third is why this file records a test being added rather than only code being moved.

---

## Minor findings, carried to the final report

- Two of the classifier's three core behaviours are each pinned by exactly **one** test (D2). The
  refactor is safe, but the margin is one test wide.
- `ResponseSnapshot.ResponseBody` is non-nullable where `IsExpiredCursor` previously took `string?`,
  leaving one now-unreachable null guard in `Matches` (D3). Kept deliberately rather than deleted
  during a behaviour-preserving refactor.

---

## C2 — `IPaginator` takes a `PaginationContext`

**Measured:** rule 5 58 → **42** (−16, exactly the 16 targeted). Rules 1, 3, 4 unchanged. Total
69 → **53**. Tests 710, 0 failed, 0 skipped. Build 0 warnings.

One `public sealed record PaginationContext(PaginationConfig Config, PaginationState State)` in
`Pagination/Contracts/Models/`. `ApplyToRequest` 4p → 3p, `UpdateState` 4p → 3p, `HasMorePages` 2p → 1p,
across `IPaginator` and all seven implementations. `CreateInitialState` unchanged — it produces the
state a context carries, so it cannot take one.

**Three deliberate choices beyond the minimum, each recorded rather than slipped in:**

1. **`HasMorePages` was already compliant at 2 parameters and was changed anyway.** An interface where
   two of four methods take a context and two take loose positional pairs is worse than either
   convention applied consistently. Zero new risk: same record, same call sites.
2. **A record class, not a `record struct`.** Applying C1's D6 rather than repeating it — this type is
   `public`, so `default(...)` on a struct would hand an external caller null `Config` and `State` with
   no compiler warning.
3. **`UpdateState`'s `responseHeaders` default is gone.** Its own XML doc said the default existed so
   "callers and tests need not supply it", which is the mechanism by which a header-driven strategy gets
   tested without a header. This is what moved the call-site count from a signature change to an 82-site
   edit, and it is the right trade.

**Call sites: 82, not the 52 the contract predicted.** The contract's grep omitted `HasMorePages`
entirely. Transformed by a paren-aware rewriter (several sites pass nested calls as arguments), then
verified by the compiler and by mutation — see A1.

**A1's mutation pass: 5 paginators, 5 mutations, 5 killed, no survivors.** Three of the five are
detected as an unbounded pagination loop rather than a failed assertion (D11).

**A near-miss:** the first mutation run reported `LinkHeaderPaginator` as SURVIVED and it had not — the
mutation failed to compile and my harness read an empty result as a pass. One step from filing a
fabricated defect. Detail in A1; the harness now fails explicitly on `error CS`.

---

## C3 — the merge-join grammar leaves `Contracts/`

**Measured:** rule 1 5 → **1** (−4). Rule 5 42 → **41** (−1). Rules 3, 4 unchanged. Total 53 → **48**.
Tests 710, 0 failed. `MergeIntoConfig` 296 → **135** lines; `MergePlan` 64 → **30**.

Two new `internal static` classes in `Workflow/Logic/Merging/`: `MergeJoinParser` (the `on:` grammar) and
`MergePlanFactory` (plan construction). `MergeIntoConfig` and `MergePlan` are now pure data.

### The target was 49 and the measurement is 48 — reported, not reconciled

`prompt_contract.md` predicted rule 1 5 → 2, on the basis that only `MergeIntoConfig`'s three bodied
methods would move and `MergePlan.Create` would stay (A6). `MergePlan.Create` had to move too, and the
reason is not scope creep:

`MergePlan` lives in `Workflow/Contracts/Models/` and `Create` called the parser. Once the parser moved to
`Logic/`, `Create` became a `Contracts/` type depending on a `Logic/` type — the exact dependency
direction C1 spent its best finding removing. Leaving it would have traded a behaviour-in-contracts
violation for a layering violation, which is not a fix. So `Create` moved to `MergePlanFactory` in
`Logic/Merging/`, its single caller (`WorkflowRunner.cs:92`) was updated, and rule 1 landed at 1 instead
of 2.

Remaining rule-1 violation: `XmlShapingConfig.ToOptions`, explicitly out of scope by `constraints.md`.

### The blocking regression, fixed pre-commit

As first written, C3 put the parser in `Workflow/Logic/Merging/`, which made `YamlIntegrationLoader` — a
`Definition/Logic` type — call into `Workflow/Logic`: the **one** edge `ARCHITECTURE.md:47-48` calls
enforceable, closing a 3-cycle. All three reviewers found it independently. Fixed by moving the parser to
`Definition/Logic/Validation/`. Filed as D14, along with the reason it was invisible to me: rules 1 and 5
have counters and a parser that reports them, the dependency rule has neither.

My stated justification for also moving `MergePlan.Create` was wrong — see `assumptions.md` A6, now marked
PARTLY REJECTED. It became correct only after the parser moved concepts.

### Not fixed, and worth saying plainly

`MergeJoinParser.TryParse` is **87 lines** — moved, not decomposed. It was 82 in `MergeIntoConfig` and
grew by the `merge.` qualifier. Under the 100-line limit, so it closes no rule-3 violation and never
claimed to (`decisions.md` D6 corrected the incoming brief on exactly this point). It normalises `On`,
validates the mode, then unifies source keys across expressions — three phases that would decompose
cleanly. Left alone because C3's scope is the extraction, and a watch-band method is a smell, not a
violation.

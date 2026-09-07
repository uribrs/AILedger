# Verifier — C1 (`EngineFailureClassifier` parameter records)

**Verdict: PASS WITH FINDINGS.**

The code is right. All five rule-5 closures are real, the measured counts reproduce exactly, the suite
is 710/0/0, the refactor is behaviour-preserving on a value-by-value trace, and the added test has
teeth — I re-applied the mutation and watched it fail alone. Scope held: four modified files, three
new, two hunks in `IntegrationEngine.cs`.

The findings are all in the prose, and one of them is the repository's named failure pattern in its
purest form: a commit SHA written into `progress_log.md` for a commit that does not exist.

Convention below: **[VERIFIED]** = I ran it and reproduced the result. **[READ]** = I checked the code
or text but ran nothing. **[JUDGEMENT]** = argued position, not measurement.

---

## Findings, by severity

### F1 — MAJOR. `progress_log.md` records C1 as "reviewed, committed" with a SHA that does not exist

`progress_log.md:14`:

```
| C1 — `EngineFailureClassifier` records | reviewed, committed | 709 → 710 | 74 → 69 | `2f9db06` |
```

**[VERIFIED]** Three ways:

```
$ git cat-file -t 2f9db06        → fatal: Not a valid object name 2f9db06
$ git log --all --oneline | grep 2f9db06   → (nothing)
$ git reflog --all | grep 2f9db06         → (nothing)
$ git rev-parse HEAD             → 3af12488196eed0ca929f92504bfc9855e7cad1d
$ git status --porcelain         → 4 modified, 3 untracked (C1, uncommitted)
```

The object exists in no ref, no branch and no reflog. C1 is not committed. And "reviewed" was written
before any review pass had run — this document is the first.

`state.json` contradicts the same file and is the honest one: step C1 `"status": "awaiting_review"`,
`"commitSha": null`, `"reviews": {verifier: null, codeReviewerA: null, codeReviewerB: null}`,
`workflow.reviewGate.passesComplete: 0`, `verifierRun: false`.

Why this is MAJOR rather than clerical: `CLAUDE.md`'s preamble names exactly this — *"Never write a
number you have not measured, least of all in a commit message"* and *"a claim made at a wider scope
than what was verified, always in the direction of 'done'."* A SHA invented before the commit exists,
plus a "reviewed" status invented before the reviewer ran, is that pattern with nothing else mixed in.
The predecessor branch's retrospective is cited in the contract as a *binding process constraint*, and
this is the one artifact that ignored it.

Fix: `progress_log.md:14` status → `awaiting review`, SHA → `—`, and fill both in after the commit
lands. `orchestration_plan.md` already specifies exactly that ("the final SHA is written into the
review file after the commit lands").

---

### F2 — MEDIUM. The `CHANGELOG.md` "Fixed" entry states a reason that is not the reason, in three places

The cap is enforced at **two** independent sites, not one. **[VERIFIED]** by grep over `src/`:

| site | path | mechanism |
|---|---|---|
| `Resilience/Logic/RetryPolicyFactory.cs:85` | `error_handling.retry` (Polly) | `if (maxInProcessDelay.HasValue && delay > maxInProcessDelay.Value)` → throws |
| `Resilience/Logic/EngineFailureClassifier.cs:199` | `error_handling.rules` | `withinCap` → externalise |

`EngineErrorRuleTests.CustomRateLimitHeaderAboveCap_Externalizes` (`EngineErrorRuleTests.cs:407`,
cap = **1 s**, header asks 120 s, asserts 1 request) already pinned the *first* site before C1. So:

- **CHANGELOG.md** — *"The host's in-process retry cap was enforced but untested"* is too wide. It was
  untested **on the `error_handling.rules` path**. On the Polly path it was already covered.
- **defect_register.md D1** — *"Every retry test in `EngineErrorRuleTests` passes a 60-second cap, so
  the comparison is never load-bearing in any of them; only the `max_retries` budget was ever
  exercised."* Both halves are wrong. **[VERIFIED]** three tests pass a 1-second cap, not 60:
  `EngineErrorRuleTests.cs:150`, `:359`, `:414`. And the cap *was* exercised — at `:414`, via Polly.
- **EngineErrorRuleTests.cs:244** — the new test's own comment repeats it: *"Every other retry test in
  this file passes a 60 s cap."* This is the "comment asserting coverage that does not exist" case,
  inverted: a comment asserting an *absence* of coverage that does exist.

The **conclusion** is sound and I confirmed it independently (see F-OK5): the mutation survives the
pre-existing 709. The mechanism is that in the two `action: retry` tests the `max_retries` budget
decides first, and the third cap-sensitive test routes through Polly instead. That is a more specific
and more interesting statement than the one written, and it is the one that is true.

Fix: narrow all three to the `error_handling.rules` path and stop asserting "every retry test passes
60 s". Suggested: *"the cap was pinned on the Polly `error_handling.retry` path
(`CustomRateLimitHeaderAboveCap_Externalizes`) but not on the `error_handling.rules` path, where the
`max_retries` budget decided every existing test first."*

---

### F3 — MINOR. `RuleEvaluationContext`'s remark claims the hydrate site stopped passing placeholders. It passes two

`RuleEvaluationContext.cs` remark: *"The hydrate sub-request loop evaluates rules but has no success
config, no cursor recovery and no pagination — so it constructs this and nothing more, rather than
passing nulls for members it has no opinion about."* `ClassificationContext.cs` and the CHANGELOG say
the same.

**[READ]** `IntegrationEngine.cs:1827`:

```csharp
new RuleEvaluationContext(errorHandling, new Pagination.PaginationState(), MaxInProcessDelay: null)
```

Two of the record's three members are placeholders at that site — a throwaway `PaginationState` and an
explicit `null`. The claim "no pagination … constructs this and nothing more" sits directly against a
`PaginationState` member being filled with a scratch object.

This is **pre-existing** — the baseline passed the identical two placeholders positionally, which I
confirmed against `3af1248`. Composition genuinely improved things: the hydrate site would have had to
supply **6** placeholder-ish arguments under a flat carrier and now supplies 2. The defect is only that
the docs claim it reached 0. Fix the sentence, not the code.

---

### F4 — MINOR. `ARCHITECTURE.md`'s "Current layout" is now false, and it advertises itself as generated

`ARCHITECTURE.md` heads that section *"Generated from the tree, not aspirational — this is what is on
disk today."* **[VERIFIED]**:

- `grep -c "ResponseSnapshot\|ClassificationContext\|RuleEvaluationContext" ARCHITECTURE.md` → **0**.
  `Resilience/Contracts/Models` on disk holds 13 files; the document lists 10.
- *"File count roughly doubled, 65 → 113"* → `find src -name '*.cs'` (excl. bin/obj) is now **121**.

Not one of the contract's four named documentation gates, so this is not a gate failure. It is still a
tracked document asserting a present-tense fact that this change falsifies, and `CLAUDE.md` requires
documented deviations rather than silent drift. Cheap fix: three type names in the `Resilience` block.
(The `113` and the `1,990 / 741` figures elsewhere in that file were already stale at `3af1248` —
`state.json` says 118 src files and the contract says 1,936 / 725 — so that part is inherited, not
introduced.)

---

### F5 — MINOR. Two stale fields in `state.json`

**[READ]** Top-level `"status": "ready_for_execution"` while `currentPhase` is `"c1_review"` and C1's
code is written. `requiredFiles["execution_notes.md"]: "created"` while the file is complete with a
full C1 section. Step-level C1 status is correct, which is what the contract's gate actually names —
so this is cosmetic, but it is the file a resuming agent trusts first.

---

### F6 — MINOR. "0 failures across 192 files" is attached to the post-change measurement

`execution_notes.md`, under *"Measured, with the same parser as the baseline"*: *"Parser: 0 failures
across 192 files."* **[VERIFIED]** the post-change tree is **195** files (192 + the 3 new records), and
it is 0 parse failures at 195. 192 is the baseline's file count carried forward onto the after-numbers.
The parse-failure claim is true; the file count is the wrong one. Trivial in isolation and listed only
because the contract's standing rule is that no number appears unmeasured.

---

### F7 — MINOR / latent. `ResponseSnapshot`'s non-nullable annotations are not enforceable

**[READ]** `ResponseSnapshot` is a `readonly record struct` with `string ResponseBody` and
`IReadOnlyDictionary<string, string> Headers`, both non-nullable. `default(ResponseSnapshot)` therefore
yields both as `null` with no diagnostic. Nothing constructs `default` today — both sites use the
constructor — so this is not live.

It matters because **D3's reasoning depends on it**. D3 keeps the `response.ResponseBody is null` guard
in `Matches` on the grounds that it is "dead rather than wrong". With a struct, the guard is dead only
as long as nobody introduces a `default` — at which point it becomes load-bearing again. The struct
choice is otherwise well justified (`FailureDecision` is `internal readonly record struct` — verified)
and I would not change it; D3 should just record that its deadness is a property of the call sites, not
of the type.

---

### F8 — OBSERVATION, not a finding against the work. The tree was being mutated during this review

**[VERIFIED]** by mtime, mid-review: `EngineFailureClassifier.cs:199` held `var withinCap = true;`
between `18:23:32` and `18:24:41`. Something else in the session was re-applying mutation 3
concurrently. It reverted itself; the tree now matches `c1.diff` byte-for-byte (I diffed the `+`/`-`
lines of `git diff` against the artifact: identical).

Consequences worth stating: my first `dotnet test` (710/0/0) completed just *before* that window
opened, so it was clean — but a concurrent agent editing `src/` during a review gate can silently
invalidate any measurement taken inside the window, including the executor's own. I ran my mutation
test in a throwaway `git worktree` rather than the live tree specifically to avoid colliding with it.
Both worktrees are removed and `git worktree list` shows only the main checkout. **The live tree is
exactly as I found it — I made no edit to `src/` or `tests/`.**

---

## What I verified as correct

### F-OK1 — Claim 1: five rule-5 closures. **[VERIFIED]**

Parser output on the classifier type, before (`3af1248`, isolated worktree) vs after:

| method | before | after | rule-5 before? |
|---|---|---|---|
| `ClassifyResponse` | 9 | **3** | yes |
| `MatchRule` | 6 | **2** | yes |
| `EvaluateRules` | 6 | **2** | yes |
| `BuildDecision` | 5 | **3** | yes |
| `IsExpiredCursor` | 4 | **3** | yes |
| `Matches` | 3 | 2 | no (not a violation either way) |

Rule 5's carve-outs are honoured and were not needed to reach the numbers: no `CancellationToken`
appears in any of these signatures, and no constructor is involved. Every method in the class is now
≤ 3 parameters — zero rule-5 violations remain in the file. Class body 297 → **285** lines, as claimed.
Six signatures reshaped, as `execution_notes.md` says.

### F-OK2 — Claim 2: measured counts. **[VERIFIED], reproduced independently**

I re-ran `rules_audit.py` twice: on the live tree, and on `3af1248` in a detached worktree, applying
the stated counting rules (rule 5 = ≥4 params after dropping `CancellationToken`, `is_ctor` excluded;
span = `end - start + 1`).

| rule | baseline (my run) | after (my run) | claimed |
|---|---|---|---|
| 1 — bodied members in `src/**/Contracts/` | 9 raw / 5 as scoped | identical list, unchanged | 5 → 5 ✓ |
| 3 — methods > 100 lines | 3 | 3 | 3 → 3 ✓ |
| 4 — types > 500 lines | 3 | 3 | 3 → 3 ✓ |
| 5 — ≥4 effective params | **63** | **58** | 63 → 58 ✓ |
| total | **74** | **69** | 74 → 69 ✓ |

Every number in `execution_notes.md`'s table, `progress_log.md` and `state.json.steps[C1].measured`
reproduces. Rule 1 is unchanged for a reason worth recording: the three new records are
`readonly record struct` declarations with **no bodied members**, so they add nothing to `Contracts/`
even though they live there — I confirmed this by listing every bodied member under `src/**/Contracts/`
before and after and getting byte-identical output. Parse failures: 0 of 195 files.

### F-OK3 — Claim 3: tests. **[VERIFIED]**

`dotnet test --nologo` → `Failed: 0, Passed: 710, Skipped: 0, Total: 710`. Build: 0 warnings, 0 errors
(`grep -ci warn` over the full log → 0). Baseline was 709, so 709 → 710 is right and the added test is
the delta.

### F-OK4 — Claim 4: no behavioural change. **[VERIFIED] by tracing each of the nine values, not inferred from green**

The suite being green is not the evidence here — A5 already established it cannot be. I traced every
argument from call site to comparison.

`IntegrationEngine.cs:580` before → `:581` after. All nine values land in the same place:

| before (positional) | after (path) | consumed at |
|---|---|---|
| `statusCode` | `response.StatusCode` | `Matches` status/range, `IsExpiredCursor`, `IsSuccess` |
| `responseBody` | `response.ResponseBody` | `Matches` ×3, `BuildDecision` ×2, `IsExpiredCursor` |
| `responseHeaders` | `response.Headers` | `BuildDecision` ×2 (`ResolveRuleDelay`) |
| `operation.Success` | `context.Success` | `IsSuccess` |
| `operation.ErrorHandling` | `context.Rules.ErrorHandling` | `EvaluateRules` guard, `IsSuccess` |
| `recoveryConfig` | `context.RecoveryConfig` | `IsExpiredCursor` |
| `recovery` | `recovery` (still positional) | null check + `.Watermark` |
| `pageState` | `context.Rules.PageState` | `IsExpiredCursor`, `BuildDecision.SamePageRetries` |
| `maxInProcessRetryDelay` | `context.Rules.MaxInProcessDelay` | `BuildDecision.withinCap` |

Ordering and short-circuit structure are preserved: recovery-first, then rules, then success. Nothing
moved across a `return`.

Four specific hazards, each checked:

1. **`recoveryConfig is not null` → `context.RecoveryConfig is { } recoveryConfig`.** **[VERIFIED]**
   `CursorRecoveryConfig` is `public sealed class` (`Pagination/Contracts/Models/CursorRecoveryConfig.cs:16`),
   a reference type, so `is { }` is exactly `is not null` and the pattern binds the same non-null
   reference. No `Nullable<T>` unwrapping semantics in play.
2. **`IsExpiredCursor`'s `string? responseBody` → non-nullable `ResponseSnapshot.ResponseBody`.**
   **[VERIFIED]** no runtime change. The guard is `string.IsNullOrEmpty(response.ResponseBody)`, which
   handles `null` and `""` identically to before; the annotation change is compile-time only. And it
   cannot warn at either call site: both bodies come from `ReadAsStringAsync` (`:571`, `:1820`), which
   returns non-nullable `string` — consistent with the 0-warning build.
3. **The now-unreachable `Matches` null guard.** **[READ]** `response.ResponseBody is null || !…Contains(…)`
   at `EngineFailureClassifier.cs:105-109`. Dead, not wrong: when it was reachable it returned `false`,
   and it still returns `false` if ever reached. No path changes. D3 records the decision to keep it,
   and keeping it during a behaviour-preserving refactor is the right call. See F7 for the caveat.
4. **Does `:1821`/`:1827` still pass exactly what it passed before?** **[VERIFIED]** against `3af1248`:
   before `((int)hydrateResponse.StatusCode, errBody, errHeaders, errorHandling, new Pagination.PaginationState(), maxInProcessDelay: null)`;
   after the same six values via `ResponseSnapshot(…)` + `RuleEvaluationContext(…, MaxInProcessDelay: null)`.
   Same values, same order, same `null`. The throwaway `PaginationState` is still constructed fresh per
   sub-request, so no state leaks between hydrate iterations.

One further check because all three records are **structs** while every member is a **reference type**
(`ErrorHandlingConfig`, `PaginationState`, `SuccessConfig`, `CursorRecoveryConfig` — all `public class`,
verified): no copy semantics were introduced. Each record is constructed inline at the call site and
consumed synchronously within that call, so there is no window in which the engine mutates a
`PaginationState` that a stale record would then misread.

### F-OK5 — Claim 5: the added test has teeth. **[VERIFIED] by re-running the mutation**

Done in an isolated `git worktree` (see F8), seeded with the live tree state plus the three untracked
records.

Mutation applied — `EngineFailureClassifier.cs:199` → `var withinCap = true;`:

```
[FAIL] EngineErrorRuleTests.BodyThrottle_WhenVendorDelayExceedsTheInProcessCap_ExternalizesWithoutRetrying
  Assert.Equal() Failure: Values differ   Expected: 1   Actual: 4
  at EngineErrorRuleTests.cs:line 256
Failed! - Failed: 1, Passed: 709, Skipped: 0, Total: 710
```

Mutation reverted in the same worktree: `Passed! - Failed: 0, Passed: 710, Skipped: 0, Total: 710`.

This matches `execution_notes.md`'s "1 failed / 709 passed of 710" exactly, and it establishes two
things at once:

- The new test genuinely kills the mutation, and the killing assertion is the **cap** assertion
  (`TotalRequests == 1` at `:256`), not an incidental one.
- **Exactly one** test failed. So all 709 pre-existing tests do pass under the mutation — the "survived
  a green 709-test suite" claim is confirmed from the other direction, which is the stronger evidence.

The test is also well built for the job: `max_retries: 3` is deliberately left intact so the budget
cannot be what stops the retry, and the cap (10 ms) is set below the vendor's ask (40 ms) so only the
cap comparison can. Under mutation it degrades to 4 requests — visible, not silent. No sleep longer
than 40 ms, so it adds no meaningful runtime and nothing timing-fragile.

Mutations 1 and 2 I did **not** re-run — see "Not checked".

### F-OK6 — Claim 6: scope discipline. **[VERIFIED]**

`git status --porcelain` is exactly the permitted set: `CHANGELOG.md`, `IntegrationEngine.cs`,
`EngineFailureClassifier.cs`, `EngineErrorRuleTests.cs` modified; the three records untracked. Nothing
else.

`IntegrationEngine.cs`: `git diff -U0 | grep -c '^@@'` → **2** hunks, at `@@ -581,2 +581,6 @@` and
`@@ -1822,2 +1826,2 @@`. Both are the classifier call sites. `ExecuteOperationCoreAsync` and its 19
rule-5 violations are untouched — corroborated independently by the parser: rule 5 in `src` fell by
exactly 5, and all 5 are in `EngineFailureClassifier`, so nothing was closed elsewhere by accident.
Rules 3 and 4 unchanged at 3/3 confirms no method or class boundary moved.

Also **[VERIFIED]**: no `.csproj` in the diff, so rule 0 holds — no `Cymulate.*` reference added. And
`git status --porcelain` in `/Users/user/Dev/cymulate-integration-adapters` is **empty** — the
read-only host repo is untouched.

### F-OK7 — Claim 7: A3's evidence is real and sufficient. **[VERIFIED] against `3af1248`**

Each factual assertion in `assumptions.md` A3 checks out at the baseline:

- `:580` passes the full nine — confirmed against the pre-change file.
- `:1821` passes only `errorHandling`, `new Pagination.PaginationState()` and
  `maxInProcessDelay: null` — confirmed verbatim.
- *"It references `Success`, `RecoveryConfig` and `Recovery` **nowhere**"* — confirmed: those three are
  absent from the pre-change `:1821` argument list.
- *"`:467` `ClassifyException(ex)` is 1 parameter and needed no change — verified, not assumed"* —
  confirmed: `grep -n ClassifyException` on the baseline returns `467: var exDecision = _failureClassifier.ClassifyException(ex);`,
  a single line, exactly at 467.

The gate A3 exists to enforce — *if the hydrate path needs any member of the wider context, stop and
report* — is genuinely satisfied. The hydrate path constructs `RuleEvaluationContext` and nothing
wider. A3 is correctly VALIDATED, and the evidence is specific enough that I could re-check every
clause of it, which is the standard the contract asked for.

### F-OK8 — Claim 8: the D1 deviation is sound, and it was recorded rather than hidden. **[VERIFIED] + [JUDGEMENT]**

**Recorded, not hidden — four places, and it is the loudest thing in each:** `decisions.md` D1 carries
an "Amended during C1 execution" block with a delivered-shape table; `assumptions.md` A3 has a "One
amendment to the shape" paragraph; `execution_notes.md` has a "Deviation from the plan" section;
`defect_register.md` D4 files it as its own entry; and `CHANGELOG.md` states it in the consumer-facing
text. `state.json.steps[C1].deviation` records it as a field. This is the opposite of hidden, and it is
the part of C1 that best follows the repository's rules.

**The reasoning, checked against `ARCHITECTURE.md`'s actual text.** The dependency rule says:

> `Contracts/` may reference other concepts' `Contracts/` freely. … `Logic/` is where direction is
> enforced, and it runs one way: … **No concept's `Logic` may reference `Execution/Logic` or
> `Workflow/Logic`, except `Workflow → Execution`.** That is the enforceable part.

**[JUDGEMENT]** The conclusion is right; the citation is slightly stronger than the source.
`ARCHITECTURE.md` grants `Contracts/ → Contracts/` and separately constrains `Logic/ → Logic/`. It
never writes the sentence *"`Contracts/` may not reference `Logic/`"*. So the artifacts' phrasing —
`assumptions.md` A3 *"which permits only `Contracts/` → other concepts' `Contracts/`"*, and the
CHANGELOG's *"a `Contracts/` record **may not** depend on another concept's `Logic/`"* — converts a
permission into an exclusive prohibition. Strictly, the direction is **unsanctioned**, not
**forbidden**. `defect_register.md` D4's own title gets this exactly right (*"sanctions no
`Contracts/` → `Logic/` direction"*), so the precise wording already exists in the task; the CHANGELOG
just didn't use it.

I am **not** raising this as a finding against the decision, because:

1. The decision is correct on the document's own logic. `Contracts/` is described as "a shared
   vocabulary" whose whole point is that it carries no behaviour — a vocabulary type holding a live
   mutable tracker from another concept's `Logic/` defeats that, independent of any arrow diagram.
2. It is the conservative direction. Refusing to introduce an unsanctioned edge during a
   parameter-count refactor is right whether the edge is forbidden or merely unlisted.
3. The cost is one parameter. `ClassifyResponse` at 3p is compliant, and all five targeted violations
   still close — which I verified, not assumed.
4. The alternative would have deepened a cycle `ARCHITECTURE.md` already flags as unresolved:
   `Resilience/Logic → Pagination/Logic` via `EngineFailureClassifier` → `CursorRecoveryTracker`, half
   of the documented `Pagination ↔ Resilience` 2-cycle. D4 notes this and leaves it open, correctly —
   it is phase-3 work.

*"A parameter-count fix that introduces a layering violation is not a fix"* is the right instinct and
the right call. Only the CHANGELOG's "may not" should soften to match D4's own wording.

### F-OK9 — Are the new records coherent domain types, or grab-bags? **[JUDGEMENT], with the usage measured**

Member-by-member read sites, **[VERIFIED]** by reading every use in the class:

| member | read by |
|---|---|
| `ResponseSnapshot.StatusCode` | `Matches` (status, status_range), `IsExpiredCursor`, `IsSuccess` |
| `ResponseSnapshot.ResponseBody` | `Matches` (×3), `BuildDecision` (×2), `IsExpiredCursor` |
| `ResponseSnapshot.Headers` | `BuildDecision` only |
| `RuleEvaluationContext.ErrorHandling` | `EvaluateRules`, `IsSuccess` |
| `RuleEvaluationContext.PageState` | `BuildDecision`, `IsExpiredCursor` |
| `RuleEvaluationContext.MaxInProcessDelay` | `BuildDecision` only |
| `ClassificationContext.Rules` | `EvaluateRules`, `IsExpiredCursor`, `IsSuccess` |
| `ClassificationContext.Success` | `IsSuccess` only |
| `ClassificationContext.RecoveryConfig` | `IsExpiredCursor` only |

**My position on `ResponseSnapshot.Headers`: acceptable, and the criterion is being read at the wrong
level.**

The success criterion is *"every new record's members are used in full by every **consumer that
receives it**"*, and `constraints.md` grounds it in a specific prior defect — the note at
`WorkflowRunner.cs:442-455`. The consumers that *receive* `ResponseSnapshot` across a boundary are the
two public entry points, `ClassifyResponse` and `MatchRule`. Both, across their execution, read all
three members: `Matches` reads status and body, `BuildDecision` reads body and headers,
`IsExpiredCursor` reads status and body. Neither entry point ignores a member.

The operative test for a grab-bag is whether a **caller is obliged to synthesise a value it has no
opinion about**. Neither is: at `IntegrationEngine.cs:571-577` `statusCode`, `responseBody` and
`responseHeaders` are computed adjacently and unconditionally from one `HttpResponseMessage`, and at
`:1820-1821` likewise. All three always exist together because they are three views of one HTTP
response. That is a coherent domain type, and `ResponseSnapshot` is the right name for it.

Applying the criterion to **private static helpers** instead would require each helper to receive only
the fields it reads — which is precisely how a 9-parameter `ClassifyResponse` gets built. Rule 5 exists
to prevent that, so an interpretation that mandates it cannot be the intended one.

The same reasoning covers `MaxInProcessDelay` (read only by `BuildDecision`), `Success` and
`RecoveryConfig` (one reader each). By a per-helper reading, **six of nine members** would be
violations and no record would be constructible at all.

**Where a real partial-use does exist, it is `RuleEvaluationContext` at the hydrate site** — 2 of 3
members are placeholders (F3). Pre-existing, materially reduced by the composition (6 placeholders → 2),
and the only way to reach 0 would be a fourth record for a single call site, which is over-decomposition.
**Acceptable; the doc claiming 0 is not** (F3).

**On composition generally:** D1's core judgement is vindicated. The hydrate site constructs
`RuleEvaluationContext` and never touches `Success`/`RecoveryConfig`/`Recovery`, exactly as A3
predicted. A flat 9-member carrier would have forced three more nulls there. Composition was the right
shape.

### F-OK10 — Structural rules. **[VERIFIED]**

One top-level type per file, each named after its type, each in its own concept's `Contracts/Models/`
(`Resilience/Contracts/Models/`) — not a shared bucket. All three are `internal`, per the default-to-
`internal` policy; nothing widened visibility for testability. Namespace is
`…Engine.Resilience` (concept, not folder), matching `ARCHITECTURE.md`'s "Namespace is the concept"
rule. `using` directives are `Contracts` → `Contracts` only (`Pagination` for `PaginationState`/
`CursorRecoveryConfig`, `Definition` for `SuccessConfig`). All three carry real XML docs explaining
*why*, including the struct choice, which is justified by `FailureDecision` being
`internal readonly record struct` — **[VERIFIED]** at `FailureDecision.cs:11`.

### F-OK11 — Documentation gates (claim 9). **[VERIFIED]** — three of four clean

| gate | status |
|---|---|
| `CHANGELOG.md` updated in working tree | ✓ present, "Changed" + "Fixed" entries — but see F2 |
| `progress_log.md` exists and is current | ✗ exists, but records a nonexistent SHA and a review that had not run (**F1**) |
| `state.json` step status correct | ✓ step C1 `awaiting_review` / `commitSha: null` correct; top-level fields stale (F5) |
| `defect_register.md` records findings | ✓ four entries, D1 FIXED + D2/D3/D4 ACCEPTED, each with a stated reason — but D1's reason is wrong (F2) |

D8's handling of the "in the same commit" directive is sound and correctly surfaced: `.gitignore`
does ignore `ai/active/` (**[VERIFIED]** — `ai/active/` and `ai/done/` under "Agent task state"), so
the directive is literally unsatisfiable for `progress_log.md`, and D8 flags it for the operator rather
than silently redefining it. That is the right handling of a contradictory constraint. Which makes F1
worse rather than better: the file that was deliberately exempted from the commit gate is the one that
went on to record a commit that does not exist.

---

## What I could not check, and why

1. **Mutations 1 and 2 were not re-run.** I re-ran only mutation 3, the one the caller named and the
   only one with a claimed *survival* and a new test attached. The two "killed" claims cite specific
   tests (`Http200WithErrorBody_DefersWithBodySourcedDelay` at `EngineErrorRuleTests.cs:352` and
   `ExpiredCursorMidPagination_RestartsFromWatermark_NoDuplicates`) and I confirmed both tests **exist**
   and plausibly exercise those paths — `:352` asserts a 200-with-error-body defers, which does require
   rules to be checked before success; but I did not apply either mutation. Two ~90-second suite runs
   would settle it. Note `execution_notes.md` cites the first as `EngineErrorRuleTests.cs:339`, whereas
   the `[Fact]` is at `:351` and the method at `:352` — `:339` is inside that test's YAML constant. Off
   by a few lines, not wrong about which test.
2. **Determinism.** Single `dotnet test` run only (plus two in the throwaway worktree). The ≥12-run trx
   gate is an end-of-task obligation, not a C1 gate. Relevant risk on record:
   `ARCHITECTURE.md` documents ~2 failures per 25 runs from a millisecond-precision NDJSON spill
   filename — though the spill was removed on this branch's predecessor, so that specific cause may be
   gone. The new test uses only 10/40 ms values, which is tight but involves no cross-test filename
   collision; I saw no flake in three runs, which is not enough to claim anything.
3. **Rule 1's exact counting rule.** I reproduced that rule 1 is **unchanged** by diffing the full list
   of bodied members under `src/**/Contracts/` before and after — byte-identical, 9 raw entries. Which
   4 of those 9 the baseline's "5" excludes (most likely the `IExecutionSink` default-interface members)
   I did not reverse-engineer, because the delta is 0 either way and C1 does not touch rule 1.
4. **Whether anything else in the session is mid-edit.** F8 shows concurrent writes to `src/` during
   this review. I verified the tree matches `c1.diff` at the moment I finished, but I cannot guarantee
   it still does when this is read. Re-run `git diff` before committing.
5. **The 52 `IPaginator` test call sites and A1/A2.** Out of scope for C1; C2's and C3's gates.

---

## Bottom line

The engineering is sound and the hard part was done properly: A5's warning was taken seriously, the
mutation testing was real, it found a genuine pre-existing coverage hole, and the test written to close
it has teeth — I reproduced all of that rather than taking it on trust. The D1 deviation was the right
call and was surfaced loudly in five places. Scope held exactly.

What lets it down is prose written ahead of fact. **F1** invents a SHA and a completed review; **F2**
publishes a wrong reason in a consumer-facing CHANGELOG for a coverage gap that was itself discovered
by careful work. Both are cheap to fix and neither touches the code. Fix F1 and F2 before the commit
lands; F3–F6 in the same pass since they are one sentence each.

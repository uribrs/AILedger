# Code review — C1-a: `EngineFailureClassifier` parameter-object decomposition

Lens: correctness and behaviour preservation. The author's stated intent is a pure restructuring with
identical runtime behaviour plus one added test.

Tree state: restored exactly as found. Verified with `git status --short` (4 modified, 3 untracked —
unchanged from the start) and by diffing `git diff EngineFailureClassifier.cs` byte-for-byte against
the reviewed `c1.diff` hunk. `dotnet build` 0 warnings / 0 errors; `dotnet test` 710/710 pass.

---

## Verdict

**Behaviour is preserved.** The classifier diff is a mechanical substitution: every one of the nine
former arguments reaches the identical comparison, and the classification chain's order is unchanged.
I found no Critical and no Major defect.

Three Minor findings, all about the *provability* rather than the correctness of the change, plus two
Nits.

### Argument trace (all nine)

| former parameter | former consumers | now read as | same? |
|---|---|---|---|
| `statusCode` | `IsExpiredCursor`, `Matches` (×2), `IsSuccess` | `response.StatusCode` | yes |
| `responseBody` | `IsExpiredCursor`, `Matches` (×3), `BuildDecision` (×2) | `response.ResponseBody` | yes |
| `headers` | `BuildDecision` → `ResolveRuleDelay` (×2) | `response.Headers` | yes |
| `success` | `IsSuccess` | `context.Success` | yes |
| `errorHandling` | `EvaluateRules`, `IsSuccess` | `context.Rules.ErrorHandling` (both) | yes |
| `recoveryConfig` | null-check, `IsExpiredCursor` | `context.RecoveryConfig is { } recoveryConfig` | yes |
| `recovery` | null-check, `.Watermark` | unchanged 3rd parameter | yes |
| `pageState` | `IsExpiredCursor`, `BuildDecision` | `context.Rules.PageState` (both) | yes |
| `maxInProcessDelay` | `BuildDecision` cap comparison | `context.Rules.MaxInProcessDelay` | yes |

`recoveryConfig is not null` → `is { } recoveryConfig` is equivalent for a reference type.
`PaginationState` is a **class** (`Pagination/Contracts/Models/PaginationState.cs:6`), so storing it in
a `readonly record struct` keeps reference semantics: `context.PageState.SamePageRetries` at
`EngineFailureClassifier.cs:200` still reads the live counter the loop increments at
`IntegrationEngine.cs:616`. Had it been a struct this refactor would have frozen the retry budget at 0
— it is not, so it does not.

Both records are constructed fresh **inside** the page loop (`IntegrationEngine.cs:581-586`), so no
staleness window exists.

Positional-swap safety: all three records have pairwise-distinct member types, so no argument can be
silently transposed at a call site. Confirmed for `ResponseSnapshot` (`int`/`string`/dictionary),
`RuleEvaluationContext` (`ErrorHandlingConfig?`/`PaginationState`/`TimeSpan?`) and
`ClassificationContext` (`RuleEvaluationContext`/`SuccessConfig?`/`CursorRecoveryConfig?`).

### The added test does pin the cap — verified by mutation

`BodyThrottle_WhenVendorDelayExceedsTheInProcessCap_ExternalizesWithoutRetrying`
(`EngineErrorRuleTests.cs:238`) asserts what its name says, and it fails if the named behaviour
regresses. How I determined it:

- Replaced `EngineFailureClassifier.cs:199` with `var withinCap = true;` → **1 failure, 709 pass**, and
  the single failure is the new test. This independently confirms the CHANGELOG's claim that the whole
  suite was green with the comparison hard-coded.
- Replaced it with `var withinCap = false;` → **3 failures**:
  `BodyThrottle_RetriesSamePageInProcess_ThenSucceeds`,
  `BodyThrottle_ExhaustsInProcessBudget_ThenExternalizes`, and
  `EngineControlStateTests.Capture_AdoptsBodyValue_AndRetriesSamePageWithIt`. So the CHANGELOG's
  "verified in both directions" is accurate.

The test is also correctly isolated from the Polly path: `DefenderThrottleYaml` declares
`error_handling.rules` but no `error_handling.retry`, so `RetryPolicyFactory.BuildRetryPolicy` returns
null (`RetryPolicyFactory.cs:58-59`) and `Create` yields a no-op policy. The 10 ms cap therefore
reaches only `BuildDecision`, which is what the test intends to pin. The engine's own sleep at
`IntegrationEngine.cs:623-624` is uncapped, so the classifier's comparison is the sole guard against a
30-minute in-process wait — the test is pinning the right line.

CHANGELOG parameter-count claims all verified against `git show HEAD:...`: `ClassifyResponse` 9→3,
`MatchRule` 6→2, `EvaluateRules` 6→2, `BuildDecision` 5→3, `IsExpiredCursor` 4→3.

Consumer check (CLAUDE.md: "find its consumers ... grep it before, not after"): grepped
`cymulate-integration-adapters` for `ClassifyResponse`, `MatchRule`, `EngineFailureClassifier`,
`ResponseSnapshot`, `ClassificationContext`, `RuleEvaluationContext`. The adapter carries a **vendored
copy** of the engine at
`src/Cymulate.Integration.Adapters/Collectors/YamlCollector/Cymulate.Integration.Yaml.Engine/Retry/EngineFailureClassifier.cs`,
not a package reference. Nothing outside this repository binds these signatures, so "no host call site
changed" holds. Note the two trees are already divergent (`Retry/` there vs `Resilience/` here); this
change widens the gap by three files, which the eventual reconciliation will have to absorb.

---

## Critical

None.

## Major

None. I looked specifically for a value that stopped reaching its comparison, an ordering flip, a
struct-copy that froze mutable state, and a nullability change with a runtime consequence. None of the
four is present.

## Minor

### 1. The precedence the comment calls load-bearing is protected by no test

`src/Cymulate.Integration.Yaml.Engine/Resilience/Logic/EngineFailureClassifier.cs:31-45`

The comment states the cursor-recovery check is "Kept ahead of the rules/success checks to preserve the
shipped precedence for the watermark restart." The diff does preserve it, but nothing enforces it. Two
mutations, both leaving **710/710 green**:

1. Swapped the recovery block and the `EvaluateRules` block.
2. Moved the recovery block after the generic success check
   (`if (IsSuccess(...)) return None;` first, recovery second, `Fail` last).

Why nothing notices: the only definition in the corpus that uses `cursor_recovery` is
`cymulate-magic-integration/integrations/crowdstrike-falcon.yaml`, operation `get_findings_for_hosts`
(`expiry_status: 404`), and its `error_handling` declares `retry` and `fail` but **no `rules`** — so
`EvaluateRules` returns null there. And 404 is non-2xx against `success.status_codes: [200]`, so the
success check falls through to recovery either way. `EngineCursorRecoveryTests` inherits both
properties.

Scenario in which it bites: a definition that configures `cursor_recovery` on a status that an
`error_handling.rules` predicate also matches — e.g. `expiry_status: 404` alongside a
`status: [404], body_contains: "NotFound" → action: skip` rule for stale references, which is exactly
the shape `SkipYaml` in the test file models. Shipped order re-anchors to the watermark and continues
the scroll; flipped order ends collection early and silently drops the remainder. Silent data loss with
`Success = true`.

Fix: one test in `EngineCursorRecoveryTests` whose YAML carries both a `cursor_recovery` on 404 and a
`status: [404] action: skip` rule, asserting the watermark restart wins (records from after the restart
are published, `CompleteCalled` true, more than one page fetched). Cheap, and it converts a comment
into a guarantee.

This is not a regression introduced here. It is flagged because the diff re-asserts the invariant in
prose while leaving it unpinned — the exact pattern CLAUDE.md's "before claiming what a test covers,
break the behaviour and watch it fail" exists to catch.

### 2. `readonly record struct` silently removes the non-null guarantee the parameter list gave

`src/Cymulate.Integration.Yaml.Engine/Resilience/Contracts/Models/RuleEvaluationContext.cs:16-19`
`src/Cymulate.Integration.Yaml.Engine/Resilience/Contracts/Models/ClassificationContext.cs:22-25`

`PaginationState PageState` is a non-nullable reference member of a struct. C# does not apply nullable
analysis to struct default-initialisation, so `default(RuleEvaluationContext)`,
`default(ClassificationContext)` and `new ClassificationContext(default, null, null)` all compile with
**zero warnings** and yield `PageState == null`. That NREs at `EngineFailureClassifier.cs:200`
(`context.PageState.SamePageRetries`) on the retry path and at `:282` (`state.CursorValue`) on the
cursor-expiry path.

Before the change, `PaginationState pageState` was a non-nullable reference *parameter*: passing null
produced CS8625. `Nullable` is `enable` repo-wide (`Directory.Build.props`) though
`TreatWarningsAsErrors` is not set, so it was a warning rather than an error — but the build currently
sits at 0 warnings, so that signal was live and is now gone entirely.

No current call site does this; both construct the records explicitly. This is a latent hazard the
positional parameters did not have, and the request asked specifically about it.

Fix, cheapest first: make the classifier not depend on the guarantee —
`(context.PageState?.SamePageRetries ?? 0) < maxRetries` at `:200` and a null check before `:282`. Do
not add a validating constructor to a per-page struct.

### 3. The new test omits the `FailAsync` assertion its two sibling defer tests carry

`tests/Cymulate.Integration.Yaml.Engine.Tests/Execution/EngineErrorRuleTests.cs:250-256`

The test asserts `!result.Success`, the 40 ms `RetryAfter`, `!sink.CompleteCalled` and one HTTP
request, but not `!sink.FailCalled`. Its siblings do:
`BodyErrorCode_DefersWithFixedDelay_NoTerminalSinkSignals:154` ("a deferred wait is not an error") and
`Http200WithErrorBody_DefersWithBodySourcedDelay:364`.

Scenario: a change that routes the above-cap escalation through `TryFailSinkAsync` before returning the
deferred result — a plausible edit, since the neighbouring `FailureAction.Fail` branch at
`IntegrationEngine.cs:674` does exactly that. The test stays green while the host is told the run
failed instead of scheduling a resume, which for the ISB means a failed-run record rather than a
checkpointed retry. The half of "a defer is neither error nor completion" that this test guards is the
completion half only.

Fix: `Assert.False(sink.FailCalled, "a deferred wait is not an error");`

## Nit

### 4. The context captures a reference to a local the loop reassigns

`src/Cymulate.Integration.Yaml.Engine/Execution/Logic/IntegrationEngine.cs:583`

`RuleEvaluationContext` holds a reference to `pageState`, and the loop reassigns that local at
`IntegrationEngine.cs:792` (`pageState = AdvancePagination(...)`, which returns
`paginator.UpdateState(...)` — free to return a different instance). Today the record is built fresh per
iteration so this is correct. But a struct named "context" built from three cheap reads is an obvious
target for someone hoisting it above the loop as an allocation win, and that hoist would silently make
the retry budget and the cursor-expiry check read a stale `PaginationState`. With the old positional
signature there was nothing to hoist. A one-line comment at the call site ("rebuilt per iteration:
`pageState` is reassigned by `AdvancePagination`") closes it.

### 5. Non-nullable `ResponseBody` next to a retained `is null` hedge

`src/Cymulate.Integration.Yaml.Engine/Resilience/Contracts/Models/ResponseSnapshot.cs:12-15`,
`Resilience/Logic/EngineFailureClassifier.cs:107`

`IsExpiredCursor` moved from `string? responseBody` to reading the non-nullable
`response.ResponseBody`, so the record now asserts non-nullability across the whole classifier — while
`Matches` still hedges with `response.ResponseBody is null`. No runtime consequence: `IsExpiredCursor`
keeps its `string.IsNullOrEmpty` guard, and both call sites supply
`await ...Content.ReadAsStringAsync(ct)` (`IntegrationEngine.cs:571` and `:1820`), which is non-null.
The hedge was already annotation-dead before this change (the old `Matches` parameter was also plain
`string`), so nothing regressed — but the record makes the inconsistency visible and it is worth
resolving one way or the other. Note that finding 2's `default(...)` hole is the one path that can
actually produce a null body here.

---

## Method

- Read the full diff, the three new record files, `EngineFailureClassifier.cs`, both call sites in
  `IntegrationEngine.cs`, `EngineErrorRuleTests.cs`, `PaginationState.cs` and `RetryPolicyFactory.cs`.
- Diffed the current classifier against `git show HEAD:...` to confirm the substitution is mechanical.
- Four mutations, each followed by a full `dotnet test` run and then restored from a backup copy:
  `withinCap = true`; `withinCap = false`; recovery/rules order swapped; recovery moved after the
  success check.
- Grepped the read-only consuming adapter for every changed symbol.
- Grepped `cymulate-magic-integration/integrations` (~279 definitions) for `cursor_recovery` and read
  the single hit's `error_handling` block to establish whether finding 1 is reachable in production
  today. It is not — hence Minor, not Major.
- Restored the tree and re-verified: `git status --short` identical to the starting state, classifier
  byte-identical to the reviewed diff, 710/710 tests pass.

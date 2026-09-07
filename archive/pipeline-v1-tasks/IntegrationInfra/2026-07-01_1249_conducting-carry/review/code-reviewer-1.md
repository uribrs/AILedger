# Code Review — Conducting concern carry + UnknownFlowRetryPolicy

**Stack:** C# / .NET 8, xUnit + Moq, Polly v8.
**Change type:** shared library / public contract code (SDK-shaped) + test-only code + build config.
**Risk level:** Medium–High. This is event-driven run/resume orchestration with retry, cancellation, partial-success, and checkpoint-resume semantics. The logic is a relocation of pre-existing production code into a new namespace layout, so most behavior is not new; scrutiny is concentrated on what the relocation newly introduces: the public API surface, XML-doc adequacy, the `UnknownFlowRetryPolicy` Polly factory, the test project, nullability, and structural consistency.

**Build/test verification:** `dotnet build` of the test project succeeds with 0 errors and 0 relevant warnings (only pre-existing NU1507/NU1900 nuget-source warnings). `dotnet test` → 30 passed, 0 failed.

Verdict: no Blockers. The relocation is clean and the new factory/tests are sound. Findings are Major-and-below, mostly consistency and doc-surface items.

---

## Critical / Blocker

None.

---

## Major

### M1. Public API surface is broad, mutable-by-init, and under-validated at the type boundary
**Files:**
- `src/IntegrationInfra/Conducting/Bus/Models/AdapterBusEntrypointDefinition.cs`
- `src/IntegrationInfra/Conducting/Collectors/DelegateCollectorBusEntrypointSource.cs`
- `src/IntegrationInfra/Conducting/Collectors/ICollectorBusEntrypointSource.cs`

**Problem:** These are `public` contract types with ~14 `required` delegate members plus ~10 optional hooks each. The invariants that actually matter (e.g. `VendorName` non-blank) are enforced only imperatively and only at two call sites (`AdapterBusEntrypointRunner.ValidateInputs`, `CollectorBusEntrypointDefinitionBuilder.Build`). A consumer constructing `AdapterBusEntrypointDefinition<T>` directly and calling `RunAsync` gets the vendor-name guard, but any *other* future entrypoint that consumes the definition inherits none of the validation. The `required` keyword protects against *missing* delegates but not against *wrong* ones (e.g. a `GetFlowName` that returns null — handled defensively in `ExtractRequestAndFlowName` with `?? string.Empty`, good, but that is the only such guard).

**Impact:** The type is a wide, order-dependent construction contract. Correctness depends on every consumer replicating the same guard sequence. This is latent coupling, not a present bug.

**Recommendation:** Acceptable for now given this is a verbatim relocation and there is a single runner. Do **not** refactor as part of this carry. Flag for follow-up: if a second entrypoint is added, centralize definition validation (a `Validate()` on the record, or funnel all construction through the builder). Local patch, deferrable.

### M2. Nullability variance between source contract and definition — `SetCurrentPlatformEvent`
**Files:**
- `ICollectorBusEntrypointSource.cs:41` → `Action<PlatformEvent?>? SetCurrentPlatformEvent`
- `DelegateCollectorBusEntrypointSource.cs:31` → `Action<PlatformEvent?>? SetCurrentPlatformEvent`
- `AdapterBusEntrypointDefinition.cs:36` → `Action<PlatformEvent>? SetCurrentPlatformEvent` (non-nullable arg)
- assigned in `CollectorBusEntrypointDefinitionBuilder.cs:111`

**Problem:** The source exposes the hook as accepting a *nullable* `PlatformEvent?`; the definition declares it accepting a *non-nullable* `PlatformEvent`. `Action<in T>` is contravariant, so assigning `Action<PlatformEvent?>` into `Action<PlatformEvent>` is a widening that the compiler allows (it does not even warn here — build is clean). At the call site (`AdapterBusEntrypointRunner.cs:65`) `effectiveEvent` is always non-null, so no NRE occurs today.

**Impact:** No runtime defect today. It is a signature inconsistency in a public contract that misrepresents the callee's own declared tolerance for null, and will confuse future implementers about whether they must null-check. Confirmed clean-compiling, so this is a design-surface smell, not a bug.

**Recommendation:** Align the two signatures — make the definition's `SetCurrentPlatformEvent` `Action<PlatformEvent?>?` (the runner only ever passes non-null, so widening the parameter is free and matches the source). Local patch, low effort. Since it is verbatim-relocation-adjacent, defer if the two shapes existed identically pre-move; fix if the mismatch is new.

### M3. XML-doc coverage is uneven across the new public surface
**Files (public types with no `<summary>`):**
- `AdapterPlatformEventFactory.cs:6` — `public static class`, public `ResolveForFlow` undocumented.
- `ConfigurationValidation.cs:4` — `public static class` + public `Validate`, undocumented.
- `CollectorGuards.cs:3` — `public static class`, both `EnsureReady` overloads undocumented.
- `CollectorTriggerParsing.cs` — class documented, but every public method (`NormalizeFlowName`, `GetAny`, `GetAnyNullable`, `TryParseUtcDateTime`, `TryParseBool`) undocumented.
- `CollectorCommonDependencies.cs:8` — public record + `Resolve`, undocumented.
- `AdapterBusEntrypointDefinition.cs` — most members undocumented (only `BuildFlowRetryPipeline` has a `<summary>`).
- `DelegateCollectorBusEntrypointSource.cs` — class documented, members not.
- `ICollectorBusEntrypointSource.cs` — several members undocumented (the optional hooks are documented; the required core members are not).

**Contrast:** `AdapterBusEntrypointRunner`, `UnknownFlowRetryPolicy`, `UnknownFlowRetryClassification`, and `CollectorResumeRunner` are documented well, including the load-bearing invariants and the Kernel/FaultGovernance split rationale.

**Impact:** Inconsistent discoverability on a library that is explicitly a reuse surface (the builder/source exist precisely to be implemented by many collectors). Not a correctness issue.

**Recommendation:** Add `<summary>` to the public members that are genuinely part of the reuse contract — at minimum the `ICollectorBusEntrypointSource<T>` required members and the `CollectorTriggerParsing` helpers (their parsing semantics, e.g. "returns null for blank or unrecognized", are behavior a caller must know). The trivial one-liner helpers (`CollectorGuards`, `ConfigurationValidation`) are self-evident and lower priority. Local, incremental; not merge-blocking but appropriate to close before this becomes a stable API.

---

## Minor

### m1. `UnknownFlowRetryPolicy` — Polly v8 usage is correct; two small notes
**File:** `src/IntegrationInfra/FaultGovernance/Logic/UnknownFlowRetryPolicy.cs`

The factory is correct:
- `MaxRetryAttempts = MaxRetries` (3), with `MaxAttempts` (4) = initial + retries, and the Kernel static ctor asserts `RetryDelays.Length == MaxRetries` — a good invariant guard that keeps this factory honest.
- `DelayGenerator` indexes `RetryDelays[AttemptNumber]` with a clamp to the last element; `AttemptNumber` is zero-based per Polly v8, so attempts map 0→30s, 1→60s, 2→120s. Correct, and the clamp is defensive dead-code given `MaxRetryAttempts == RetryDelays.Length`.
- `OnRetry` logs `AttemptNumber + 1` as the human-facing "Retry N/3" — correct for zero-based `AttemptNumber`.
- `ShouldHandle` re-checks `IsUnknownRetryCandidate`, delegating classification single-homed to the Kernel. Good — no duplicated classification logic.

Notes (no action required):
1. `ShouldHandle` returns `false` when `args.Outcome.Exception is null` (successful outcome) — correct, Polly won't retry a success anyway, but the explicit `ex != null` guard is fine.
2. The pipeline is rebuilt per flow execution (`AdapterBusFlowExecutor.cs:16`) rather than cached. Given fixed delays and no `HttpClient`/handler state, this is a cheap allocation on a cold-ish path (once per run, not per page), so caching would be premature optimization. Leave as-is.

### m2. `AdapterBusFlowExecutor` — potential double-log of the exhausted-retry error
**File:** `src/IntegrationInfra/Conducting/Bus/Logic/AdapterBusFlowExecutor.cs:37-46`

After Polly exhausts retries and rethrows, the outer `catch (Exception ex) when (IsUnknownRetryCandidate(ex))` logs "Unknown-error retries exhausted" and rethrows to the runner, whose top-level `catch (Exception)` then calls `HandleUnhandledExceptionAsync` (which may `OnUnhandledException` + publish error). Meanwhile `UnknownFlowRetryPolicy.OnRetry` already logged a warning per retry. Net: for an unknown failure you get N retry-warnings + 1 error-log here + downstream handling logs. This is intentional observability layering, not a bug, but it is verbose. No change required; noting for anyone triaging log volume.

### m3. `CollectorTriggerParsing.GetAny` returns `current` (possibly empty) on no match
**File:** `CollectorTriggerParsing.cs:36-52`

`GetAny` returns the (possibly empty/whitespace) `current` when no fallback key matches, whereas `GetAnyNullable` returns `null`. The test `GetAny_PrefersCurrent_ThenFallsBackToKeys` covers the found-fallback case but not the all-missing case (would return `""`). Behavior is reasonable (non-nullable contract must return *something*), but the empty-string-on-miss path is untested. Minor test-coverage gap. Add one assertion if cheap.

### m4. `NormalizeFlowName(string value)` accepts null despite non-nullable parameter
**File:** `CollectorTriggerParsing.cs:17-19`

Parameter is declared `string value` (non-nullable) but the body defends with `(value ?? string.Empty)`. Under `<Nullable>enable</Nullable>` this defensive null-coalesce is either dead code (if callers honor the contract) or a signal the parameter should be `string?`. Pick one: if null is a real input, type it `string?`; if not, drop the coalesce. Cosmetic.

---

## Nit

### n1. `catch when (ex is ArgumentException or InvalidOperationException)` narrows silently
**File:** `AdapterBusEntrypointSetup.cs:73`

`ConfigureFromEventAsync` only treats `ArgumentException`/`InvalidOperationException` from `SetConfiguration` as a configuration failure; any other exception type propagates to the runner's generic handler. This is a deliberate, defensible choice (only "expected" config errors become `INVALID_CONFIGURATION`), but the set of caught types is a hidden assumption. A one-line comment stating "other exceptions are treated as unhandled failures by design" would help. Verbatim-relocation, no action.

### n2. Redundant local-function-then-null-check pattern in the builder
**File:** `CollectorBusEntrypointDefinitionBuilder.cs:25-53`

Each hook does `var x = source.GetX(); if (x is null) x = DefaultX;`. This is `source.GetX() ?? DefaultX` five times. The current form is readable and the local functions must be declared regardless; collapsing to `??` is a stylistic micro-choice. No action.

### n3. `PureUnitTests.cs` file name vs. contents
**File:** `tests/IntegrationInfra.Conducting.Tests/PureUnitTests.cs`

The file `PureUnitTests.cs` contains six distinct public test classes (`CollectorTriggerParsingTests`, `ConfigurationValidationTests`, `CollectorGuardsTests`, `CollectorResultPayloadTests`, `AdapterPlatformEventFactoryTests`, `UnknownFlowRetryPolicyTests`). One-type-per-file would aid navigation, but co-locating small pure-function test classes is a common and defensible convention. No action.

---

## Test Quality Assessment

**The two `AdapterBusEntrypointRunnerInvariantTests` are meaningful and non-tautological.** Both drive the real `AdapterBusEntrypointRunner.RunAsync` end-to-end against a recording Moq stub of `IAdapterExecutionContext` and assert observable publish behavior, not internal state:

- `PartialSuccessWins_FailureIsSuppressed_WhenPartialResultIsBuilt`: `CollectAssetsAsync` throws `InvalidOperationException` → legacy executor rethrows (unknown) → empty Polly pipeline (no retry) → runner outer `catch(Exception)` → `HandleUnhandledExceptionAsync` builds partial success. Asserts the partial `AdapterResult` is returned, `CompletionRequest` is published (`Times.AtLeastOnce`), and **no `ErrorRequest`** (`Times.Never`). This genuinely exercises the "partial published before failure; failure path suppressed" invariant. Real behavior, not a mirror of the implementation.

- `Cancellation_IsNackNotFailure_PublishesNothing`: `CollectAssetsAsync` throws `OperationCanceledException`. Because `IsUnknownRetryCandidate` returns false for OCE, the retry filter does not engage and the dedicated `catch(OperationCanceledException)` in the runner fires. Asserts result is not-success and **neither** `CompletionRequest` **nor** `ErrorRequest` is published. This is exactly the NACK+requeue+resume invariant. Non-tautological.

Both correctly reach the real `AssetsFlow` dispatch path (`GetFlowName => AssetsFlow`, which `IsSupportedCollectorFlow` accepts).

**The Moq stub is sound.** `CreateContext()` stubs `CreateProgressContext` to build a real `AdapterProgressContext.FromPlatformEvent`, and both `PublishAsync` overloads to return `PublishResult.Ok()`. The `BuildFlowRetryPipeline = _ => new ResiliencePipelineBuilder().Build()` override is a good touch — it keeps the tests synchronous and avoids the default 30/60/120s unknown-retry backoff that would otherwise make the partial test wait minutes. Comment on line 49-50 explains why. This is deliberate and correct.

**`PureUnitTests.cs`** covers the pure helpers well: trigger parsing (bool/date/flow-name/fallback), config validation success+failure-with-factory, guards, result-payload key mapping, platform-event storageUrl promotion (including the "does not overwrite existing" case), and the retry classification (excludes OCE/HttpRequestException/TimeoutException; true for generic; `MaxAttempts == 4`). These assert real input→output behavior. Coverage gap noted in m3 (GetAny all-missing path).

**Not covered (acceptable for this carry):** success path publishing completion-before-result-build; the strategy-executor path (`ResilienceStrategy` non-null) in the runner; classified-flow-exception publishing. The two invariant tests target the highest-value behaviors deliberately; broader coverage is a reasonable follow-up, not a merge blocker.

---

## Structure / Consistency Observations (no action)

- Namespace layout is coherent: `Conducting.Bus.{Logic,Models}`, `Conducting.Collectors.{Guards,Triggers,Validation,Recovery}`, `Conducting.DependencyInjection`. Internal executors are `internal static`; contract/definition/source/factory types are `public`. The Kernel (Polly-free classification) / FaultGovernance (Polly pipeline) split is clean and well-documented in both `UnknownFlowRetryPolicy` and `UnknownFlowRetryClassification` — the relocation preserves call-site compatibility (`.CreatePipeline` / `.IsUnknownRetryCandidate` / `.MaxAttempts`) exactly as the remarks claim.
- `using` ordering is slightly inconsistent (some files lead with `Cymulate.IntegrationInfra.FaultGovernance` before `Cymulate.IntegrationInfra.Conducting.*`, e.g. `AdapterBusEntrypointRunner.cs:1-2`), but ImplicitUsings is off in src and this is cosmetic.
- `Directory.Packages.props`: `Moq 4.20.72` added centrally with an inline comment scoping it to the Conducting runner-invariant tests — appropriate. `.slnx` correctly registers the new test project alongside the existing test projects.
- Resume path (`CollectorResumeRunner` + strategy/legacy executors) mirrors the fresh-run structure faithfully, reusing the same failure-decision/partial-success/publish machinery. The parallelism between `AdapterBusStrategyFlowExecutor` and `CollectorResumeStrategyExecutor` is deliberate (fresh vs. resume, `IsResume` flag differs); some duplication exists but factoring it would couple two independently-evolving lifecycles — not worth it now.

---

## Summary of severities

| Severity | Count | Items |
|---|---|---|
| Critical/Blocker | 0 | — |
| Major | 3 | M1 wide construction contract (defer), M2 nullability variance on `SetCurrentPlatformEvent`, M3 uneven public XML-doc coverage |
| Minor | 4 | m1 Polly notes (no action), m2 double-log verbosity, m3 `GetAny` miss-path untested, m4 `NormalizeFlowName` null defense vs signature |
| Nit | 3 | n1 narrowed catch comment, n2 `??` collapse, n3 test file naming |

The carry is technically safe. Tests pass (30/30), build is clean, the Polly factory is correct and correctly delegates classification to the Kernel, and the two invariant tests assert real, load-bearing behavior. No merge blocker. The only items worth acting on before this API stabilizes are M2 (trivial signature alignment) and M3 (doc the reuse-contract members); M1 is a deferred watch-item for when a second entrypoint appears.
